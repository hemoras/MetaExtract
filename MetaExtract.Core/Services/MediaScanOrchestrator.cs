using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

public sealed record ScanProgress(int Processed, int Total, string? CurrentFileName);

/// <summary>
/// Orchestre un scan complet : parcours récursif des dossiers puis
/// extraction des métadonnées de chaque fichier trouvé, avec un
/// parallélisme borné (mediainfo.exe étant lancé en process externe par
/// fichier, on limite le nombre de process concurrents) et un report de
/// progression pour l'UI.
/// </summary>
public sealed class MediaScanOrchestrator
{
    private readonly IMediaInfoProvider _provider;
    private readonly int _maxDegreeOfParallelism;

    public MediaScanOrchestrator(IMediaInfoProvider provider, int? maxDegreeOfParallelism = null)
    {
        _provider = provider;
        _maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism ?? Environment.ProcessorCount);
    }

    public async Task<IReadOnlyList<VideoFileRecord>> ScanAsync(
        IEnumerable<string> rootFolders,
        IReadOnlySet<string> allowedExtensions,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanResult = FolderScanner.FindVideoFiles(rootFolders, allowedExtensions, cancellationToken);
        var files = scanResult.Files;
        var total = files.Count;

        var results = new VideoFileRecord?[total];
        var processed = 0;
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);

        var tasks = new List<Task>(total);
        for (int i = 0; i < total; i++)
        {
            int index = i;
            string file = files[i];

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results[index] = await _provider.ExtractAsync(file, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    var done = Interlocked.Increment(ref processed);
                    progress?.Report(new ScanProgress(done, total, Path.GetFileName(file)));
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return results.Where(r => r is not null).Select(r => r!).ToList();
    }
}
