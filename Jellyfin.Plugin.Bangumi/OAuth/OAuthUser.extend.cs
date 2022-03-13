using System;

namespace Jellyfin.Plugin.Bangumi.OAuth
{
    public partial class OAuthUser
    {
        public DateTime EffectiveTime { get; set; } = DateTime.Now;

        public bool Expired => ExpireTime < DateTime.Now;
    }
}