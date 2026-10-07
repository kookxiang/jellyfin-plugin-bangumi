using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedBaseItemManager : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "IsMetadataFetcherEnabled")
            return ((TypeOptions)args![1]!).MetadataFetchers.Contains((string)args[2]!);
        throw new NotSupportedException(targetMethod?.Name);
    }
}
