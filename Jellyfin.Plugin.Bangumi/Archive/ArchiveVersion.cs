using System;

namespace Jellyfin.Plugin.Bangumi.Archive;

// Bump IndexVersion when existing archives need their local indexes rebuilt.
public sealed record ArchiveVersion(long Id, DateTime UpdateTime, long Size, int IndexVersion = 1);
