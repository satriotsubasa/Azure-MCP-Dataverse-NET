using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DataverseMcp.FunctionApp.Functions;

public class HealthFunction
{
    private readonly ILogger<HealthFunction> _logger;

    public HealthFunction(ILogger<HealthFunction> logger)
    {
        _logger = logger;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options")] HttpRequestData req)
    {
        _logger.LogInformation("Health check requested");

        try
        {
            // Handle CORS preflight
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                var corsResponse = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(corsResponse);
                return corsResponse;
            }

            // Simple health check without dependencies
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

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(healthInfo);
            AddCorsHeaders(response);
            return response;
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

            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(errorInfo);
            AddCorsHeaders(response);
            return response;
        }
    }

    private static void AddCorsHeaders(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        response.Headers.Add("Access-Control-Max-Age", "3600");
    }
}