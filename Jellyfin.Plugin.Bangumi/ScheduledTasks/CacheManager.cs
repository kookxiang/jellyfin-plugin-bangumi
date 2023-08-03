using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.ScheduledTasks
{
    public class CacheManager : IScheduledTask
    {
        private readonly Dictionary<string, CachedResult> _cache;
        private readonly ILogger<CacheManager> _log;

        public string Name => throw new NotImplementedException();

        public string Key => throw new NotImplementedException();

        public string Description => throw new NotImplementedException();

        public string Category => throw new NotImplementedException();

        public CacheManager(ILogger<CacheManager> log)
        {
            _cache = new Dictionary<string, CachedResult>();
            _log = log;
        }
        public string GenerateCacheKey(params object[] parameters)
        {
            return string.Join("_", parameters.Select(p => p.ToString()));
        }

        public async Task<TResult?> GetCachedResult<TResult>(string cacheKey, Func<Task<TResult?>> getDataFunc, TimeSpan cacheValidity)
        {
            if (_cache.ContainsKey(cacheKey) && IsCacheValid(_cache[cacheKey]))
            {
                _log.LogDebug("Use cache for: {cacheKey}", cacheKey);
                return (TResult?)_cache[cacheKey].Result;
            }

            TResult? result = await getDataFunc.Invoke();

            DateTime expirationTime = DateTime.UtcNow.Add(cacheValidity);
            _cache[cacheKey] = new CachedResult(result, expirationTime);
            _log.LogDebug("Save cache for: {cacheKey}", cacheKey);
            return result;
        }
        /// <summary>
        /// Check the expiration time of the cached result
        /// </summary>
        /// <param name="cachedResult"></param>
        /// <returns></returns>
        private bool IsCacheValid(CachedResult cachedResult)
        {
            return cachedResult.ExpirationTime > DateTime.UtcNow;
        }

        /// <summary>
        /// Represents a cached result with an expiration time
        /// </summary>
        private class CachedResult
        {
            public object? Result { get; }
            public DateTime ExpirationTime { get; }

            public CachedResult(object? result, DateTime expirationTime)
            {
                Result = result;
                ExpirationTime = expirationTime;
            }
        }
        private void ClearExpiredCache()
        {
            lock (_cache)
            {
                var expiredKeys = _cache.Keys.Where(k => !IsCacheValid(_cache[k])).ToList();
                foreach (var key in expiredKeys)
                {
                    _cache.Remove(key);
                    _log.LogDebug("Expired cache removed for: {key}", key);
                }
            }
        }

        // TODO 定期检查和清理过期的缓存项
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            throw new NotImplementedException();
        }
    }
}