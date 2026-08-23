using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>
/// Abstraction de la source de métadonnées. L'implémentation par défaut
/// (<see cref="MediaInfoCliProvider"/>) s'appuie sur l'exécutable
/// mediainfo.exe installé par l'utilisateur. Cette interface permet de
/// brancher plus tard une autre implémentation (ex: MediaInfoLib en
/// binding direct) sans toucher au reste de l'application.
/// </summary>
public interface IMediaInfoProvider
{
    /// <summary>
    /// Extrait les métadonnées d'un fichier vidéo. Ne doit pas lever
    /// d'exception pour une erreur "métier" (fichier corrompu, non
    /// reconnu...) : dans ce cas, retourner un <see cref="VideoFileRecord"/>
    /// avec la propriété Error renseignée, afin que le fichier reste
    /// visible dans les résultats.
    /// </summary>
    Task<VideoFileRecord> ExtractAsync(string filePath, CancellationToken cancellationToken);
}
