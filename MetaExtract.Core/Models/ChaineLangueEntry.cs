using System.Text.Json.Serialization;

namespace MetaExtract.Core.Models;

/// <summary>
/// Associe une chaîne TV à sa langue principale, dans chaines_langues.json
/// (à côté de l'exécutable). Utilisé uniquement en repli, quand aucune piste
/// audio du fichier ne renseigne déjà de langue.
///
/// Comme grands_prix.json (voir <see cref="GrandPrixEntry"/>), ce fichier
/// est une donnée de référence maintenue dans le projet
/// (MetaExtract.App\chaines_langues.json) et livrée telle quelle à côté de
/// l'exécutable à chaque build : l'utilisateur n'a normalement pas besoin
/// d'y toucher.
/// </summary>
public sealed class ChaineLangueEntry
{
    [JsonPropertyName("chaine")]
    public string Chaine { get; set; } = "";

    [JsonPropertyName("langue")]
    public string Langue { get; set; } = "";
}
