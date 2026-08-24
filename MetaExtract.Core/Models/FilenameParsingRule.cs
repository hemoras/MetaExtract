using System.Text.Json.Serialization;

namespace MetaExtract.Core.Models;

/// <summary>
/// Une règle de reconnaissance de nom de fichier : une expression régulière
/// avec des groupes nommés optionnels ("manche", "saison", "type", "chaine"),
/// essayée par <see cref="Services.FilenameMetadataService"/> dans l'ordre du
/// fichier filename_rules.json (à côté de l'exécutable ; la première règle
/// qui correspond est utilisée).
/// </summary>
public sealed class FilenameParsingRule
{
    /// <summary>Nom libre, pour se repérer dans le fichier de config (non utilisé par le moteur).</summary>
    [JsonPropertyName("nom")]
    public string Nom { get; set; } = "";

    /// <summary>
    /// Expression régulière (.NET) appliquée au nom de fichier SANS son
    /// extension. Groupes nommés reconnus, tous optionnels :
    ///   - "manche" : numéro de manche (entier)
    ///   - "saison" : année (entier)
    ///   - "type"   : type de séance (Course, Qualifications...) s'il est précisé
    ///   - "chaine" : chaîne TV telle qu'écrite dans le nom de fichier
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    /// <summary>Type de séance utilisé quand le groupe "type" n'a pas capturé de valeur (ex: "Course").</summary>
    [JsonPropertyName("type_par_defaut")]
    public string? TypeParDefaut { get; set; }

    /// <summary>
    /// Table de correspondance optionnelle : traduit la valeur brute captée
    /// par le groupe "type" (telle qu'écrite dans le nom de fichier, ex.
    /// "Race", "FP1", "Race.Highlights") vers le libellé à afficher (ex.
    /// "Course", "Essais Libres 1", "Résumé"). Recherche insensible à la
    /// casse. Si absente, ou si la valeur captée n'y figure pas, la valeur
    /// brute du groupe "type" est utilisée telle quelle (comportement
    /// d'origine, utile quand le nom de fichier contient déjà directement le
    /// libellé français, ex. "01 GP Australie 1998 - Qualifications (TF1)").
    /// </summary>
    [JsonPropertyName("types")]
    public Dictionary<string, string>? Types { get; set; }
}
