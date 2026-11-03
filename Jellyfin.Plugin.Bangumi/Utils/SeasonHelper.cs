using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Utils;

internal static class SeasonHelper
{
    /// <summary>
    /// 用 Bangumi 前传链推断季号：链里必须包含 Jellyfin 系列根条目，
    /// 否则说明是不同世界观、不同演绎、番外/总集篇，季度可能冲突，返回 null。
    /// </summary>
    public static async Task<int?> TryResolveChainSeasonNumberAsync<T>(
        BangumiApi api,
        int subjectId,
        IReadOnlyDictionary<string, string>? seriesProviderIds,
        Logger<T> log,
        CancellationToken token)
    {
        if (subjectId <= 0)
        {
            log.Debug("Season inference skipped: subject id is {SubjectId}", subjectId);
            return null;
        }
        var jellyfinSeriesId = int.TryParse(seriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out var id) ? id : 0;
        if (jellyfinSeriesId <= 0)
        {
            log.Warn("Season inference skipped for subject {SubjectId}: SeriesProviderIds has no Bangumi id", subjectId);
            return null;
        }
        try
        {
            var chain = await api.GetPrequelChainSubjectIds(subjectId, token);
            if (chain.Contains(jellyfinSeriesId))
                return chain.Count;
            log.Debug("Season inference skipped for subject {SubjectId}: series {SeriesRootId} is not in its prequel chain", subjectId, jellyfinSeriesId);
            return null;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            log.Warn("Season inference failed for subject {SubjectId}: {Error}", subjectId, ex.Message);
            return null;
        }
    }
}