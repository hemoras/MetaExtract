namespace MetaExtract.Core.Models;

/// <summary>
/// Représente toutes les métadonnées récupérées pour un fichier vidéo.
/// Les valeurs sont conservées "brutes" (types natifs) et c'est
/// <see cref="Services.FieldCatalog.GetDisplayValue"/> qui se charge du
/// formatage texte pour l'affichage / l'export, à partir de la clé de
/// champ choisie par l'utilisateur.
///
/// L'indexeur `this[key]` permet le binding WPF dynamique
/// (Binding "[Key]") sans avoir à générer du code par colonne.
/// </summary>
public sealed class VideoFileRecord
{
    // --- Système de fichiers ---
    public required string FileName { get; init; }
    public required string FolderPath { get; init; }
    public required string FullPath { get; init; }
    public string Extension { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime? DateModified { get; init; }

    // --- Général (conteneur) ---
    public string? ContainerFormat { get; init; }
    public double? DurationMs { get; init; }
    public long? OverallBitRate { get; init; }

    // --- Vidéo ---
    public string? VideoFormat { get; init; }
    public string? VideoCodecId { get; init; }
    public string? VideoProfile { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }
    public long? VideoBitRate { get; init; }
    public string? VideoBitDepth { get; init; }
    public string? AspectRatio { get; init; }
    public string? ScanType { get; init; }
    public string? ChromaSubsampling { get; init; }

    // --- Audio (piste principale) ---
    public string? AudioFormat { get; init; }
    public string? AudioCodecId { get; init; }
    public long? AudioBitRate { get; init; }
    public string? AudioBitRateMode { get; init; }
    public string? AudioChannels { get; init; }
    public long? AudioSampleRate { get; init; }
    public string? AudioLanguage { get; init; }
    public int AudioTrackCount { get; init; }
    /// <summary>Chaîne(s) TV déduite(s) du champ Audio/Title (ex: "TF1 (José Rosinski)" → "TF1"), une par piste distincte.</summary>
    public string? TvChannelName { get; init; }

    /// <summary>Message d'erreur si l'extraction a échoué pour ce fichier (le fichier reste listé).</summary>
    public string? Error { get; init; }

    /// <summary>Permet le binding WPF dynamique : Binding Path="[NomDeLaCle]".</summary>
    public string this[string fieldKey] => Services.FieldCatalog.GetDisplayValue(this, fieldKey);
}
