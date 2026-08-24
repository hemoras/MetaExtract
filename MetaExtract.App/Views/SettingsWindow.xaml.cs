using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using MetaExtract.Core.Services;

namespace MetaExtract.App.Views;

public partial class SettingsWindow : Window
{
    /// <summary>Chemin validé, disponible après fermeture avec DialogResult == true.</summary>
    public string? ResultPath { get; private set; }

    public SettingsWindow(string? currentPath)
    {
        InitializeComponent();
        PathTextBox.Text = currentPath ?? "";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Sélectionner mediainfo.exe",
            Filter = "Exécutable (mediainfo.exe)|mediainfo.exe|Exécutable (*.exe)|*.exe|Tous les fichiers|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            PathTextBox.Text = dialog.FileName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PathTextBox.Text.Trim();

        if (!MediaInfoCliProvider.TryValidate(path, out var error))
        {
            ValidationText.Text = error;
            return;
        }

        ResultPath = path;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OpenConfigFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var configDir = AppPaths.ConfigDirectory;

        // S'assure que les fichiers filename_rules.json, grands_prix.json et
        // chaines_langues.json (avec leurs valeurs par défaut) existent déjà
        // avant d'ouvrir le dossier, même si aucun scan n'a encore été lancé.
        _ = new FilenameMetadataService();

        try
        {
            Process.Start(new ProcessStartInfo { FileName = configDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le dossier : {ex.Message}\n\nChemin : {configDir}",
                "Ouvrir le dossier de configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
