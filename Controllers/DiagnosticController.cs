using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerPlatform.Dataverse.Client;
using MarkMpn.Sql4Cds.Engine;

namespace DataverseMcp.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticController : ControllerBase
{
    private readonly ILogger<DiagnosticController> _logger;

    public DiagnosticController(ILogger<DiagnosticController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        _logger.LogInformation("Diagnostic endpoint called");

        try
        {
            var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTIONSTRING");
            
            var diagnosticInfo = new
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                environment_check = new
                {
                    has_connectionstring = !string.IsNullOrEmpty(connectionString),
                    connectionstring_length = connectionString?.Length ?? 0,
                    connectionstring_preview = connectionString?.Length > 50 ? 
                        connectionString[..50] + "..." : connectionString ?? "null"
                },
                connection_tests = await TestConnections(connectionString)
            };

            return Ok(diagnosticInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostic failed");
            
            var errorInfo = new
            {
                error = "Diagnostic failed",
                message = ex.Message,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            return StatusCode(500, errorInfo);
        }
    }

    private async Task<object> TestConnections(string? connectionString)
    {
        var tests = new List<object>();

        // Test 1: Connection string validation
        tests.Add(await TestConnectionStringFormat(connectionString));
        
        // Test 2: ServiceClient initialization
        tests.Add(await TestServiceClientInit(connectionString));
        
        // Test 3: Sql4CDS initialization  
        tests.Add(await TestSql4CdsInit(connectionString));

        return tests;
    }

    private async Task<object> TestConnectionStringFormat(string? connectionString)
    {
        await Task.Delay(1); // Make async
        
        try
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return new { test = "connectionstring_format", success = false, error = "Connection string is null or empty" };
            }

            var parts = connectionString.Split(';');
            var parsedParts = new Dictionary<string, string>();
            
            foreach (var part in parts)
            {
                if (part.Contains('='))
                {
                    var keyValue = part.Split('=', 2);
                    if (keyValue.Length == 2)
                    {
                        parsedParts[keyValue[0]] = keyValue[1].Length > 20 ? keyValue[1][..20] + "..." : keyValue[1];
                    }
                }
            }

            var requiredKeys = new[] { "AuthType", "Url", "ClientId", "ClientSecret" };
            var missingKeys = requiredKeys.Where(key => !parsedParts.ContainsKey(key)).ToArray();

            return new 
            { 
                test = "connectionstring_format",
                success = missingKeys.Length == 0,
                error = missingKeys.Length > 0 ? $"Missing keys: {string.Join(", ", missingKeys)}" : null,
                parsed_parts = parsedParts,
                parts_count = parts.Length
            };
        }
        catch (Exception ex)
        {
            return new { test = "connectionstring_format", success = false, error = ex.Message };
        }
    }

    private async Task<object> TestServiceClientInit(string? connectionString)
    {
        await Task.Delay(1); // Make async
        
        try
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return new { test = "serviceclient_init", success = false, error = "No connection string provided" };
            }

            _logger.LogInformation("Testing ServiceClient initialization...");
            
            using var serviceClient = new ServiceClient(connectionString);
            
            if (serviceClient.IsReady)
            {
                return new 
                { 
                    test = "serviceclient_init",
                    success = true,
                    connection_id = serviceClient.ConnectedOrgId.ToString(),
                    org_name = serviceClient.ConnectedOrgFriendlyName ?? "unknown",
                    version = serviceClient.ConnectedOrgVersion?.ToString() ?? "unknown"
                };
            }
            else
            {
                return new 
                { 
                    test = "serviceclient_init",
                    success = false,
                    error = "ServiceClient not ready",
                    last_error = serviceClient.LastError ?? "No error details available"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ServiceClient initialization failed");
            return new 
            { 
                test = "serviceclient_init",
                success = false,
                error = ex.Message,
                exception_type = ex.GetType().Name
            };
        }
    }

    private async Task<object> TestSql4CdsInit(string? connectionString)
    {
        await Task.Delay(1); // Make async
        
        try
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return new { test = "sql4cds_init", success = false, error = "No connection string provided" };
            }

            _logger.LogInformation("Testing Sql4CDS initialization...");
            
            using var serviceClient = new ServiceClient(connectionString);
            
            if (!serviceClient.IsReady)
            {
                return new 
                { 
                    test = "sql4cds_init",
                    success = false,
                    error = "ServiceClient not ready for SQL4CDS",
                    last_error = serviceClient.LastError ?? "No error details"
                };
            }

            using var sql4CdsConnection = new Sql4CdsConnection(serviceClient) { UseLocalTimeZone = true };
            using var command = sql4CdsConnection.CreateCommand();
            command.CommandText = "SELECT TOP 1 logicalname FROM metadata.entity WHERE logicalname = 'contact'";
            
            using var reader = await command.ExecuteReaderAsync();
            var hasData = reader.Read();
            var result = hasData ? reader.GetString(0) : "no data";

            return new 
            { 
                test = "sql4cds_init",
                success = true,
                sample_query_result = result,
                connection_ready = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL4CDS initialization failed");
            return new 
            { 
                test = "sql4cds_init",
                success = false,
                error = ex.Message,
                exception_type = ex.GetType().Name
            };
        }
    }
}