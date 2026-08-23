using System.Globalization;
using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>
/// Catalogue central des champs de métadonnées disponibles dans
/// l'application, et logique de formatage pour l'affichage/export.
///
/// Ajouter un nouveau champ = ajouter une entrée dans <see cref="All"/>
/// et un case correspondant dans <see cref="GetDisplayValue"/>.
/// </summary>
public static class FieldCatalog
{
    public const string FileNameKey = "FileName";

    /// <summary>Liste ordonnée (ordre "naturel" par catégorie) de tous les champs disponibles.</summary>
    public static readonly IReadOnlyList<MetadataFieldDefinition> All = new List<MetadataFieldDefinition>
    {
        // Fichier / dossier
        new(FileNameKey,          "Nom du fichier",           FieldCategory.FichierEtDossier, isMandatory: true),
        new("FolderPath",         "Dossier",                  FieldCategory.FichierEtDossier),
        new("FullPath",           "Chemin complet",           FieldCategory.FichierEtDossier),
        new("Extension",          "Extension",                FieldCategory.FichierEtDossier),
        new("FileSize",           "Taille du fichier",        FieldCategory.FichierEtDossier),
        new("DateModified",       "Date de modification",     FieldCategory.FichierEtDossier),

        // Général / conteneur
        new("ContainerFormat",    "Format du conteneur",      FieldCategory.General),
        new("Duration",           "Durée",                    FieldCategory.General),
        new("OverallBitRate",     "Bitrate global",           FieldCategory.General),

        // Vidéo
        new("VideoFormat",        "Format vidéo",             FieldCategory.Video),
        new("VideoCodecId",       "Codec vidéo",              FieldCategory.Video),
        new("VideoProfile",       "Profil codec vidéo",       FieldCategory.Video),
        new("Width",              "Largeur (px)",             FieldCategory.Video),
        new("Height",             "Hauteur (px)",             FieldCategory.Video),
        new("Resolution",         "Résolution",               FieldCategory.Video),
        new("FrameRate",          "FPS",                      FieldCategory.Video),
        new("VideoBitRate",       "Bitrate vidéo",            FieldCategory.Video),
        new("VideoBitDepth",      "Profondeur de couleur",    FieldCategory.Video),
        new("AspectRatio",        "Ratio d'affichage",        FieldCategory.Video),
        new("ScanType",           "Type de scan",             FieldCategory.Video),
        new("ChromaSubsampling",  "Sous-échantillonnage",     FieldCategory.Video),

        // Audio
        new("AudioFormat",        "Format audio",             FieldCategory.Audio),
        new("AudioCodecId",       "Codec audio",              FieldCategory.Audio),
        new("AudioBitRate",       "Bitrate audio",            FieldCategory.Audio),
        new("AudioBitRateMode",   "Mode bitrate audio",       FieldCategory.Audio),
        new("AudioChannels",      "Canaux audio",             FieldCategory.Audio),
        new("AudioSampleRate",    "Fréquence d'échantillonnage", FieldCategory.Audio),
        new("AudioLanguage",      "Langue audio",             FieldCategory.Audio),
        new("TvChannel",          "Chaîne",                   FieldCategory.Audio),
        new("AudioTrackCount",    "Nb pistes audio",          FieldCategory.Audio),

        new("Error",              "Erreur",                   FieldCategory.FichierEtDossier),
    };

    public static MetadataFieldDefinition? Find(string key) => All.FirstOrDefault(f => f.Key == key);

    public static readonly MetadataFieldDefinition Mandatory = All.First(f => f.IsMandatory);

    /// <summary>
    /// Retourne la valeur formatée (prête à afficher / exporter) d'un
    /// champ pour un enregistrement donné. Ne lève jamais d'exception :
    /// une clé inconnue ou une valeur absente retourne une chaîne vide.
    /// </summary>
    public static string GetDisplayValue(VideoFileRecord record, string fieldKey)
    {
        return fieldKey switch
        {
            "FileName" => record.FileName,
            "FolderPath" => record.FolderPath,
            "FullPath" => record.FullPath,
            "Extension" => record.Extension,
            "FileSize" => FormatBytes(record.FileSizeBytes),
            "DateModified" => record.DateModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",

            "ContainerFormat" => record.ContainerFormat ?? "",
            "Duration" => FormatDuration(record.DurationMs),
            "OverallBitRate" => FormatBitRate(record.OverallBitRate),

            "VideoFormat" => record.VideoFormat ?? "",
            "VideoCodecId" => record.VideoCodecId ?? "",
            "VideoProfile" => record.VideoProfile ?? "",
            "Width" => record.Width?.ToString(CultureInfo.InvariantCulture) ?? "",
            "Height" => record.Height?.ToString(CultureInfo.InvariantCulture) ?? "",
            "Resolution" => (record.Width.HasValue && record.Height.HasValue)
                ? $"{record.Width}x{record.Height}"
                : "",
            "FrameRate" => record.FrameRate.HasValue
                ? record.FrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture) + " fps"
                : "",
            "VideoBitRate" => FormatBitRate(record.VideoBitRate),
            "VideoBitDepth" => record.VideoBitDepth ?? "",
            "AspectRatio" => record.AspectRatio ?? "",
            "ScanType" => record.ScanType ?? "",
            "ChromaSubsampling" => record.ChromaSubsampling ?? "",

            "AudioFormat" => record.AudioFormat ?? "",
            "AudioCodecId" => record.AudioCodecId ?? "",
            "AudioBitRate" => FormatBitRate(record.AudioBitRate),
            "AudioBitRateMode" => record.AudioBitRateMode ?? "",
            "AudioChannels" => record.AudioChannels ?? "",
            "AudioSampleRate" => record.AudioSampleRate.HasValue
                ? (record.AudioSampleRate.Value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz"
                : "",
            "AudioLanguage" => record.AudioLanguage ?? "",
            "TvChannel" => record.TvChannelName ?? "",
            "AudioTrackCount" => record.AudioTrackCount.ToString(CultureInfo.InvariantCulture),

            "Error" => record.Error ?? "",

            _ => "",
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0
            ? $"{bytes} {units[0]}"
            : $"{size.ToString("0.##", CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private static string FormatBitRate(long? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue) return "";
        double v = bitsPerSecond.Value;
        if (v >= 1_000_000)
            return (v / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + " Mb/s";
        if (v >= 1_000)
            return (v / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + " Kb/s";
        return v.ToString("0", CultureInfo.InvariantCulture) + " b/s";
    }

    private static string FormatDuration(double? milliseconds)
    {
        if (!milliseconds.HasValue) return "";
        var ts = TimeSpan.FromMilliseconds(milliseconds.Value);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }
}
