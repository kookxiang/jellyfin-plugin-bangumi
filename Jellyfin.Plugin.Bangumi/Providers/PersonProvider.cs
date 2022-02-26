using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class PersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
    {
        private readonly ILogger<PersonProvider> _log;
        private readonly IApplicationPaths _paths;

        public PersonProvider(IApplicationPaths appPaths, ILogger<PersonProvider> logger)
        {
            _log = logger;
            _paths = appPaths;
        }

        public int Order => -5;
        public string Name => Constants.ProviderName;

        public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new MetadataResult<Person> { ResultLanguage = Constants.Language };
            var personId = info.ProviderIds?.GetValueOrDefault(Constants.ProviderName);
            if (string.IsNullOrEmpty(personId))
                return result;
            var person = await Api.GetPerson(personId, token);
            if (person == null)
                return result;
            result.HasMetadata = true;
            result.Item = new Person
            {
                Name = person.Name,
                Overview = person.Summary,
                PremiereDate = person.Birthdate,
                ProductionYear = person.BirthYear
            };
            result.Item.ProviderIds.Add(Constants.ProviderName, $"{person.Id}");
            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance!.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }
    }
}