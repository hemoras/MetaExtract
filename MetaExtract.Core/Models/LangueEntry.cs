using System.Text.Json.Serialization;

namespace MetaExtract.Core.Models;

/// <summary>
/// Associe un code de langue (ex: "fr", au format renvoyé par MediaInfo) à
/// son nom complet affiché dans l'application (ex: "Français"), dans
/// langues.json (à côté de l'exécutable).
///
/// Comme grands_prix.json et chaines_langues.json (voir
/// <see cref="GrandPrixEntry"/> et <see cref="ChaineLangueEntry"/>), ce
/// fichier est une donnée de référence maintenue dans le projet
/// (MetaExtract.App\langues.json) et livrée telle quelle à côté de
/// l'exécutable à chaque build : l'utilisateur n'a normalement pas besoin
/// d'y toucher.
/// </summary>
public sealed class LangueEntry
{
    [JsonPropertyName("langue")]
    public string Langue { get; set; } = "";

    [JsonPropertyName("nom_langue")]
    public string NomLangue { get; set; } = "";
}
