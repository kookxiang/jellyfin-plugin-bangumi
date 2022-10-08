using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Offline;

public class ArchiveDataDownloadTask : IScheduledTask
{
    private const string ArchiveDownloadUrl = "https://github.com/bangumi/Archive/releases/download/archive/dump.zip";
    private const int StreamCopyBufferSize = 8192;
    private readonly PluginDatabase _database;
    private readonly ILogger<ArchiveDataDownloadTask> _log;

    private readonly Plugin _plugin;

    public ArchiveDataDownloadTask(Plugin plugin, ILogger<ArchiveDataDownloadTask> log, PluginDatabase database)
    {
        _plugin = plugin;
        _log = log;
        _database = database;
    }

    public string Key => "ArchiveDataDownloadTask";
    public string Name => "离线数据库更新";
    public string Description => "从 GitHub 下载并更新本地离线数据库";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Enumerable.Empty<TaskTriggerInfo>();
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        _log.LogInformation("download bangumi archive data from {Url}", ArchiveDownloadUrl);

        // read archive file from github
        var httpClient = _plugin.GetHttpClient();
        var response = await httpClient.GetAsync(ArchiveDownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var totalSize = response.Content.Headers.ContentLength!.Value;
        await using var archiveStream = await response.Content.ReadAsStreamAsync(token);
        using var memoryStream = new MemoryStream();

        // download archive data with progress
        int bytesRead;
        var totalRead = 0L;
        var buffer = new byte[StreamCopyBufferSize].AsMemory();
        while ((bytesRead = await archiveStream.ReadAsync(buffer, token)) != 0)
        {
            totalRead += bytesRead;
            progress.Report(70d * totalRead / totalSize);
            await memoryStream.WriteAsync(buffer[..bytesRead], token);
        }

        _log.LogInformation("download complete, total size: {Size}", memoryStream.Length);
        memoryStream.Seek(0, SeekOrigin.Begin);
        progress.Report(70);

        // open archive file and extract data
        var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        _database.Reset();

        _log.LogInformation("importing subject data from archive");
        ImportData(_database.Subjects, zipArchive.GetEntry("subject.jsonlines"));
        progress.Report(80);

        _log.LogInformation("importing person data from archive");
        ImportData(_database.Persons, zipArchive.GetEntry("person.jsonlines"));
        progress.Report(90);

        _log.LogInformation("importing episode data from archive");
        ImportData(_database.Episodes, zipArchive.GetEntry("episode.jsonlines"));

        _log.LogInformation("archive data imported");
        progress.Report(100);
    }

    private static void ImportData<T>(ILiteCollection<T> collection, ZipArchiveEntry? entry)
    {
        if (entry == null)
            return;

        collection.InsertBulk(new JsonLinesProvider<T>(entry.Open()));
    }
}