using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/Scripts")]
[AllowAnonymous]
public class ScriptsController : ControllerBase
{
    private static readonly Assembly ResourceAssembly = typeof(ScriptsController).Assembly;
    private const string ResourcePrefix = "Jellyfin.Plugin.JellyReview.Web.Scripts.";

    [HttpGet("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetScript(string name)
    {
        var resourceName = ResourcePrefix + name;
        var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, "application/javascript");
    }
}
