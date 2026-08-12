using Microsoft.AspNetCore.Mvc;

namespace Station.WebHost.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", service = "station-embedded-web" });
    }
}
