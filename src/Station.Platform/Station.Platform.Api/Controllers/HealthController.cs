using Microsoft.AspNetCore.Mvc;

namespace Station.Platform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", service = "station-platform-api" });
    }
}
