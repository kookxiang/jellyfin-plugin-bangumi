using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class PersonImageProvider(BangumiApi api)
    : IRemoteImageProvider, IHasOrder
{
    public int Order => -5;

    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Person or MusicArtist;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? imageUrl;

        var personId = item.GetProviderId(Constants.ProviderName);
        if (string.IsNullOrEmpty(personId))
            return [];
        if (personId.StartsWith(Constants.CharacterIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(personId.AsSpan(Constants.CharacterIdPrefix.Length), out var id))
                return [];

            // Bangumi 角色图片部分是长条比例，需要在 Jellyfin 自定义 CSS 中添加：
            // /* 针对人物卡片图片容器 */
            // .portraitCard.cardImageContainer,
            // .personCard .cardImageContainer {
            //     background-size: cover !important;
            //     background-position: center top !important;  /* 从顶部开始显示，裁掉底部 */
            // }
            imageUrl = await api.GetCharacterImage(id, cancellationToken);
        }
        else
        {
            if (!int.TryParse(personId, out var id))
                return [];

            imageUrl = await api.GetPersonImage(id, cancellationToken);
        }

        if (imageUrl != null)
            return new List<RemoteImageInfo>
            {
                new()
                {
                    ProviderName = Constants.PluginName,
                    Type = ImageType.Primary,
                    Url = imageUrl
                }
            };

        return [];
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
