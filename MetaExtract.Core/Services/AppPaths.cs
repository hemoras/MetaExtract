namespace MetaExtract.Core.Services;

/// <summary>
/// Centralise l'emplacement des fichiers de configuration de l'application
/// (settings.json, filename_rules.json, grands_prix.json,
/// chaines_langues.json, mediainfo_template.txt).
///
/// Depuis la 1.1.0, ces fichiers sont stockés à côté de l'exécutable
/// (MetaExtract.App.exe) plutôt que dans %AppData%\MetaExtract : plus facile
/// à retrouver, et cohérent avec une utilisation "portable" (dossier copié
/// tel quel, clé USB...). <see cref="ConfigDirectory"/> (= AppContext.BaseDirectory)
/// pointe vers ce dossier aussi bien pour un exécutable unique auto-suffisant
/// (publication "single file") que pour un lancement via "dotnet run" (dans
/// ce cas, le dossier de build bin\...\ correspondant).
/// </summary>
public static class AppPaths
{
    public static string ConfigDirectory => AppContext.BaseDirectory;

    // Ancien emplacement (avant la 1.1.0), conservé uniquement pour migrer
    // une configuration existante vers le nouvel emplacement.
    private static readonly string LegacyConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MetaExtract");

    /// <summary>
    /// Si <paramref name="fileName"/> n'existe pas encore à son nouvel
    /// emplacement (à côté de l'exe) mais existe à l'ancien
    /// (%AppData%\MetaExtract), le copie une bonne fois pour toutes afin de
    /// ne pas faire perdre à l'utilisateur une configuration déjà en place.
    /// Ne fait jamais échouer l'appelant : une migration ratée retombe
    /// simplement sur les valeurs par défaut.
    /// </summary>
    public static void MigrateLegacyFileIfNeeded(string fileName)
    {
        try
        {
            var newPath = Path.Combine(ConfigDirectory, fileName);
            var legacyPath = Path.Combine(LegacyConfigDirectory, fileName);
            if (!File.Exists(newPath) && File.Exists(legacyPath))
                File.Copy(legacyPath, newPath);
        }
        catch
        {
            // Cf. commentaire ci-dessus : jamais bloquant.
        }
    }
}
