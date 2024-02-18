using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.IO;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Serialization;
using System.Linq;

namespace Jellyfin.Plugin.Bangumi;

[Route("/ServiceTest", "GET")]

public class AddFolder: IReturnVoid
{
}

[Authenticated]
public class FolderSyncApi : IService
{

    public FolderSyncApi()
    {
    }

    public object Get(AddFolder request)
    {
        return new
        {
            Name = "Hello World"
        };
    }
}
