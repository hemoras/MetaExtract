using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using MetaExtract.Core.Models;
using MetaExtract.App.ViewModels;

namespace MetaExtract.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        ApplyVersionToTitle();

        ViewModel.SelectedFields.CollectionChanged += (_, _) => RebuildResultsColumns();
        RebuildResultsColumns();

        Closing += (_, _) => ViewModel.SaveSettings();
    }

    /// <summary>
    /// Ajoute le numéro de version au titre de la fenêtre, à côté du nom de
    /// l'application : "1.0.0" pour une release officielle, ou
    /// "1.0.0-dev+20260824-1533" (numéro + date/heure de build) sinon (voir
    /// InformationalVersion dans Directory.Build.props). En cas d'échec de
    /// lecture (ne devrait pas arriver), le titre défini dans le XAML reste
    /// inchangé plutôt que de planter.
    /// </summary>
    private void ApplyVersionToTitle()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(version))
            Title = $"MetaExtract v{version} — Extraction de métadonnées vidéo";
    }

    /// <summary>
    /// Reconstruit dynamiquement les colonnes de la grille de résultats à
    /// partir des champs sélectionnés (clé + ordre). Chaque colonne se lie
    /// via l'indexeur de VideoFileRecord (Binding "[Clé]"), ce qui évite
    /// de générer une classe/propriété par champ.
    /// </summary>
    private void RebuildResultsColumns()
    {
        ResultsDataGrid.Columns.Clear();
        foreach (var field in ViewModel.SelectedFields)
        {
            var column = new DataGridTextColumn
            {
                Header = field.Label,
                Binding = new Binding($"[{field.Key}]"),
                IsReadOnly = true,
            };
            ResultsDataGrid.Columns.Add(column);
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // FolderBrowserDialog (WinForms) ne propose le multi-sélection
        // qu'à partir de .NET 9 : on utilise donc CommonOpenFileDialog
        // (package WindowsAPICodePack), qui le supporte depuis longtemps.
        using var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog
        {
            Title = "Sélectionner un ou plusieurs dossiers à analyser",
            IsFolderPicker = true,
            Multiselect = true,
        };

        if (dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
        {
            foreach (var path in dialog.FileNames)
            {
                ViewModel.AddFolder(path);
            }
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(ViewModel.MediaInfoExecutablePath) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            ViewModel.MediaInfoExecutablePath = settingsWindow.ResultPath;
            ViewModel.SaveSettings();
        }
    }

    private void AddFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableFieldsListBox.SelectedItem is MetadataFieldDefinition field)
            ViewModel.AddFieldCommand.Execute(field);
    }

    private void AvailableFieldsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableFieldsListBox.SelectedItem is MetadataFieldDefinition field)
            ViewModel.AddFieldCommand.Execute(field);
    }

    private void RemoveFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFieldsListBox.SelectedItem is MetadataFieldDefinition field)
            ViewModel.RemoveFieldCommand.Execute(field);
    }

    private void MoveFieldUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFieldsListBox.SelectedItem is MetadataFieldDefinition field)
            ViewModel.MoveFieldUpCommand.Execute(field);
    }

    private void MoveFieldDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFieldsListBox.SelectedItem is MetadataFieldDefinition field)
            ViewModel.MoveFieldDownCommand.Execute(field);
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Results.Count == 0)
        {
            MessageBox.Show(this, "Aucun résultat à exporter. Lancez d'abord un scan.", "Export CSV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exporter en CSV",
            Filter = "Fichier CSV (*.csv)|*.csv",
            FileName = "metadonnees_video.csv",
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                ViewModel.ExportCsv(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Échec de l'export CSV : {ex.Message}", "Export CSV",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportExcelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Results.Count == 0)
        {
            MessageBox.Show(this, "Aucun résultat à exporter. Lancez d'abord un scan.", "Export Excel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exporter en Excel",
            Filter = "Classeur Excel (*.xlsx)|*.xlsx",
            FileName = "metadonnees_video.xlsx",
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                ViewModel.ExportExcel(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Échec de l'export Excel : {ex.Message}", "Export Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Contrairement à "Export CSV..."/"Export Excel...", n'utilise jamais
    /// les colonnes sélectionnées par l'utilisateur : la liste de colonnes
    /// est fixe (voir <see cref="MetaExtract.Core.Services.FieldCatalog.FullExportFieldKeys"/>).
    /// Le format (CSV ou Excel) est déterminé par l'extension choisie dans
    /// la boîte de dialogue.
    /// </summary>
    private void ExportFullButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Results.Count == 0)
        {
            MessageBox.Show(this, "Aucun résultat à exporter. Lancez d'abord un scan.", "Export complet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export complet",
            Filter = "Classeur Excel (*.xlsx)|*.xlsx|Fichier CSV (*.csv)|*.csv",
            FileName = "metadonnees_video_complet.xlsx",
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                if (string.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                    ViewModel.ExportFullCsv(dialog.FileName);
                else
                    ViewModel.ExportFullExcel(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Échec de l'export complet : {ex.Message}", "Export complet",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
