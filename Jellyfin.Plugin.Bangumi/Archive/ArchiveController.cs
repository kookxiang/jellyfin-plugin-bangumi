using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.Archive;

[ApiController]
[Route("Plugins/Bangumi/Archive")]
public class OAuthController(ArchiveData archive)
    : ControllerBase
{
    [HttpGet("Status")]
    [Authorize]
    public Dictionary<string, object?> Status()
    {
        var totalSize = 0L;
        DateTime? lastModifyTime = null;

        var directory = new DirectoryInfo(archive.BasePath);
        foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if (lastModifyTime == null)
                lastModifyTime = info.LastWriteTime;
            else if (info.LastWriteTime.CompareTo(lastModifyTime) > 0)
                lastModifyTime = info.LastWriteTime;
            if (info is FileInfo fileInfo)
                totalSize += fileInfo.Length;
        }

        return new Dictionary<string, object?>
        {
            ["path"] = archive.BasePath,
            ["size"] = totalSize,
            ["time"] = lastModifyTime
        };
    }

    [HttpDelete("Store")]
    [Authorize]
    public bool Delete()
    {
        Directory.Delete(archive.BasePath, true);
        Directory.CreateDirectory(archive.BasePath);
        return false;
    }
}