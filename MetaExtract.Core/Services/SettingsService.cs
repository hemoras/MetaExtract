using System.Text.Json;
using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>
/// Charge/sauvegarde les paramètres de l'application dans un fichier
/// settings.json situé à côté de l'exécutable (voir <see cref="AppPaths"/>).
/// </summary>
public sealed class SettingsService
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public SettingsService(string? overrideFilePath = null)
    {
        if (overrideFilePath is not null)
        {
            _settingsFilePath = overrideFilePath;
            return;
        }

        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        AppPaths.MigrateLegacyFileIfNeeded(FileName);
        _settingsFilePath = Path.Combine(AppPaths.ConfigDirectory, FileName);
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch
        {
            // Un fichier de config corrompu ne doit jamais empêcher le démarrage de l'app.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }
}
