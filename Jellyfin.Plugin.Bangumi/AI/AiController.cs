using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.AI;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/Bangumi/AI")]
public class AiController(AiProviderClient client, AiStatisticsStore statistics) : ControllerBase
{
    [HttpPost("Test")]
    public async Task<IActionResult> Test([FromBody] AiProviderConfiguration provider, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await client.TestAsync(provider, cancellationToken);
            return Ok(new { Success = true, result.Text, result.Usage, ElapsedMilliseconds = stopwatch.ElapsedMilliseconds });
        }
        catch (ArgumentException error)
        {
            return ProbeFailure(error.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeFailure("模型服务请求超时（60 秒）。");
        }
        catch (HttpRequestException error)
        {
            return ProbeFailure(error.StatusCode.HasValue ? error.Message : "无法连接模型服务，请检查地址和服务器网络。");
        }
        catch (JsonException)
        {
            return ProbeFailure("模型服务没有返回有效 JSON，请检查 Endpoint 和接口格式。");
        }
        catch (InvalidOperationException)
        {
            return ProbeFailure("接口返回成功，但没有可读取的文本回复，请检查接口格式或模型输出。");
        }
    }

    [HttpGet("Statistics")]
    public async Task<IActionResult> Statistics(CancellationToken cancellationToken)
    {
        try { return Ok(new { Success = true, Statistics = await statistics.GetAsync(cancellationToken) }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return ProbeFailure("无法读取用量统计，请检查服务器数据目录权限和统计文件。原有统计不会被覆盖。");
        }
    }

    [HttpPost("Statistics/Clear")]
    public async Task<IActionResult> ClearStatistics(CancellationToken cancellationToken)
    {
        try { return Ok(new { Success = true, Statistics = await statistics.ClearAsync(cancellationToken) }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ProbeFailure("无法清零用量统计，请检查服务器数据目录权限。");
        }
    }

    // A failed probe is a result, not a failed Jellyfin request. This preserves
    // useful diagnostics through hosts that discard bodies on non-2xx responses.
    private OkObjectResult ProbeFailure(string message) => Ok(new { Success = false, Message = message });
}
