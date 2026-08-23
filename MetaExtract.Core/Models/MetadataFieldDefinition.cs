namespace MetaExtract.Core.Models;

/// <summary>
/// Catégorie d'un champ de métadonnée, utilisée uniquement pour regrouper
/// visuellement les champs disponibles dans l'interface.
/// </summary>
public enum FieldCategory
{
    FichierEtDossier,
    General,
    Video,
    Audio
}

/// <summary>
/// Décrit un champ de métadonnée pouvant être affiché/exporté.
/// Cette classe ne contient aucune logique d'extraction : elle sert
/// uniquement de descripteur (clé, libellé, catégorie) réutilisable pour
/// la sélection dynamique des colonnes et leur persistance (on ne
/// sérialise que la clé + l'ordre, jamais un délégué).
/// </summary>
public sealed class MetadataFieldDefinition
{
    public MetadataFieldDefinition(string key, string label, FieldCategory category, bool isMandatory = false)
    {
        Key = key;
        Label = label;
        Category = category;
        IsMandatory = isMandatory;
    }

    /// <summary>Identifiant stable du champ (utilisé pour la persistance et le binding).</summary>
    public string Key { get; }

    /// <summary>Libellé affiché à l'utilisateur (colonne de la grille / en-tête d'export).</summary>
    public string Label { get; }

    public FieldCategory Category { get; }

    /// <summary>Le nom de fichier est obligatoire et toujours en première position.</summary>
    public bool IsMandatory { get; }

    public override string ToString() => Label;
}
