namespace MetaExtract.Core.Services;

/// <summary>
/// Parcourt récursivement un ou plusieurs dossiers pour lister les
/// fichiers vidéo (filtrés par extension). Implémenté avec une pile
/// manuelle plutôt que Directory.EnumerateFiles(..., AllDirectories) car
/// ce dernier interrompt tout le parcours à la première exception
/// (dossier protégé, lien cassé, etc.) : ici, un sous-dossier
/// inaccessible est simplement ignoré et signalé.
/// </summary>
public static class FolderScanner
{
    public sealed record ScanResult(IReadOnlyList<string> Files, IReadOnlyList<string> Warnings);

    public static ScanResult FindVideoFiles(
        IEnumerable<string> rootFolders,
        IReadOnlySet<string> allowedExtensions,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var warnings = new List<string>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in rootFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                warnings.Add($"Dossier introuvable, ignoré : {root}");
                continue;
            }

            var fullRoot = Path.GetFullPath(root);
            if (!seenFolders.Add(fullRoot))
                continue; // évite de scanner deux fois le même dossier (doublons ou imbrication)

            ScanDirectoryRecursive(fullRoot, allowedExtensions, files, warnings, cancellationToken);
        }

        return new ScanResult(files, warnings);
    }

    private static void ScanDirectoryRecursive(
        string directory,
        IReadOnlySet<string> allowedExtensions,
        List<string> files,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFiles(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            warnings.Add($"Accès refusé au dossier : {directory} ({ex.Message})");
            entries = Array.Empty<string>();
        }

        foreach (var file in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (!string.IsNullOrEmpty(ext) && allowedExtensions.Contains(ext))
                files.Add(file);
        }

        IEnumerable<string> subDirs;
        try
        {
            subDirs = Directory.EnumerateDirectories(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            warnings.Add($"Accès refusé au dossier : {directory} ({ex.Message})");
            subDirs = Array.Empty<string>();
        }

        foreach (var subDir in subDirs)
        {
            ScanDirectoryRecursive(subDir, allowedExtensions, files, warnings, cancellationToken);
        }
    }
}
