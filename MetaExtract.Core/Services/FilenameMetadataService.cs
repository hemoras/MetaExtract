using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>Résultat de l'analyse d'un nom de fichier par <see cref="FilenameMetadataService"/>.</summary>
public sealed record FilenameParseResult(int? Saison, int? Manche, string? RaceType, string? Chaine);

/// <summary>
/// Déduit saison / manche / type de séance / chaîne à partir du nom de
/// fichier (sans son extension), et résout le Grand Prix et la langue via
/// des tables de correspondance. Les trois fichiers de configuration sont
/// externalisés à côté de l'exécutable (JSON, en texte clair — voir
/// <see cref="AppPaths"/>) :
///
///   - filename_rules.json    : règles de reconnaissance (regex nommées).
///                              Propre à la convention de nommage de chaque
///                              utilisateur ; recréé avec un exemple minimal
///                              au premier lancement s'il est absent.
///   - grands_prix.json       : (saison, manche) -> nom du Grand Prix.
///                              Donnée de référence maintenue dans le projet
///                              (MetaExtract.App\grands_prix.json) et livrée
///                              à côté de l'exécutable à chaque build : déjà
///                              présente au premier lancement, l'utilisateur
///                              n'a normalement pas besoin d'y toucher.
///   - chaines_langues.json   : chaîne -> langue principale. Comme
///                              grands_prix.json, donnée de référence
///                              maintenue dans le projet
///                              (MetaExtract.App\chaines_langues.json) et
///                              livrée à côté de l'exécutable à chaque
///                              build : l'utilisateur n'a normalement pas
///                              besoin d'y toucher.
///
/// Un fichier absent (cas normal pour filename_rules.json au premier
/// lancement ; ne devrait pas arriver pour grands_prix.json/
/// chaines_langues.json, livrés avec l'exécutable) est recréé avec des
/// valeurs par défaut. Un fichier présent mais invalide (JSON mal formé) ne
/// bloque jamais un scan : on retombe silencieusement sur les valeurs par
/// défaut en mémoire, sans écraser le fichier sur disque (pour ne pas
/// perdre une édition de l'utilisateur en cours, potentiellement juste mal
/// formée).
/// </summary>
public sealed class FilenameMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        // Encodeur par défaut trop prudent pour un fichier local en texte
        // clair : il échappe "<", ">", "+", etc. en séquences \uXXXX
        // illisibles (pensé pour de la sortie HTML/JS, pas pertinent ici).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Nombre à 4 chiffres isolé (ni précédé ni suivi d'un autre chiffre) -
    // utilisé par le repli de détection de saison, cf. ParseFallback().
    private static readonly Regex FourDigitNumberRegex = new(@"(?<!\d)\d{4}(?!\d)", RegexOptions.Compiled);

    // Expression entre parenthèses en toute fin de chaîne ; la parenthèse
    // fermante est optionnelle (cf. ExtractTrailingParenExpression()).
    private static readonly Regex TrailingParenExpressionRegex = new(@"\(([^()]*)\)?\s*$", RegexOptions.Compiled);

    private readonly List<FilenameParsingRule> _rules;
    private readonly List<GrandPrixEntry> _grandsPrix;
    private readonly List<ChaineLangueEntry> _chainesLangues;

    private const string RulesFileName = "filename_rules.json";
    private const string GrandsPrixFileName = "grands_prix.json";
    private const string ChainesLanguesFileName = "chaines_langues.json";

    public FilenameMetadataService(string? overrideConfigDirectory = null)
    {
        var configDir = overrideConfigDirectory ?? AppPaths.ConfigDirectory;
        Directory.CreateDirectory(configDir);

        if (overrideConfigDirectory is null)
        {
            AppPaths.MigrateLegacyFileIfNeeded(RulesFileName);
            AppPaths.MigrateLegacyFileIfNeeded(GrandsPrixFileName);
            AppPaths.MigrateLegacyFileIfNeeded(ChainesLanguesFileName);
        }

        _rules = LoadOrCreateDefault(Path.Combine(configDir, RulesFileName), DefaultRules());
        _grandsPrix = LoadOrCreateDefault(Path.Combine(configDir, GrandsPrixFileName), DefaultGrandsPrix());
        _chainesLangues = LoadOrCreateDefault(Path.Combine(configDir, ChainesLanguesFileName), DefaultChainesLangues());
    }

    /// <summary>
    /// Analyse un nom de fichier (SANS extension) selon la première règle de
    /// <c>filename_rules.json</c> qui correspond. Si aucune règle ne
    /// correspond, retombe sur une détection heuristique minimale (voir
    /// <see cref="ParseFallback"/>) plutôt que de tout renvoyer à null.
    /// </summary>
    /// <param name="fileNameWithoutExtension">Nom du fichier, sans extension.</param>
    /// <param name="fullFilePath">
    /// Chemin complet du fichier (optionnel). Utilisé uniquement par le
    /// repli heuristique, pour chercher une année dans le dossier parent
    /// quand le nom de fichier lui-même n'en contient pas.
    /// </param>
    public FilenameParseResult Parse(string fileNameWithoutExtension, string? fullFilePath = null)
    {
        foreach (var rule in _rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;

            Match match;
            try
            {
                match = Regex.Match(fileNameWithoutExtension, rule.Pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                // Regex invalide dans le fichier de config utilisateur : on
                // ignore cette règle plutôt que de faire échouer le scan.
                continue;
            }

            if (!match.Success) continue;

            var saison = TryGetInt(match, "saison");
            var manche = TryGetInt(match, "manche");

            var type = ResolveType(rule, TryGetString(match, "type"));

            var chaine = TryGetString(match, "chaine");
            if (chaine is not null && chaine.Contains("multi", StringComparison.OrdinalIgnoreCase))
                chaine = null;

            return new FilenameParseResult(saison, manche, type, chaine);
        }

        return ParseFallback(fileNameWithoutExtension, fullFilePath);
    }

    /// <summary>
    /// Repli utilisé quand aucune règle de <c>filename_rules.json</c> ne
    /// correspond au nom de fichier :
    ///   - Saison : premier nombre à 4 chiffres du nom de fichier compris
    ///     entre 1950 et l'année en cours. Si aucun nombre valide n'est
    ///     trouvé dans le nom de fichier, on cherche de la même façon dans
    ///     le chemin du dossier parent (<paramref name="fullFilePath"/>).
    ///   - Chaîne : si le nom de fichier se termine par une expression entre
    ///     parenthèses (fermante optionnelle, ex: "... (AFAVA - Motors TV"),
    ///     et que cette expression contient un tiret, on ne garde que la
    ///     partie après le tiret (ex: "AFAVA - Motors TV" -> "Motors TV").
    ///     Cette expression (ou sa partie après le tiret) est alors
    ///     recherchée telle quelle dans chaines_langues.json ; si trouvée,
    ///     elle devient la chaîne détectée (la langue associée est résolue
    ///     séparément via <see cref="ResolveLangue"/> par l'appelant).
    /// Manche et type restent toujours null dans ce repli.
    /// </summary>
    private FilenameParseResult ParseFallback(string fileNameWithoutExtension, string? fullFilePath)
    {
        var saison = FindPlausibleYear(fileNameWithoutExtension);
        if (saison is null && fullFilePath is not null)
            saison = FindPlausibleYear(Path.GetDirectoryName(fullFilePath));

        string? chaine = null;
        var candidate = ExtractTrailingParenExpression(fileNameWithoutExtension);
        if (candidate is not null)
        {
            foreach (var entry in _chainesLangues)
            {
                if (string.Equals(entry.Chaine?.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                {
                    chaine = entry.Chaine;
                    break;
                }
            }
        }

        return new FilenameParseResult(saison, null, null, chaine);
    }

    /// <summary>
    /// Recherche le premier nombre isolé à 4 chiffres de <paramref name="text"/>
    /// compris entre 1950 et l'année en cours (bornes incluses). "Isolé"
    /// signifie non précédé/suivi d'un autre chiffre (pour ne pas confondre
    /// avec un nombre plus long, ex. un débit).
    /// </summary>
    private static int? FindPlausibleYear(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var currentYear = DateTime.Now.Year;
        foreach (Match m in FourDigitNumberRegex.Matches(text))
        {
            if (int.TryParse(m.Value, out var year) && year >= 1950 && year <= currentYear)
                return year;
        }
        return null;
    }

    /// <summary>
    /// Si <paramref name="fileNameWithoutExtension"/> se termine par une
    /// expression entre parenthèses (la parenthèse fermante est optionnelle,
    /// certains noms de fichiers en étant dépourvus), retourne cette
    /// expression - ou uniquement la partie après le tiret si elle en
    /// contient un (ex: "AFAVA - Motors TV" -> "Motors TV"). Retourne null
    /// si le nom ne se termine pas par une telle expression.
    /// </summary>
    private static string? ExtractTrailingParenExpression(string fileNameWithoutExtension)
    {
        var match = TrailingParenExpressionRegex.Match(fileNameWithoutExtension);
        if (!match.Success) return null;

        var expression = match.Groups[1].Value.Trim();
        if (expression.Length == 0) return null;

        var dashIndex = expression.IndexOf('-');
        if (dashIndex >= 0)
            expression = expression[(dashIndex + 1)..].Trim();

        return expression.Length == 0 ? null : expression;
    }

    /// <summary>Résout le nom du Grand Prix pour une saison + manche données (null si absent de grands_prix.json).</summary>
    public string? ResolveGrandPrix(int? saison, int? manche)
    {
        if (saison is null || manche is null) return null;

        foreach (var entry in _grandsPrix)
        {
            if (entry.Saison == saison.Value && entry.Manche == manche.Value)
                return string.IsNullOrWhiteSpace(entry.GrandPrix) ? null : entry.GrandPrix;
        }
        return null;
    }

    /// <summary>Résout la langue principale d'une chaîne (null si absente de chaines_langues.json).</summary>
    public string? ResolveLangue(string? chaine)
    {
        if (string.IsNullOrWhiteSpace(chaine)) return null;

        var chaineTrimmed = chaine.Trim();
        foreach (var entry in _chainesLangues)
        {
            if (string.Equals(entry.Chaine?.Trim(), chaineTrimmed, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(entry.Langue) ? null : entry.Langue;
        }
        return null;
    }

    /// <summary>
    /// Traduit la valeur brute captée par le groupe "type" via
    /// <see cref="FilenameParsingRule.Types"/> (recherche insensible à la
    /// casse) si elle y figure ; sinon la retourne telle quelle. Si le
    /// groupe "type" n'a rien capté, retombe sur <see cref="FilenameParsingRule.TypeParDefaut"/>.
    /// </summary>
    private static string? ResolveType(FilenameParsingRule rule, string? rawType)
    {
        if (rawType is null)
            return string.IsNullOrWhiteSpace(rule.TypeParDefaut) ? null : rule.TypeParDefaut;

        if (rule.Types is not null)
        {
            foreach (var (key, value) in rule.Types)
            {
                if (string.Equals(key, rawType, StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        }

        return rawType;
    }

    private static int? TryGetInt(Match match, string groupName)
    {
        var group = match.Groups[groupName];
        if (!group.Success) return null;
        return int.TryParse(group.Value.Trim(), out var value) ? value : null;
    }

    private static string? TryGetString(Match match, string groupName)
    {
        var group = match.Groups[groupName];
        if (!group.Success) return null;
        var trimmed = group.Value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static List<T> LoadOrCreateDefault<T>(string path, List<T> defaults)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
                return defaults;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
            return loaded ?? defaults;
        }
        catch
        {
            // Fichier de config présent mais invalide : on ne casse jamais un
            // scan pour ça, et on ne touche pas au fichier (l'utilisateur est
            // peut-être justement en train de le corriger).
            return defaults;
        }
    }

    private static List<FilenameParsingRule> DefaultRules() => new()
    {
        new FilenameParsingRule
        {
            Nom = "Course F1 : \"<manche> GP <nom> <saison>[ - <type>][ (<chaine>)]\" ou \"<manche> GP <nom> <saison> <chaine>\" "
                + "(ex: \"01 GP Australie 1998 (TF1)\", \"01 GP Australie 1998 - Qualifications (TF1)\", \"08 GP Canada 2006 RAI\")",
            // Après la saison, trois formes possibles pour la suite (une
            // seule s'applique par fichier) :
            //   1. " - <type> [(<chaine>)]" : tiret suivi du type, avec une
            //      chaîne entre parenthèses en option (ex: "- Qualifications (TF1)").
            //   2. " (<chaine>)" : chaîne entre parenthèses, sans type (ex: "(TF1)").
            //   3. " <chaine>" : chaîne "en clair", sans tiret ni parenthèses
            //      (ex: "RAI") — pour les fichiers qui n'utilisent pas de
            //      parenthèses autour du nom de la chaîne.
            // Les trois formes réutilisent le même groupe nommé "chaine" :
            // .NET autorise plusieurs groupes de même nom du moment qu'ils
            // sont dans des branches d'alternative différentes (jamais deux
            // à la fois), et Groups["chaine"] renvoie alors celui qui a
            // effectivement participé au match.
            Pattern = @"^(?<manche>\d{1,2})\s+GP\s+.+?\s+(?<saison>\d{4})"
                + @"(?:\s*-\s*(?<type>[^()]+?)\s*(?:\((?<chaine>[^()]+)\))?|\s*\((?<chaine>[^()]+)\)|\s+(?<chaine>[^()\s].*?))?\s*$",
            TypeParDefaut = "Course",
        },
        new FilenameParsingRule
        {
            Nom = "Format \"Formula1.<saison>.Round<manche>.<nom>.<type>.<chaine>...\" "
                + "(ex: \"Formula1.1998.Round01.Australia.Race.TF1.1080p.H264.French\")",
            Pattern = @"^Formula1\.(?<saison>\d{4})\.Round(?<manche>\d{1,2})\..+?\."
                + @"(?<type>Race\.Highlights|Qualifying\.Reports|Qualifying\.Highlights|Race\.Preview|Post-Race|Qualifying|Preview|Quali1|Quali2|Race|Q1|Q2|FP1|FP2|FP3|FP4)\."
                + @"(?<chaine>[^.]+)",
            Types = new Dictionary<string, string>
            {
                ["Race"] = "Course",
                ["Qualifying"] = "Qualifications",
                ["Q1"] = "Qualifications 1",
                ["Quali1"] = "Qualifications 1",
                ["Q2"] = "Qualifications 2",
                ["Quali2"] = "Qualifications 2",
                ["Race.Highlights"] = "Résumé",
                ["Qualifying.Reports"] = "Résumé",
                ["Qualifying.Highlights"] = "Résumé Qualifications",
                ["Preview"] = "Pre-Course",
                ["Race.Preview"] = "Pre-Course",
                ["Post-Race"] = "Post-Course",
                ["FP1"] = "Essais Libres 1",
                ["FP2"] = "Essais Libres 2",
                ["FP3"] = "Essais Libres 3",
                ["FP4"] = "Essais Libres 4",
            },
        },
    };

    // Filet de sécurité minimal si, contre toute attente, le grands_prix.json
    // livré à côté de l'exécutable (MetaExtract.App\grands_prix.json dans le
    // projet, copié au build) est absent ou illisible : la vraie liste de
    // référence (toutes saisons) vit dans ce fichier, pas ici.
    private static List<GrandPrixEntry> DefaultGrandsPrix() => new()
    {
        new GrandPrixEntry { Saison = 1998, Manche = 1, GrandPrix = "Australie" },
    };

    // Filet de sécurité minimal, cf. DefaultGrandsPrix() ci-dessus : la vraie
    // table de référence vit dans MetaExtract.App\chaines_langues.json.
    private static List<ChaineLangueEntry> DefaultChainesLangues() => new()
    {
        new ChaineLangueEntry { Chaine = "TF1", Langue = "fr" },
    };
}
