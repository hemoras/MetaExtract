using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
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
        "Video;VID|%Format%|%CodecID%|%Format_Profile%|%Width%|%Height%|%FrameRate%|%BitRate%|%BitDepth%|%DisplayAspectRatio/String%|%ScanType%|%ChromaSubsampling%\\n\n" +
        "Audio;AUD|%Format%|%CodecID%|%BitRate%|%BitRate_Mode%|%Channels%|%SamplingRate%|%Language%|%Title%\\n\n";

    // Extrait la "chaîne" du champ Audio/Title en retirant un éventuel
    // suffixe entre parenthèses (ex: "TF1 (José Rosinski)" -> "TF1").
    // Si aucune parenthèse n'est présente, la valeur est retournée telle quelle.
    private static readonly Regex ChaineParenSuffixRegex = new(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);

    private static readonly string TemplateFilePath = Path.Combine(
        AppPaths.ConfigDirectory, "mediainfo_template.txt");

    private readonly string _mediaInfoExecutablePath;
    private readonly FilenameMetadataService _filenameMetadataService;

    public MediaInfoCliProvider(string mediaInfoExecutablePath, FilenameMetadataService? filenameMetadataService = null)
    {
        if (string.IsNullOrWhiteSpace(mediaInfoExecutablePath))
            throw new MediaInfoNotConfiguredException(
                "Aucun chemin vers mediainfo.exe n'est configuré. Ouvrez Paramètres pour indiquer l'emplacement de votre installation MediaInfo.");

        if (!File.Exists(mediaInfoExecutablePath))
            throw new MediaInfoNotConfiguredException(
                $"Le fichier '{mediaInfoExecutablePath}' est introuvable. Vérifiez le chemin configuré vers mediainfo.exe.");

        _mediaInfoExecutablePath = mediaInfoExecutablePath;
        _filenameMetadataService = filenameMetadataService ?? new FilenameMetadataService();

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
            var record = ParseIntoRecord(filePath, fileInfo, output);
            return ApplyFilenameDerivedFields(record, filePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Erreur "métier" : le fichier reste dans les résultats avec un message d'erreur.
            var record = BuildBaseRecord(filePath, fileInfo, ex.Message);
            return ApplyFilenameDerivedFields(record, filePath);
        }
    }

    /// <summary>
    /// Complète un enregistrement (déjà rempli à partir de MediaInfo, ou du
    /// strict minimum en cas d'erreur) avec les champs déduits du nom de
    /// fichier : Saison, Manche, Type et Grand Prix n'ont pas d'équivalent
    /// MediaInfo et sont donc toujours calculés ici.
    ///
    /// Chaîne et Langue suivent deux logiques selon le nombre de pistes
    /// audio :
    ///   - Cas général (0, ou ≥ 2 pistes audio) : recalculées à partir du nom
    ///     de fichier UNIQUEMENT si MediaInfo n'a rien pu déterminer (chaîne
    ///     vide / langue vide).
    ///   - Piste audio UNIQUE dont le titre (une fois les parenthèses
    ///     retirées) ne correspond à AUCUNE chaîne connue de
    ///     chaines_langues.json : ce titre n'est pas fiable (probablement un
    ///     texte générique comme "Nom de chaine inconnu", pas un vrai nom de
    ///     chaîne). On l'ignore et on retente la reconnaissance à partir du
    ///     nom de fichier (filename_rules.json) ; si elle trouve une chaîne,
    ///     la langue lue dans les métadonnées est elle aussi remplacée par
    ///     celle de cette chaîne (chaines_langues.json) — ex: piste audio en
    ///     "en" avec le titre "Nom de chaine inconnu", mais "RAI" trouvé dans
    ///     le nom de fichier -> Chaîne="RAI", Langue="it" (et non plus "en").
    /// </summary>
    private VideoFileRecord ApplyFilenameDerivedFields(VideoFileRecord record, string filePath)
    {
        var parsed = _filenameMetadataService.Parse(Path.GetFileNameWithoutExtension(filePath));
        var grandPrix = _filenameMetadataService.ResolveGrandPrix(parsed.Saison, parsed.Manche);

        bool singleTrackWithUnknownChaine = record.AudioTrackCount == 1
            && !string.IsNullOrWhiteSpace(record.TvChannelName)
            && _filenameMetadataService.ResolveLangue(record.TvChannelName) is null;

        string? chaine;
        string? langue;

        if (singleTrackWithUnknownChaine)
        {
            chaine = parsed.Chaine;
            langue = chaine is not null ? _filenameMetadataService.ResolveLangue(chaine) : record.AudioLanguage;
        }
        else
        {
            chaine = !string.IsNullOrWhiteSpace(record.TvChannelName) ? record.TvChannelName : parsed.Chaine;
            langue = !string.IsNullOrWhiteSpace(record.AudioLanguage) ? record.AudioLanguage : _filenameMetadataService.ResolveLangue(chaine);
        }

        return record with
        {
            Saison = parsed.Saison,
            Manche = parsed.Manche,
            RaceType = parsed.RaceType,
            GrandPrix = grandPrix,
            TvChannelName = chaine,
            AudioLanguage = langue,
        };
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

        string? audioFormat = null, audioCodecId = null, audioBitRateMode = null, audioChannels = null;
        long? audioBitRate = null, audioSampleRate = null;
        int audioTrackCount = 0;
        bool audioCaptured = false;
        bool generalCaptured = false;

        // Agrégées sur TOUTES les pistes audio (pas seulement la première) :
        // langues distinctes et chaînes distinctes déduites d'Audio/Title.
        var audioLanguagesSeen = new List<string>();
        var chainesSeen = new List<string>();

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
                    // %CodecID% (position 1 du template) n'est pas fiable pour l'affichage :
                    // c'est un identifiant propre au conteneur (ex. "V_MPEG4/ISO/AVC" en
                    // Matroska, mais un simple code numérique comme "27" en MPEG-TS, illisible).
                    // %Format% est en revanche cohérent quel que soit le conteneur (toujours
                    // "AVC", "HEVC"...), donc on réutilise cette même valeur pour les deux champs.
                    // NormalizeVideoFormat() simplifie ensuite certains libellés MediaInfo
                    // trop techniques (ex. "MPEG Video" -> "MPEG") pour l'affichage.
                    videoFormat = NormalizeVideoFormat(Field(f, 0));
                    videoCodecId = videoFormat;
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
                var f = line.Substring(4).Split('|');

                if (!audioCaptured)
                {
                    // Même remarque que pour la vidéo : %CodecID% (ex. "A_MPEG/L3" en
                    // Matroska vs "15-2" en MPEG-TS) n'est pas cohérent d'un conteneur à
                    // l'autre. On réutilise %Format%, cohérent partout (ex. "MPEG Audio").
                    // NormalizeAudioFormat() simplifie ensuite certains libellés MediaInfo
                    // trop techniques (ex. "MPEG Audio" -> "MP3") pour l'affichage.
                    audioFormat = NormalizeAudioFormat(Field(f, 0));
                    audioCodecId = audioFormat;
                    audioBitRate = ParseLong(f, 2);
                    audioBitRateMode = Field(f, 3);
                    audioChannels = Field(f, 4);
                    audioSampleRate = ParseLong(f, 5);
                    audioCaptured = true;
                }

                // Langue et chaîne sont agrégées sur TOUTES les pistes audio,
                // pas seulement la première (contrairement aux champs ci-dessus).
                var trackLanguage = Field(f, 6);
                if (trackLanguage is not null && !audioLanguagesSeen.Contains(trackLanguage, StringComparer.OrdinalIgnoreCase))
                    audioLanguagesSeen.Add(trackLanguage);

                var chaine = ExtractChaine(Field(f, 7));
                if (chaine is not null && !chainesSeen.Contains(chaine, StringComparer.OrdinalIgnoreCase))
                    chainesSeen.Add(chaine);
            }
        }

        string? audioLanguage = audioLanguagesSeen.Count > 0 ? string.Join(", ", audioLanguagesSeen) : null;
        string? tvChannelName = chainesSeen.Count > 0 ? string.Join(", ", chainesSeen) : null;

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
            TvChannelName = tvChannelName,
        };
    }

    /// <summary>
    /// Déduit la "chaîne" (ex: "TF1") à partir du champ Audio/Title, qui
    /// contient parfois en plus le(s) commentateur(s) entre parenthèses
    /// (ex: "TF1 (José Rosinski)"). Le suffixe entre parenthèses est retiré
    /// s'il est présent ; sinon la valeur du titre est conservée telle quelle
    /// (ex: "TF1" -> "TF1"). Retourne null si le titre est vide/absent ou si
    /// le résultat après nettoyage est vide.
    /// </summary>
    private static string? ExtractChaine(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var cleaned = ChaineParenSuffixRegex.Replace(title.Trim(), "").Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Simplifie certains libellés vidéo bruts renvoyés par MediaInfo (%Format%)
    /// pour un affichage plus lisible dans l'application. Les valeurs non listées
    /// sont conservées telles quelles (ex: "AVC", "HEVC"...).
    /// </summary>
    private static string? NormalizeVideoFormat(string? format) => format switch
    {
        "MPEG Video" => "MPEG",
        _ => format,
    };

    /// <summary>
    /// Simplifie certains libellés audio bruts renvoyés par MediaInfo (%Format%)
    /// pour un affichage plus lisible dans l'application. Les valeurs non listées
    /// sont conservées telles quelles (ex: "AC-3", "AAC"...).
    /// </summary>
    private static string? NormalizeAudioFormat(string? format) => format switch
    {
        "MPEG Audio" => "MP3",
        _ => format,
    };

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
