using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Bangumi
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            IHttpClientFactory httpClientFactory)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _httpClientFactory = httpClientFactory;
        }

        /// <inheritdoc />
        public override string Name => Constants.PluginName;

        /// <inheritdoc />
        public override Guid Id => Guid.Parse(Constants.PluginGuid);

        public static Plugin Instance { get; private set; }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", GetType().Namespace)
                }
            };
        }

        public HttpClient GetHttpClient()
        {
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(Name, Version.ToString()));

            return httpClient;
        }
    }
}