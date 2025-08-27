using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using DataverseMcp.FunctionApp.Models;
using DataverseMcp.FunctionApp.Services;

namespace DataverseMcp.FunctionApp.Functions;

public class HttpTriggerFunction
{
    private readonly ILogger<HttpTriggerFunction> _logger;
    private readonly DataverseService _dataverseService;

    public HttpTriggerFunction(
        ILogger<HttpTriggerFunction> logger,
        DataverseService dataverseService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
    }

    [Function("HttpTrigger")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options")] HttpRequestData req)
    {
        _logger.LogInformation("Processing request: {Method} {Url}", req.Method, req.Url);

        try
        {
            // Handle CORS preflight
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return CreateCorsResponse(req, HttpStatusCode.OK);
            }

            // Handle GET requests (health check and diagnostics)
            if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleGetRequest(req);
            }

            // Handle POST requests (MCP protocol)
            if (req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleMcpRequest(req);
            }

            return CreateErrorResponse(req, HttpStatusCode.MethodNotAllowed, "Method not allowed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request");
            return CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error");
        }
    }

    private async Task<HttpResponseData> HandleGetRequest(HttpRequestData req)
    {
        // Check for diagnostic parameters
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        
        if (query["dataverse-test"] != null)
        {
            return await HandleDataverseTest(req);
        }
        
        if (query["test-search"] != null)
        {
            var searchQuery = query["q"] ?? "test";
            return await HandleTestSearch(req, searchQuery);
        }

        // Default: Return MCP server descriptor for ChatGPT registration
        var serverInfo = new
        {
            name = "Dataverse MCP Server - .NET Enterprise",
            version = "1.0.0",
            description = "MCP server for ChatGPT integration with Microsoft Dataverse using .NET and SQL4CDS",
            protocol = "MCP/1.0",
            capabilities = new[] { "tools" },
            status = "healthy",
            authentication = "none",
            environment = "azure",
            endpoints = new
            {
                mcp = "POST /",
                health = "GET /health",
                manifest = "GET /manifest.json"
            }
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(serverInfo);
        return AddCorsHeaders(response);
    }

    private async Task<HttpResponseData> HandleMcpRequest(HttpRequestData req)
    {
        try
        {
            var requestBody = await req.ReadAsStringAsync();
            _logger.LogInformation("MCP Request body: {Body}", requestBody);

            if (string.IsNullOrEmpty(requestBody))
            {
                return CreateMcpErrorResponse(req, null, -32700, "Parse error");
            }

            var mcpRequest = JsonSerializer.Deserialize<McpRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mcpRequest == null)
            {
                return CreateMcpErrorResponse(req, null, -32700, "Parse error");
            }

            return await ProcessMcpMethod(req, mcpRequest);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error");
            return CreateMcpErrorResponse(req, null, -32700, "Parse error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MCP request");
            return CreateMcpErrorResponse(req, null, -32603, "Internal error");
        }
    }

    private async Task<HttpResponseData> ProcessMcpMethod(HttpRequestData req, McpRequest mcpRequest)
    {
        _logger.LogInformation("Processing MCP method: {Method}", mcpRequest.Method);

        return mcpRequest.Method switch
        {
            "initialize" => await HandleInitialize(req, mcpRequest.Id),
            "tools/list" => await HandleToolsList(req, mcpRequest.Id),
            "tools/call" => await HandleToolsCall(req, mcpRequest.Id, mcpRequest.Params),
            _ => CreateMcpErrorResponse(req, mcpRequest.Id, -32601, $"Unknown method: {mcpRequest.Method}")
        };
    }

    private async Task<HttpResponseData> HandleInitialize(HttpRequestData req, object? requestId)
    {
        var result = new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new McpCapabilities
            {
                Tools = new { }
            },
            ServerInfo = new McpServerInfo
            {
                Name = "Dataverse MCP Server - .NET Enterprise",
                Version = "1.0.0"
            },
            Instructions = "Enterprise .NET MCP server for Microsoft Dataverse legal matters and IP matters using SQL4CDS."
        };

        var mcpResponse = new McpResponse
        {
            Id = requestId,
            Result = result
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(mcpResponse);
        return AddCorsHeaders(response);
    }

    private async Task<HttpResponseData> HandleToolsList(HttpRequestData req, object? requestId)
    {
        var tools = new McpTool[]
        {
            new()
            {
                Name = "search",
                Description = "Search for legal matters and IP matters in Dataverse. Returns relevant search results from legalops_matters and legalops_mattersip tables. Excludes highly confidential matters automatically.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["query"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Search query string. Can include matter names, codes, keywords, or natural language queries."
                        }
                    },
                    Required = new[] { "query" }
                }
            },
            new()
            {
                Name = "fetch",
                Description = "Retrieve detailed information about a specific legal matter or IP matter record by its unique ID.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["id"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Unique record identifier obtained from search results"
                        }
                    },
                    Required = new[] { "id" }
                }
            }
        };

        var result = new McpToolsListResult { Tools = tools };
        var mcpResponse = new McpResponse
        {
            Id = requestId,
            Result = result
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(mcpResponse);
        return AddCorsHeaders(response);
    }

    private async Task<HttpResponseData> HandleToolsCall(HttpRequestData req, object? requestId, object? parameters)
    {
        try
        {
            if (parameters == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Invalid params");
            }

            var paramsJson = JsonSerializer.Serialize(parameters);
            var toolParams = JsonSerializer.Deserialize<McpToolCallParams>(paramsJson);

            if (toolParams == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Invalid params");
            }

            _logger.LogInformation("Tool call: {ToolName} with args: {Args}", toolParams.Name, 
                JsonSerializer.Serialize(toolParams.Arguments));

            return toolParams.Name switch
            {
                "search" => await HandleSearchTool(req, requestId, toolParams.Arguments),
                "fetch" => await HandleFetchTool(req, requestId, toolParams.Arguments),
                _ => CreateMcpErrorResponse(req, requestId, -32601, $"Unknown tool: {toolParams.Name}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in tools/call");
            return CreateMcpErrorResponse(req, requestId, -32603, "Tool execution failed");
        }
    }

    private async Task<HttpResponseData> HandleSearchTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (!arguments.TryGetValue("query", out var queryObj) || queryObj is not string query)
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'query' parameter");
            }

            _logger.LogInformation("Executing search for: '{Query}'", query);

            var results = await _dataverseService.SearchMattersAsync(query, 10);
            var searchResult = new McpSearchResult { Results = results };
            
            var mcpResponse = new McpResponse
            {
                Id = requestId,
                Result = searchResult
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"Search failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseData> HandleFetchTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (!arguments.TryGetValue("id", out var idObj) || idObj is not string recordId)
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'id' parameter");
            }

            _logger.LogInformation("Fetching record: {RecordId}", recordId);

            // Determine table type from ID or default to matters
            var tableType = recordId.Contains("mattersip") ? "ip" : "matters";
            var result = await _dataverseService.FetchRecordAsync(recordId, tableType);

            var mcpResponse = new McpResponse
            {
                Id = requestId,
                Result = result
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"Fetch failed: {ex.Message}");
        }
    }

    // Diagnostic endpoints
    private async Task<HttpResponseData> HandleDataverseTest(HttpRequestData req)
    {
        try
        {
            // Test SQL4CDS connection
            var testResult = await _dataverseService.ExecuteSqlQueryAsync("SELECT TOP 1 * FROM metadata.entity WHERE logicalname = 'contact'");
            
            var result = new
            {
                test = "dataverse_connection",
                result = new
                {
                    success = !testResult.Contains("<error>"),
                    message = testResult.Contains("<error>") ? "Connection failed" : "Successfully connected to Dataverse",
                    details = testResult
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataverse test failed");
            var result = new
            {
                test = "dataverse_connection",
                result = new { success = false, error = ex.Message },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return AddCorsHeaders(response);
        }
    }

    private async Task<HttpResponseData> HandleTestSearch(HttpRequestData req, string query)
    {
        try
        {
            var results = await _dataverseService.SearchMattersAsync(query, 10);
            
            var result = new
            {
                test = "search_test",
                query,
                results_count = results.Length,
                results,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test search failed");
            var result = new
            {
                test = "search_test",
                query,
                error = ex.Message,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return AddCorsHeaders(response);
        }
    }

    // Helper methods
    private HttpResponseData CreateCorsResponse(HttpRequestData req, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        return AddCorsHeaders(response);
    }

    private HttpResponseData CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        response.WriteString(JsonSerializer.Serialize(new { error = message }));
        response.Headers.Add("Content-Type", "application/json");
        return AddCorsHeaders(response);
    }

    private HttpResponseData CreateMcpErrorResponse(HttpRequestData req, object? requestId, int code, string message)
    {
        var mcpResponse = new McpResponse
        {
            Id = requestId,
            Error = new McpError
            {
                Code = code,
                Message = message
            }
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString(JsonSerializer.Serialize(mcpResponse));
        response.Headers.Add("Content-Type", "application/json");
        return AddCorsHeaders(response);
    }

    private HttpResponseData AddCorsHeaders(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        response.Headers.Add("Access-Control-Max-Age", "3600");
        return response;
    }
}