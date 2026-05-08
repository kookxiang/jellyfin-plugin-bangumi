using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedMediaSourceManager : IMediaSourceManager
{
    private readonly Dictionary<string, (long Size, long RunTimeTicks)> _fileMediaInfo = new();

    public void SetFileMediaInfo(string path, long size, TimeSpan duration)
    {
        var normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        _fileMediaInfo[normalizedPath] = (size, duration.Ticks);
    }

    public List<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User? user = null)
    {
        var list = new List<MediaSourceInfo>();
        if (item?.Path != null && _fileMediaInfo.TryGetValue(item.Path, out var info))
        {
            list.Add(new MediaSourceInfo
            {
                Id = item.Id.ToString(),
                Path = item.Path,
                Protocol = MediaProtocol.File,
                Type = MediaSourceType.Default,
                Size = info.Size,
                RunTimeTicks = info.RunTimeTicks,
            });
        }
        return list;
    }

    public Task<MediaSourceInfo> GetMediaSource(BaseItem item, string mediaSourceId, string liveStreamId, bool enablePathSubstitution, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<LiveStreamResponse> OpenLiveStream(LiveStreamRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Tuple<LiveStreamResponse, IDirectStreamProvider>> OpenLiveStreamInternal(LiveStreamRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<MediaSourceInfo> GetLiveStream(string id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Tuple<MediaSourceInfo, IDirectStreamProvider>> GetLiveStreamWithDirectStreamProvider(string id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public ILiveStream GetLiveStreamInfo(string id)
    {
        throw new NotImplementedException();
    }

    public ILiveStream GetLiveStreamInfoByUniqueId(string uniqueId)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<MediaSourceInfo>> GetRecordingStreamMediaSources(ActiveRecordingInfo info, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task CloseLiveStream(string id)
    {
        throw new NotImplementedException();
    }

    public Task<MediaSourceInfo> GetLiveStreamMediaInfo(string id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public bool SupportsDirectStream(string path, MediaProtocol protocol)
    {
        throw new NotImplementedException();
    }

    public MediaProtocol GetPathProtocol(string path)
    {
        throw new NotImplementedException();
    }

    public void SetDefaultAudioAndSubtitleStreamIndices(BaseItem item, MediaSourceInfo source, User user)
    {
        throw new NotImplementedException();
    }

    public Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, bool isAudio, string cacheKey, bool addProbeDelay, bool isLiveStream, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public void AddParts(IEnumerable<IMediaSourceProvider> providers)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<MediaStream> GetMediaStreams(Guid itemId)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<MediaStream> GetMediaStreams(MediaStreamQuery query)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<MediaAttachment> GetMediaAttachments(Guid itemId)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<MediaSourceInfo>> GetPlaybackMediaSources(BaseItem item, User user, bool allowMediaProbe, bool enablePathSubstitution, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    IReadOnlyList<MediaSourceInfo> IMediaSourceManager.GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User user)
    {
        return GetStaticMediaSources(item, enablePathSubstitution, user);
    }
}
