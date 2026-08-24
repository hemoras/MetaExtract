using System.Text.Encodings.Web;
using System.Text.Json;
using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>
/// Résout le nom complet d'une langue (ex: "fr" -> "Français") pour
/// l'affichage et l'export, à partir de langues.json (à côté de
/// l'exécutable). Comme grands_prix.json et chaines_langues.json, ce
/// fichier est une donnée de référence maintenue dans le projet
/// (MetaExtract.App\langues.json) et livrée telle quelle à côté de
/// l'exécutable à chaque build : l'utilisateur n'a normalement pas besoin
/// d'y toucher. Chargé une seule fois (mis en cache) au premier appel.
/// </summary>
public static class LangueNameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string FileName = "langues.json";

    private static readonly Lazy<List<LangueEntry>> Entries = new(Load);

    /// <summary>
    /// Retourne le nom complet de la langue (ex: "Français") pour un code
    /// (ex: "fr", recherche insensible à la casse). Retourne le code tel
    /// quel (nettoyé des espaces) si absent de langues.json ou si le code
    /// est vide/blanc.
    /// </summary>
    public static string Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";

        var trimmed = code.Trim();
        foreach (var entry in Entries.Value)
        {
            if (string.Equals(entry.Langue?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(entry.NomLangue) ? trimmed : entry.NomLangue;
        }
        return trimmed;
    }

    private static List<LangueEntry> Load()
    {
        try
        {
            var configDir = AppPaths.ConfigDirectory;
            Directory.CreateDirectory(configDir);
            AppPaths.MigrateLegacyFileIfNeeded(FileName);

            var path = Path.Combine(configDir, FileName);
            if (!File.Exists(path))
            {
                var defaults = DefaultLangues();
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
                return defaults;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<LangueEntry>>(json, JsonOptions);
            return loaded ?? DefaultLangues();
        }
        catch
        {
            // Fichier de config présent mais invalide : on ne casse jamais
            // l'affichage pour ça, et on ne touche pas au fichier.
            return DefaultLangues();
        }
    }

    // Filet de sécurité minimal si, contre toute attente, le langues.json
    // livré à côté de l'exécutable (MetaExtract.App\langues.json dans le
    // projet, copié au build) est absent ou illisible : la vraie liste de
    // référence vit dans ce fichier, pas ici.
    private static List<LangueEntry> DefaultLangues() => new()
    {
        new LangueEntry { Langue = "fr", NomLangue = "Français" },
        new LangueEntry { Langue = "en", NomLangue = "Anglais" },
    };
}
