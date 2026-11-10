using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    /// <summary>Liveness: la API está arriba (no consulta la base de datos).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new { status = "Healthy" });
}
