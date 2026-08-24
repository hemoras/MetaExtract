using System.Text.Json.Serialization;

namespace MetaExtract.Core.Models;

/// <summary>
/// Associe une saison + un numéro de manche au nom du Grand Prix, dans
/// grands_prix.json (à côté de l'exécutable). On ne déduit jamais le nom du
/// Grand Prix directement du nom de fichier car il peut varier pour une
/// même course selon les sources (ex: "Hollande" / "Pays-Bas").
///
/// Contrairement à filename_rules.json et chaines_langues.json (propres à
/// la convention de nommage de chaque utilisateur), grands_prix.json est une
/// donnée de référence maintenue dans le projet (MetaExtract.App\grands_prix.json)
/// et livrée telle quelle à côté de l'exécutable à chaque build : l'utilisateur
/// n'a normalement pas besoin d'y toucher, une nouvelle saison étant ajoutée
/// via une nouvelle version de l'application plutôt qu'une édition manuelle.
/// </summary>
public sealed class GrandPrixEntry
{
    [JsonPropertyName("saison")]
    public int Saison { get; set; }

    [JsonPropertyName("manche")]
    public int Manche { get; set; }

    [JsonPropertyName("grand_prix")]
    public string GrandPrix { get; set; } = "";
}
