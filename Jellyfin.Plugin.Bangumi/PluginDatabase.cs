using System;
using System.IO;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using LiteDB;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi;

public class PluginDatabase : IDisposable
{
    private readonly LiteDatabase _database;

    public ILiteCollection<Episode> Episodes;

    public ILiteCollection<OAuthUser> Logins;

    public ILiteCollection<Person> Persons;

    public ILiteCollection<Subject> Subjects;

    public PluginDatabase(IApplicationPaths paths)
    {
        _database = new LiteDatabase(Path.Join(paths.ProgramDataPath, "bangumi.db"));

        Logins = _database.GetCollection<OAuthUser>();

        Subjects = _database.GetCollection<Subject>();

        Persons = _database.GetCollection<Person>();

        Episodes = _database.GetCollection<Episode>();
        Episodes.EnsureIndex(x => x.ParentId);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Reset()
    {
        _database.DropCollection(Subjects.Name);
        Subjects = _database.GetCollection<Subject>();

        _database.DropCollection(Persons.Name);
        Persons = _database.GetCollection<Person>();

        _database.DropCollection(Episodes.Name);
        Episodes = _database.GetCollection<Episode>();
        Episodes.EnsureIndex(x => x.ParentId);
    }
}