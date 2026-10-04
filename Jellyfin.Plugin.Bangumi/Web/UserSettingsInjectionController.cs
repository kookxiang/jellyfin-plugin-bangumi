using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.Web;

[ApiController]
[Route("Plugins/Bangumi/WebInjection")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class UserSettingsInjectionController(UserSettingsInjectionService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<UserSettingsInjectionStatus> GetStatus() => service.GetStatus();

    [HttpPut]
    public ActionResult<UserSettingsInjectionStatus> SetEnabled([FromQuery] bool enabled)
    {
        var status = service.SetEnabled(enabled);
        if (enabled && !status.Active)
            return StatusCode(StatusCodes.Status409Conflict, status);
        return status;
    }
}
