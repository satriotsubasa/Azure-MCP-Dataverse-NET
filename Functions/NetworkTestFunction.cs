using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Diagnostics;

namespace DataverseMcp.FunctionApp.Functions;

public class NetworkTestFunction
{
    private readonly ILogger<NetworkTestFunction> _logger;
    private readonly HttpClient _httpClient;

    public NetworkTestFunction(ILogger<NetworkTestFunction> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    [Function("NetworkTest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options")] HttpRequestData req)
    {
        _logger.LogInformation("Network connectivity test started");

        try
        {
            // Handle CORS preflight
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                var corsResponse = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(corsResponse);
                return corsResponse;
            }

            var testEndpoints = new[]
            {
                new { name = "Microsoft", url = "https://microsoft.com", description = "Microsoft main site" },
                new { name = "Google", url = "https://google.com", description = "Google main site" },
                new { name = "iManage", url = "https://riotinto-test.cloudimanage.com/work/web/", description = "Rio Tinto iManage instance" },
                new { name = "Dataverse", url = "https://rt-pp-legal-dev.crm.dynamics.com", description = "Rio Tinto Dataverse environment" },
                new { name = "Dataverse API", url = "https://rt-pp-legal-dev.crm.dynamics.com/api/data/v9.2/", description = "Dataverse Web API endpoint" },
                new { name = "Azure", url = "https://azure.microsoft.com", description = "Azure main site" }
            };

            var testResults = new List<object>();

            foreach (var endpoint in testEndpoints)
            {
                testResults.Add(await TestEndpoint(endpoint.name, endpoint.url, endpoint.description));
            }

            var networkInfo = new
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                test_summary = new
                {
                    total_tests = testResults.Count,
                    successful = testResults.Count(r => ((dynamic)r).success),
                    failed = testResults.Count(r => !((dynamic)r).success)
                },
                azure_function_info = new
                {
                    machine_name = Environment.MachineName,
                    os_version = Environment.OSVersion.ToString(),
                    dotnet_version = Environment.Version.ToString(),
                    environment_variables = new
                    {
                        http_proxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? "not set",
                        https_proxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? "not set",
                        no_proxy = Environment.GetEnvironmentVariable("NO_PROXY") ?? "not set"
                    }
                },
                test_results = testResults
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(networkInfo);
            AddCorsHeaders(response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network test failed");
            
            var errorInfo = new
            {
                error = "Network test failed",
                message = ex.Message,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(errorInfo);
            AddCorsHeaders(response);
            return response;
        }
    }

    private async Task<object> TestEndpoint(string name, string url, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Testing connectivity to {Name}: {Url}", name, url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Azure-Function-Network-Test/1.0");

            using var response = await _httpClient.SendAsync(request);
            stopwatch.Stop();

            var headers = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

            return new
            {
                name,
                url,
                description,
                success = true,
                status_code = (int)response.StatusCode,
                status_text = response.StatusCode.ToString(),
                response_time_ms = stopwatch.ElapsedMilliseconds,
                headers = new
                {
                    server = headers.ContainsKey("Server") ? headers["Server"] : "unknown",
                    content_type = headers.ContainsKey("Content-Type") ? headers["Content-Type"] : "unknown",
                    location = headers.ContainsKey("Location") ? headers["Location"] : null
                },
                content_length = response.Content.Headers.ContentLength ?? 0,
                final_url = response.RequestMessage?.RequestUri?.ToString() ?? url
            };
        }
        catch (HttpRequestException httpEx)
        {
            stopwatch.Stop();
            _logger.LogWarning(httpEx, "HTTP request failed for {Name}: {Url}", name, url);
            
            return new
            {
                name,
                url,
                description,
                success = false,
                error = "HTTP request failed",
                error_details = httpEx.Message,
                response_time_ms = stopwatch.ElapsedMilliseconds,
                error_type = "HttpRequestException"
            };
        }
        catch (TaskCanceledException timeoutEx)
        {
            stopwatch.Stop();
            _logger.LogWarning(timeoutEx, "Request timeout for {Name}: {Url}", name, url);
            
            return new
            {
                name,
                url,
                description,
                success = false,
                error = "Request timeout",
                error_details = timeoutEx.Message,
                response_time_ms = stopwatch.ElapsedMilliseconds,
                error_type = "TaskCanceledException"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error testing {Name}: {Url}", name, url);
            
            return new
            {
                name,
                url,
                description,
                success = false,
                error = "Unexpected error",
                error_details = ex.Message,
                response_time_ms = stopwatch.ElapsedMilliseconds,
                error_type = ex.GetType().Name
            };
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