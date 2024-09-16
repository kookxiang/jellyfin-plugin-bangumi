using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAI_API.Chat;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class OpenApi
{
    [TestMethod]
    public async Task GuessTitle()
    {
        var client = new OpenAIApi();
        var chatClient = client.GetChatClient();
        var prompt = @"
ファイル名にはいくつかの略語があります、この用語集を参照してください。
IV=interview
NC=creditless
その上で、以下のファイル名から、このファイルに最もふさわしいタイトルを当ててください（日本語で、できるだけ短く、説明の必要はありません、直接お答えください、記号は不要です。）

[Aria The Crepuscolo][SP05][Original Preview 60sec. Ver.][BDRIP][1080P][H264_FLAC].mkv
";
        var req = new ChatRequest()
        {
            Model = "gpt-4o",
            Messages = new List<ChatMessage>(){
                new(ChatMessageRole.User, prompt),
            },
            Temperature = 0.3,
        };
        var result = await chatClient.CreateChatCompletionAsync(req);
        var str = result.ToString();

        Console.WriteLine(str);
    }

}