using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Archive;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class ArchiveDownloadTask(BangumiApi api, ArchiveData archive, ITaskManager taskManager, Logger<ArchiveDownloadTask> log)
    : IScheduledTask
{
    private const string ArchiveReleaseUrl = "https://raw.githubusercontent.com/bangumi/Archive/master/aux/latest.json";

    public string Key => "ArchiveDataDownloadTask";
    public string Name => "离线数据库更新";
    public string Description => "从 GitHub 下载并更新本地离线数据库";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromDays(14).Ticks,
                MaxRuntimeTicks = TimeSpan.FromHours(1).Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(archive.BasePath);
        Directory.CreateDirectory(archive.TempPath);

        var archiveMeta = await GetLatestArchiveMeta(cancellationToken);

        progress.Report(5);
        log.Info("download bangumi archive data from {Url}", archiveMeta.DownloadUrl);

        var downloadingProgress = new Progress<double>();
        downloadingProgress.ProgressChanged += (sender, args) => progress.Report(5D + 55D * args);
        using var memoryStream = await api.FetchStream(archiveMeta.DownloadUrl, downloadingProgress, cancellationToken);
        log.Info("download complete, total size: {Size}", memoryStream.Length);
        progress.Report(60);

        using var zipStream = new ZipArchive(memoryStream, ZipArchiveMode.Read);
        progress.Report(65);

        var completed = 0;
        foreach (var oldStore in archive.Stores)
        {
            var newFileName = Path.GetRandomFileName();
            var newStore = oldStore.Fork(archive.TempPath, newFileName);
            var fileName = oldStore.FileName;

            log.Info("decompressing {FileName} to {TempName}", fileName, $"temp/{newFileName}");
            var entry = zipStream.GetEntry(fileName);
            if (entry == null)
                throw new FileNotFoundException($"{fileName} not found in archive file");
            progress.Report(65D + (30D * (completed + 0.2D) / archive.Stores.Count));

            await using (var writeStream = File.OpenWrite(newStore.FilePath))
            {
                await using var readStream = entry.Open();
                await readStream.CopyToAsync(writeStream, cancellationToken);
                await writeStream.FlushAsync(cancellationToken);
            }

            progress.Report(65D + (30D * (completed + 0.5D) / archive.Stores.Count));

            log.Info("generating index for {TempName}", $"temp/{newFileName}");
            await newStore.GenerateIndex(cancellationToken);
            progress.Report(65D + (30D * (completed + 0.8D) / archive.Stores.Count));

            log.Info("replacing {FileName} and index files with {TempName}", fileName, $"temp/{newFileName}");
            await oldStore.Move(archive.TempPath, Path.GetRandomFileName());
            await newStore.Move(archive.BasePath, fileName);

            progress.Report(65D + (30D * ++completed / archive.Stores.Count));
        }

        await archive.SubjectRelations.GenerateIndex(zipStream, cancellationToken);
        await archive.SubjectEpisodeRelation.GenerateIndex(cancellationToken);
        await archive.SubjectPersonRelation.GenerateIndex(zipStream, cancellationToken);

        log.Info("update completed. cleaning up temp files");
        Directory.Delete(archive.TempPath, true);

        if (Plugin.Instance?.Configuration.RefreshRatingWhenArchiveUpdate == true) taskManager.Execute<RatingRefreshTask>();
    }

    private async Task<ArchiveReleaseMeta> GetLatestArchiveMeta(CancellationToken token)
    {
        return (await api.Get<ArchiveReleaseMeta>(ArchiveReleaseUrl, null, token))!;
    }

    internal class ArchiveReleaseMeta
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdateTime { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = "";
    }
}
