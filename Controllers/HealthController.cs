using Microsoft.AspNetCore.Mvc;

namespace DataverseMcp.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        _logger.LogInformation("Health check requested");

        try
        {
            var healthInfo = new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                version = "1.0.0",
                environment_variables = new
                {
                    has_dataverse_connectionstring = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DATAVERSE_CONNECTIONSTRING")),
                    dataverse_connectionstring_length = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTIONSTRING")?.Length ?? 0
                },
                runtime = new
                {
                    dotnet_version = Environment.Version.ToString(),
                    os_version = Environment.OSVersion.ToString(),
                    machine_name = Environment.MachineName
                }
            };

            return Ok(healthInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            
            var errorInfo = new
            {
                status = "unhealthy",
                error = ex.Message,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            return StatusCode(500, errorInfo);
        }
    }
}