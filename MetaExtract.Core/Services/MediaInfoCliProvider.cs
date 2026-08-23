using System.Diagnostics;
using System.Globalization;
using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>
/// Exception levée quand l'exécutable mediainfo.exe configuré est
/// introuvable ou invalide. Distincte des erreurs par-fichier (qui sont
/// stockées dans VideoFileRecord.Error) car elle bloque tout le scan.
/// </summary>
public sealed class MediaInfoNotConfiguredException : Exception
{
    public MediaInfoNotConfiguredException(string message) : base(message) { }
}

/// <summary>
/// Implémentation de <see cref="IMediaInfoProvider"/> qui s'appuie sur
/// l'exécutable MediaInfo CLI (mediainfo.exe) déjà installé sur la
/// machine de l'utilisateur (chemin fourni via les paramètres de
/// l'application, cf. <see cref="AppSettings.MediaInfoExecutablePath"/>).
///
/// On invoque `mediainfo.exe --Output=file://&lt;modele&gt; "&lt;fichier&gt;"`,
/// exactement le mécanisme de la commande manuelle
/// `MediaInfo --Output=file://modele.txt "dossier\*" &gt;&gt; sortie.txt` :
/// contrairement à `--Output=JSON` (non reconnu par toutes les versions,
/// avec parfois un repli sur l'interface graphique par défaut), ce
/// mécanisme de template texte est supporté par toutes les versions de
/// MediaInfo et ne déclenche jamais d'interface graphique.
///
/// Important : le template DOIT être fourni via un fichier (une règle
/// "TypeDePiste;texte" par LIGNE PHYSIQUE). Passer plusieurs règles dans
/// un seul argument `--Inform=` ne fonctionne pas : MediaInfo ne retient
/// que la première règle et évalue tout le reste dans ce même contexte
/// (les champs vidéo/audio, invalides pour une piste General, ressortent
/// alors vides — seuls les champs General comme %Duration% fonctionnent).
///
/// Chaque ligne du template commence par un marqueur littéral
/// ("GEN|", "VID|", "AUD|") permettant d'identifier le type de piste à la
/// lecture, sans dépendre d'un format structuré (JSON/XML).
/// </summary>
public sealed class MediaInfoCliProvider : IMediaInfoProvider
{
    // Une ligne physique par type de piste : chaque règle "TypeDePiste;texte"
    // n'est émise par MediaInfo que pour les pistes du type correspondant
    // présentes dans le fichier. Le "\n" en fin de règle est la séquence
    // littérale (2 caractères) attendue par la syntaxe de template MediaInfo
    // pour terminer la ligne de sortie généré, pas un retour à la ligne réel
    // (les vrais retours à la ligne ci-dessous séparent les règles entre elles).
    private const string TemplateContent =
        "General;GEN|%Format%|%Duration%|%OverallBitRate%\\n\n" +
        "Video;VID|%Format%|%CodecID%|%Format_Profile%|%Width%|%Height%|%FrameRate%|%BitRate%|%BitDepth%|%DisplayAspectRatio%|%ScanType%|%ChromaSubsampling%\\n\n" +
        "Audio;AUD|%Format%|%CodecID%|%BitRate%|%BitRate_Mode%|%Channels%|%SamplingRate%|%Language%\\n\n";

    private static readonly string TemplateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MetaExtract", "mediainfo_template.txt");

    private readonly string _mediaInfoExecutablePath;

    public MediaInfoCliProvider(string mediaInfoExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(mediaInfoExecutablePath))
            throw new MediaInfoNotConfiguredException(
                "Aucun chemin vers mediainfo.exe n'est configuré. Ouvrez Paramètres pour indiquer l'emplacement de votre installation MediaInfo.");

        if (!File.Exists(mediaInfoExecutablePath))
            throw new MediaInfoNotConfiguredException(
                $"Le fichier '{mediaInfoExecutablePath}' est introuvable. Vérifiez le chemin configuré vers mediainfo.exe.");

        _mediaInfoExecutablePath = mediaInfoExecutablePath;

        // Toujours réécrit pour rester synchronisé avec TemplateContent
        // (coût négligeable, évite un template périmé après une mise à jour).
        Directory.CreateDirectory(Path.GetDirectoryName(TemplateFilePath)!);
        File.WriteAllText(TemplateFilePath, TemplateContent);
    }

    /// <summary>Vérifie que le chemin configuré pointe vers un exécutable exploitable, sans lancer de scan.</summary>
    public static bool TryValidate(string? path, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Aucun chemin renseigné.";
            return false;
        }
        if (!File.Exists(path))
        {
            errorMessage = "Le fichier indiqué n'existe pas.";
            return false;
        }
        errorMessage = "";
        return true;
    }

    public async Task<VideoFileRecord> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);

        try
        {
            string output = await RunMediaInfoAsync(filePath, cancellationToken).ConfigureAwait(false);
            return ParseIntoRecord(filePath, fileInfo, output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Erreur "métier" : le fichier reste dans les résultats avec un message d'erreur.
            return BuildBaseRecord(filePath, fileInfo, ex.Message);
        }
    }

    private async Task<string> RunMediaInfoAsync(string filePath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _mediaInfoExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add($"--Output=file://{TemplateFilePath}");
        psi.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Évite de laisser un mediainfo.exe orphelin tourner en arrière-plan si le scan est annulé.
        await using var killOnCancel = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
        });

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string stdOut = await stdOutTask.ConfigureAwait(false);
        string stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdOut))
            throw new InvalidOperationException($"mediainfo a retourné le code {process.ExitCode}: {stdErr}");

        return stdOut;
    }

    private static VideoFileRecord BuildBaseRecord(string filePath, FileInfo fileInfo, string? error = null) => new()
    {
        FileName = fileInfo.Name,
        FolderPath = fileInfo.DirectoryName ?? "",
        FullPath = filePath,
        Extension = fileInfo.Extension,
        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
        DateModified = fileInfo.Exists ? fileInfo.LastWriteTime : null,
        Error = error,
    };

    private static VideoFileRecord ParseIntoRecord(string filePath, FileInfo fileInfo, string output)
    {
        string? containerFormat = null;
        double? durationMs = null;
        long? overallBitRate = null;

        string? videoFormat = null, videoCodecId = null, videoProfile = null, videoBitDepth = null;
        string? aspectRatio = null, scanType = null, chromaSubsampling = null;
        int? width = null, height = null;
        double? frameRate = null;
        long? videoBitRate = null;
        bool videoCaptured = false;

        string? audioFormat = null, audioCodecId = null, audioBitRateMode = null, audioChannels = null, audioLanguage = null;
        long? audioBitRate = null, audioSampleRate = null;
        int audioTrackCount = 0;
        bool audioCaptured = false;
        bool generalCaptured = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', '\n', ' ', '\t');
            if (line.Length == 0) continue;

            if (line.StartsWith("GEN|", StringComparison.Ordinal))
            {
                var f = line.Substring(4).Split('|');
                containerFormat = Field(f, 0);
                // %Duration% (paramètre brut, hors "/String") est exprimé en millisecondes par MediaInfo.
                durationMs = ParseDouble(f, 1);
                overallBitRate = ParseLong(f, 2);
                generalCaptured = true;
            }
            else if (line.StartsWith("VID|", StringComparison.Ordinal))
            {
                if (!videoCaptured)
                {
                    var f = line.Substring(4).Split('|');
                    videoFormat = Field(f, 0);
                    videoCodecId = Field(f, 1);
                    videoProfile = Field(f, 2);
                    width = (int?)ParseLong(f, 3);
                    height = (int?)ParseLong(f, 4);
                    frameRate = ParseDouble(f, 5);
                    videoBitRate = ParseLong(f, 6);
                    videoBitDepth = Field(f, 7);
                    aspectRatio = Field(f, 8);
                    scanType = Field(f, 9);
                    chromaSubsampling = Field(f, 10);
                    videoCaptured = true;
                }
            }
            else if (line.StartsWith("AUD|", StringComparison.Ordinal))
            {
                audioTrackCount++;
                if (!audioCaptured)
                {
                    var f = line.Substring(4).Split('|');
                    audioFormat = Field(f, 0);
                    audioCodecId = Field(f, 1);
                    audioBitRate = ParseLong(f, 2);
                    audioBitRateMode = Field(f, 3);
                    audioChannels = Field(f, 4);
                    audioSampleRate = ParseLong(f, 5);
                    audioLanguage = Field(f, 6);
                    audioCaptured = true;
                }
            }
        }

        if (!generalCaptured && !videoCaptured && !audioCaptured)
            return BuildBaseRecord(filePath, fileInfo, "Aucune donnée retournée par MediaInfo pour ce fichier.");

        return new VideoFileRecord
        {
            FileName = fileInfo.Name,
            FolderPath = fileInfo.DirectoryName ?? "",
            FullPath = filePath,
            Extension = fileInfo.Extension,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            DateModified = fileInfo.Exists ? fileInfo.LastWriteTime : null,

            ContainerFormat = containerFormat,
            DurationMs = durationMs,
            OverallBitRate = overallBitRate,

            VideoFormat = videoFormat,
            VideoCodecId = videoCodecId,
            VideoProfile = videoProfile,
            Width = width,
            Height = height,
            FrameRate = frameRate,
            VideoBitRate = videoBitRate,
            VideoBitDepth = videoBitDepth,
            AspectRatio = aspectRatio,
            ScanType = scanType,
            ChromaSubsampling = chromaSubsampling,

            AudioFormat = audioFormat,
            AudioCodecId = audioCodecId,
            AudioBitRate = audioBitRate,
            AudioBitRateMode = audioBitRateMode,
            AudioChannels = audioChannels,
            AudioSampleRate = audioSampleRate,
            AudioLanguage = audioLanguage,
            AudioTrackCount = audioTrackCount,
        };
    }

    private static string? Field(string[] fields, int index) =>
        index < fields.Length && !string.IsNullOrWhiteSpace(fields[index]) ? fields[index] : null;

    private static long? ParseLong(string[] fields, int index)
    {
        var v = index < fields.Length ? fields[index] : null;
        if (string.IsNullOrWhiteSpace(v)) return null;
        // MediaInfo peut renvoyer des valeurs comme "5000000" ou "5000000.000"
        if (long.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var l)) return l;
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return (long)d;
        return null;
    }

    private static double? ParseDouble(string[] fields, int index)
    {
        var v = index < fields.Length ? fields[index] : null;
        if (string.IsNullOrWhiteSpace(v)) return null;
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
