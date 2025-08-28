using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using DataverseMcp.WebApi.Models;
using DataverseMcp.WebApi.Services;

namespace DataverseMcp.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class McpController : ControllerBase
{
    private readonly ILogger<McpController> _logger;
    private readonly DataverseService? _dataverseService;

    public McpController(ILogger<McpController> logger, DataverseService? dataverseService = null)
    {
        _logger = logger;
        _dataverseService = dataverseService;
    }

    [HttpOptions]
    public IActionResult HandleOptions()
    {
        return Ok();
    }

    [HttpGet]
    public IActionResult GetServerInfo()
    {
        var serverInfo = new
        {
            name = "Dataverse MCP Server - .NET Web API",
            version = "1.0.0",
            description = "MCP server for ChatGPT integration with Microsoft Dataverse using .NET and SQL4CDS",
            protocol = "MCP/1.0",
            capabilities = new[] { "tools" },
            status = "healthy",
            authentication = "none",
            environment = "render",
            endpoints = new
            {
                mcp = "POST /api/mcp",
                health = "GET /api/health",
                diagnostic = "GET /api/diagnostic"
            }
        };

        return Ok(serverInfo);
    }

    [HttpPost]
    public async Task<IActionResult> HandleMcpRequest([FromBody] JsonElement body)
    {
        _logger.LogInformation("MCP Request received: {Method}", Request.Method);

        try
        {
            var requestBody = body.GetRawText();
            _logger.LogInformation("MCP Request body: {Body}", requestBody);

            if (string.IsNullOrEmpty(requestBody))
            {
                return Ok(CreateMcpErrorResponse(null, -32700, "Parse error"));
            }

            var mcpRequest = JsonSerializer.Deserialize<McpRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mcpRequest == null)
            {
                return Ok(CreateMcpErrorResponse(null, -32700, "Parse error"));
            }

            return await ProcessMcpMethod(mcpRequest);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error");
            return Ok(CreateMcpErrorResponse(null, -32700, "Parse error"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MCP request");
            return Ok(CreateMcpErrorResponse(null, -32603, "Internal error"));
        }
    }

    private async Task<IActionResult> ProcessMcpMethod(McpRequest mcpRequest)
    {
        _logger.LogInformation("Processing MCP method: {Method}", mcpRequest.Method);

        return mcpRequest.Method switch
        {
            "initialize" => Ok(await HandleInitialize(mcpRequest.Id)),
            "tools/list" => Ok(await HandleToolsList(mcpRequest.Id)),
            "tools/call" => Ok(await HandleToolsCall(mcpRequest.Id, mcpRequest.Params)),
            _ => Ok(CreateMcpErrorResponse(mcpRequest.Id, -32601, $"Unknown method: {mcpRequest.Method}"))
        };
    }

    private async Task<McpResponse> HandleInitialize(object? requestId)
    {
        await Task.Delay(1); // Make async

        var result = new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new McpCapabilities
            {
                Tools = new { }
            },
            ServerInfo = new McpServerInfo
            {
                Name = "Dataverse MCP Server - .NET Web API",
                Version = "1.0.0"
            },
            Instructions = "Enterprise .NET Web API MCP server for Microsoft Dataverse legal matters and IP matters using SQL4CDS."
        };

        return new McpResponse
        {
            Id = requestId,
            Result = result
        };
    }

    private async Task<McpResponse> HandleToolsList(object? requestId)
    {
        await Task.Delay(1); // Make async

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
        return new McpResponse
        {
            Id = requestId,
            Result = result
        };
    }

    private async Task<McpResponse> HandleToolsCall(object? requestId, object? parameters)
    {
        try
        {
            if (parameters == null)
            {
                return CreateMcpErrorResponse(requestId, -32602, "Invalid params");
            }

            var paramsJson = JsonSerializer.Serialize(parameters);
            var toolParams = JsonSerializer.Deserialize<McpToolCallParams>(paramsJson);

            if (toolParams == null)
            {
                return CreateMcpErrorResponse(requestId, -32602, "Invalid params");
            }

            _logger.LogInformation("Tool call: {ToolName} with args: {Args}", toolParams.Name, 
                JsonSerializer.Serialize(toolParams.Arguments));

            return toolParams.Name switch
            {
                "search" => await HandleSearchTool(requestId, toolParams.Arguments),
                "fetch" => await HandleFetchTool(requestId, toolParams.Arguments),
                _ => CreateMcpErrorResponse(requestId, -32601, $"Unknown tool: {toolParams.Name}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in tools/call");
            return CreateMcpErrorResponse(requestId, -32603, "Tool execution failed");
        }
    }

    private async Task<McpResponse> HandleSearchTool(object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(requestId, -32603, "DataverseService not available - check connection configuration");
            }

            if (!arguments.TryGetValue("query", out var queryObj) || queryObj is not string query)
            {
                return CreateMcpErrorResponse(requestId, -32602, "Missing or invalid 'query' parameter");
            }

            _logger.LogInformation("Executing search for: '{Query}'", query);

            var results = await _dataverseService.SearchMattersAsync(query, 10);
            var searchResult = new McpSearchResult { Results = results };
            
            return new McpResponse
            {
                Id = requestId,
                Result = searchResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search tool failed");
            return CreateMcpErrorResponse(requestId, -32603, $"Search failed: {ex.Message}");
        }
    }

    private async Task<McpResponse> HandleFetchTool(object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(requestId, -32603, "DataverseService not available - check connection configuration");
            }

            if (!arguments.TryGetValue("id", out var idObj) || idObj is not string recordId)
            {
                return CreateMcpErrorResponse(requestId, -32602, "Missing or invalid 'id' parameter");
            }

            _logger.LogInformation("Fetching record: {RecordId}", recordId);

            // Determine table type from ID or default to matters
            var tableType = recordId.Contains("mattersip") ? "ip" : "matters";
            var result = await _dataverseService.FetchRecordAsync(recordId, tableType);

            return new McpResponse
            {
                Id = requestId,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch tool failed");
            return CreateMcpErrorResponse(requestId, -32603, $"Fetch failed: {ex.Message}");
        }
    }

    private static McpResponse CreateMcpErrorResponse(object? requestId, int code, string message)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError
            {
                Code = code,
                Message = message
            }
        };
    }
}