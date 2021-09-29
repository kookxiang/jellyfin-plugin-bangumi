using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi.API
{
    /**
     * 评分
     */
    internal class Rating
    {
        public int Total { get; set; }

        public float Score { get; set; }

        public Dictionary<string, int> Count { get; set; }
    }
}