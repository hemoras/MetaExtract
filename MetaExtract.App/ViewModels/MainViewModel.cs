using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MetaExtract.Core.Models;
using MetaExtract.Core.Services;

namespace MetaExtract.App.ViewModels;

/// <summary>
/// ViewModel principal : gère la liste de dossiers à analyser, la
/// sélection/ordre des colonnes de métadonnées, le lancement du scan et
/// les exports. Ne contient aucune référence directe à une boîte de
/// dialogue Windows (celles-ci sont ouvertes depuis le code-behind de la
/// vue) afin de garder cette classe simple et indépendante de l'UI.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<string> SelectedFolders { get; } = new();
    public ObservableCollection<MetadataFieldDefinition> AvailableFields { get; } = new();
    public ObservableCollection<MetadataFieldDefinition> SelectedFields { get; } = new();
    public ObservableCollection<VideoFileRecord> Results { get; } = new();

    [ObservableProperty]
    private string? mediaInfoExecutablePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool isBusy;

    [ObservableProperty]
    private int progressCurrent;

    [ObservableProperty]
    private int progressTotal;

    [ObservableProperty]
    private string statusMessage = "Prêt.";

    public MainViewModel()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        MediaInfoExecutablePath = settings.MediaInfoExecutablePath;

        foreach (var folder in settings.LastFolders)
        {
            if (!SelectedFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
                SelectedFolders.Add(folder);
        }

        // Reconstruit la sélection de champs, en forçant le nom de
        // fichier obligatoire en première position quel que soit le
        // contenu du fichier de config.
        SelectedFields.Add(FieldCatalog.Mandatory);

        var restoredKeys = settings.SelectedFieldKeys.Where(k => k != FieldCatalog.FileNameKey).ToList();
        var chosenKeys = restoredKeys.Count > 0 ? restoredKeys : DefaultFieldKeys();

        foreach (var key in chosenKeys)
        {
            var def = FieldCatalog.Find(key);
            if (def is not null && !SelectedFields.Contains(def))
                SelectedFields.Add(def);
        }

        foreach (var def in FieldCatalog.All)
        {
            if (!SelectedFields.Contains(def))
                AvailableFields.Add(def);
        }
    }

    private static List<string> DefaultFieldKeys() => new()
    {
        "FolderPath", "Resolution", "FrameRate", "Duration", "FileSize",
        "ContainerFormat", "VideoCodecId", "VideoBitRate", "AudioCodecId", "AudioBitRate"
    };

    public void SaveSettings()
    {
        var settings = _settingsService.Load();
        settings.MediaInfoExecutablePath = MediaInfoExecutablePath;
        settings.SelectedFieldKeys = SelectedFields.Select(f => f.Key).ToList();
        settings.LastFolders = SelectedFolders.ToList();
        _settingsService.Save(settings);
    }

    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (SelectedFolders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase))) return;
        SelectedFolders.Add(path);
        StartScanCommand.NotifyCanExecuteChanged();
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveFolder(string? path)
    {
        if (path is null) return;
        SelectedFolders.Remove(path);
        StartScanCommand.NotifyCanExecuteChanged();
        SaveSettings();
    }

    [RelayCommand]
    private void AddField(MetadataFieldDefinition? field)
    {
        if (field is null || field.IsMandatory) return;
        if (AvailableFields.Remove(field))
        {
            SelectedFields.Add(field);
            SaveSettings();
        }
    }

    [RelayCommand]
    private void RemoveField(MetadataFieldDefinition? field)
    {
        if (field is null || field.IsMandatory) return;
        if (SelectedFields.Remove(field))
        {
            InsertIntoAvailableSorted(field);
            SaveSettings();
        }
    }

    private void InsertIntoAvailableSorted(MetadataFieldDefinition field)
    {
        // Réinsère le champ à la position correspondant à l'ordre du
        // catalogue, pour garder la liste de gauche stable et lisible.
        var catalogOrder = FieldCatalog.All.ToList();
        var catalogIndex = catalogOrder.IndexOf(field);
        int insertAt = 0;
        for (; insertAt < AvailableFields.Count; insertAt++)
        {
            if (catalogOrder.IndexOf(AvailableFields[insertAt]) > catalogIndex) break;
        }
        AvailableFields.Insert(insertAt, field);
    }

    [RelayCommand]
    private void MoveFieldUp(MetadataFieldDefinition? field)
    {
        if (field is null || field.IsMandatory) return;
        var index = SelectedFields.IndexOf(field);
        // L'index 0 est toujours le champ obligatoire (nom de fichier) : impossible de monter au-dessus.
        if (index > 1)
        {
            SelectedFields.Move(index, index - 1);
            SaveSettings();
        }
    }

    [RelayCommand]
    private void MoveFieldDown(MetadataFieldDefinition? field)
    {
        if (field is null || field.IsMandatory) return;
        var index = SelectedFields.IndexOf(field);
        if (index >= 1 && index < SelectedFields.Count - 1)
        {
            SelectedFields.Move(index, index + 1);
            SaveSettings();
        }
    }

    private bool CanStartScan() => !IsBusy && SelectedFolders.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (string.IsNullOrWhiteSpace(MediaInfoExecutablePath) || !File.Exists(MediaInfoExecutablePath))
        {
            StatusMessage = "Veuillez configurer le chemin vers mediainfo.exe dans Paramètres avant de lancer un scan.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Analyse en cours...";
        Results.Clear();
        ProgressCurrent = 0;
        ProgressTotal = 0;

        _scanCts = new CancellationTokenSource();

        try
        {
            var provider = new MediaInfoCliProvider(MediaInfoExecutablePath!);
            var orchestrator = new MediaScanOrchestrator(provider);

            var settings = _settingsService.Load();
            var extensions = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);

            // Le contexte de synchronisation capturé ici est celui du thread UI
            // (StartScanAsync est déclenché par un clic bouton), donc les
            // callbacks de Progress<T> s'exécutent bien sur le thread UI.
            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressCurrent = p.Processed;
                ProgressTotal = p.Total;
                StatusMessage = $"Analyse : {p.Processed}/{p.Total} — {p.CurrentFileName}";
            });

            var records = await orchestrator.ScanAsync(
                SelectedFolders.ToList(), extensions, progress, _scanCts.Token);

            foreach (var record in records)
                Results.Add(record);

            StatusMessage = $"Terminé : {Results.Count} fichier(s) analysé(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Analyse annulée. {Results.Count} fichier(s) traité(s).";
        }
        catch (MediaInfoNotConfiguredException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur lors du scan : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private bool CanCancelScan() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        StatusMessage = "Annulation en cours...";
    }

    public void ExportCsv(string path) =>
        ExportService.ExportToCsv(Results, SelectedFields.ToList(), path);

    public void ExportExcel(string path) =>
        ExportService.ExportToExcel(Results, SelectedFields.ToList(), path);
}
