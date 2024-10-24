using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Archive;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class ArchiveDownloadTask(BangumiApi api, ArchiveData archive, ILogger<ArchiveDownloadTask> log) : IScheduledTask
{
    private const string ArchiveReleaseUrl = "https://raw.githubusercontent.com/bangumi/Archive/master/aux/latest.json";
    private const int StreamCopyBufferSize = 16 * 1024;

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

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(archive.BasePath);
        Directory.CreateDirectory(archive.TempPath);

        var archiveMeta = await GetLatestArchiveMeta(token);

        progress.Report(5);
        log.LogInformation("download bangumi archive data from {Url}", archiveMeta.DownloadUrl);

        // read archive file from GitHub
        using var httpClient = api.GetHttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(2);
        using var response = await httpClient.GetAsync(archiveMeta.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var httpStream = await response.Content.ReadAsStreamAsync(token);
        var totalSize = response.Content.Headers.ContentLength ?? archiveMeta.Size;
        using var memoryStream = new MemoryStream();

        // download archive data with progress
        int bytesRead;
        var totalRead = 0L;
        var buffer = new byte[StreamCopyBufferSize].AsMemory();
        while ((bytesRead = await httpStream.ReadAsync(buffer, token)) != 0)
        {
            totalRead += bytesRead;
            progress.Report(5D + 55D * totalRead / totalSize);
            await memoryStream.WriteAsync(buffer[..bytesRead], token);
        }

        log.LogInformation("download complete, total size: {Size}", memoryStream.Length);
        memoryStream.Seek(0, SeekOrigin.Begin);
        progress.Report(60);

        using var zipStream = new ZipArchive(memoryStream, ZipArchiveMode.Read);
        progress.Report(65);

        var completed = 0;
        foreach (var oldStore in archive.Stores)
        {
            var newFileName = Path.GetRandomFileName();
            var newStore = oldStore.Fork(archive.TempPath, newFileName);
            var fileName = oldStore.FileName;

            log.LogInformation("decompressing {FileName} to {TempName}", fileName, $"temp/{newFileName}");
            var entry = zipStream.GetEntry(fileName);
            if (entry == null)
                throw new FileNotFoundException($"{fileName} not found in archive file");
            progress.Report(65D + 30D * (completed + 0.2D) / archive.Stores.Count);

            await using (var writeStream = File.OpenWrite(newStore.FilePath))
            {
                await using var readStream = entry.Open();
                await readStream.CopyToAsync(writeStream, token);
                await writeStream.FlushAsync(token);
            }

            progress.Report(65D + 30D * (completed + 0.5D) / archive.Stores.Count);

            log.LogInformation("generating index for {TempName}", $"temp/{newFileName}");
            await newStore.GenerateIndex(token);
            progress.Report(65D + 30D * (completed + 0.8D) / archive.Stores.Count);

            log.LogInformation("replacing {FileName} and index files with {TempName}", fileName, $"temp/{newFileName}");
            await oldStore.Move(archive.TempPath, Path.GetRandomFileName());
            await newStore.Move(archive.BasePath, fileName);

            progress.Report(65D + 30D * ++completed / archive.Stores.Count);
        }

        await archive.SubjectEpisode.GenerateIndex(token);

        log.LogInformation("update completed. cleaning up temp files");
        Directory.Delete(archive.TempPath, true);
    }

    private async Task<ArchiveReleaseMeta> GetLatestArchiveMeta(CancellationToken token)
    {
        using var httpClient = api.GetHttpClient();
        using var response = await httpClient.GetAsync(ArchiveReleaseUrl, token);
        response.EnsureSuccessStatusCode();
        var jsonString = await response.Content.ReadAsStringAsync(token);
        return JsonSerializer.Deserialize<ArchiveReleaseMeta>(jsonString, Constants.JsonSerializerOptions)!;
    }

    public class ArchiveReleaseMeta
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