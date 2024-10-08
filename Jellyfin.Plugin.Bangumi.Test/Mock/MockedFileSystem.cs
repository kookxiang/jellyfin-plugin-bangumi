using System;
using System.Collections.Generic;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedFileSystem : IFileSystem
{
    public bool AreEqual(string path1, string path2)
    {
        throw new NotImplementedException();
    }

    public bool ContainsSubPath(string parentPath, string path)
    {
        throw new NotImplementedException();
    }

    public void CreateShortcut(string shortcutPath, string target)
    {
        throw new NotImplementedException();
    }

    public void DeleteFile(string path)
    {
        throw new NotImplementedException();
    }

    public bool DirectoryExists(string path)
    {
        throw new NotImplementedException();
    }

    public bool FileExists(string path)
    {
        throw new NotImplementedException();
    }

    public DateTime GetCreationTimeUtc(FileSystemMetadata info)
    {
        throw new NotImplementedException();
    }

    public DateTime GetCreationTimeUtc(string path)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<FileSystemMetadata> GetDirectories(string path, bool recursive = false)
    {
        throw new NotImplementedException();
    }

    public FileSystemMetadata GetDirectoryInfo(string path)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetDirectoryPaths(string path, bool recursive = false)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<FileSystemMetadata> GetDrives()
    {
        throw new NotImplementedException();
    }

    public FileSystemMetadata GetFileInfo(string path)
    {
        throw new NotImplementedException();
    }

    public string GetFileNameWithoutExtension(FileSystemMetadata info)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetFilePaths(string path, bool recursive = false)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetFilePaths(string path, string[] extensions, bool enableCaseSensitiveExtensions, bool recursive)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<FileSystemMetadata> GetFiles(string path, bool recursive = false)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<FileSystemMetadata> GetFiles(string path, IReadOnlyList<string> extensions, bool enableCaseSensitiveExtensions, bool recursive)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<FileSystemMetadata> GetFileSystemEntries(string path, bool recursive = false)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetFileSystemEntryPaths(string path, bool recursive = false)
    {
        throw new NotImplementedException();
    }

    public FileSystemMetadata GetFileSystemInfo(string path)
    {
        throw new NotImplementedException();
    }

    public DateTime GetLastWriteTimeUtc(FileSystemMetadata info)
    {
        throw new NotImplementedException();
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        throw new NotImplementedException();
    }

    public string GetValidFilename(string filename)
    {
        throw new NotImplementedException();
    }

    public bool IsPathFile(string path)
    {
        throw new NotImplementedException();
    }

    public bool IsShortcut(string filename)
    {
        throw new NotImplementedException();
    }

    public string MakeAbsolutePath(string folderPath, string filePath)
    {
        throw new NotImplementedException();
    }

    public string ResolveShortcut(string filename)
    {
        throw new NotImplementedException();
    }

    public void SetAttributes(string path, bool isHidden, bool readOnly)
    {
        throw new NotImplementedException();
    }

    public void SetHidden(string path, bool isHidden)
    {
        throw new NotImplementedException();
    }

    public void SwapFiles(string file1, string file2)
    {
        throw new NotImplementedException();
    }
}