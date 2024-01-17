using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using OpenAI_API.Chat;

namespace Jellyfin.Plugin.Bangumi;

public class OpenAIApi
{

    private readonly OpenAI_API.OpenAIAPI _api;
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    private static string FormatEndpoint(string endpoint)
    {
        return endpoint.TrimEnd('/') + "/{0}/{1}";
    }

    public OpenAIApi()
    {
        _api = new OpenAI_API.OpenAIAPI(new OpenAI_API.APIAuthentication(""));
    }

    public IChatEndpoint GetChatClient()
    {
        _api.Auth.ApiKey = Configuration.OpenaiToken;
        _api.ApiUrlFormat = FormatEndpoint(Configuration.OpenaiEndpoint);
        return _api.Chat;
    }

    public async Task<string> SummarizeTitleFromFilename(string filename, CancellationToken token)
    {
        // strip path for filename
        filename = Path.GetFileName(filename);
        var chatClient = GetChatClient();
        var prompt = Configuration.OpenaiPrompt + "\n\n" + filename;
        var req = new ChatRequest()
        {
            Model = Configuration.OpenaiModel,
            Messages = new List<ChatMessage>(){
                new(ChatMessageRole.User, prompt),
            },
            Temperature = 0.3,
        };
        var result = await chatClient.CreateChatCompletionAsync(req);
        return result.ToString();
    }

}