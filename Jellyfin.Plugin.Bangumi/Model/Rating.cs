using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi.Model;

public class Rating
{
    public int Rank { get; set; }

    public int Total { get; set; }

    public Dictionary<string, int> Count { get; set; } = new();

    public float Score { get; set; }
}