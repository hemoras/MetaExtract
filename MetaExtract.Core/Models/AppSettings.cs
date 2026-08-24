namespace MetaExtract.Core.Models;

/// <summary>
/// Paramètres persistés de l'application (fichier JSON à côté de l'exécutable).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Chemin complet vers l'exécutable mediainfo.exe installé par l'utilisateur.</summary>
    public string? MediaInfoExecutablePath { get; set; }

    /// <summary>
    /// Clés des champs sélectionnés, dans l'ordre d'affichage choisi par
    /// l'utilisateur. "FileName" est toujours forcé en première position
    /// au chargement, même si absent de cette liste.
    /// </summary>
    public List<string> SelectedFieldKeys { get; set; } = new();

    /// <summary>Derniers dossiers sélectionnés (confort, pré-remplissage à l'ouverture).</summary>
    public List<string> LastFolders { get; set; } = new();

    /// <summary>Extensions de fichiers vidéo prises en compte lors du scan.</summary>
    public List<string> VideoExtensions { get; set; } = new()
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".vob", ".3gp", ".ogv", ".divx"
    };
}
