using System.Windows;
using System.Windows.Threading;

namespace MetaExtract.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Sans ça, une exception au démarrage (avant l'affichage de la
        // moindre fenêtre) fait quitter le processus silencieusement :
        // c'est très probablement ce qui se passe quand "rien ne se passe"
        // au lancement. On l'affiche pour pouvoir diagnostiquer.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Erreur non gérée au démarrage :\n\n{args.ExceptionObject}",
                "MetaExtract — Erreur fatale", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Erreur non gérée :\n\n{args.Exception}",
                "MetaExtract — Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
