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
    private readonly DataverseService? _dataverseService;

    public HttpTriggerFunction(
        ILogger<HttpTriggerFunction> logger,
        DataverseService? dataverseService = null)
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
            description = "Enterprise .NET MCP server for Microsoft Dataverse legal matters and IP matters using SQL4CDS. Supports comprehensive metadata queries, SQL execution, FetchXML conversion, and intelligent search with field-specific syntax mapping for enhanced ChatGPT integration.",
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
            },
            systemInstructions = "You will be asked questions pertaining to Dataverse. The main objective is to retrieve data on transactional tables using SQL. Always try to retrieve records which are in active state. Discover table metadata and validate using GetFieldMetadataByTableName. Use SQL for querying with schema names (dbo or metadata). When ordering results, use highest to lowest for aggregated queries, and most recent to oldest by modifiedon for other queries. Always use lowercase for entity/table names and field names. If field is a Picklist/Optionset/Choice or EntityReference/Lookup, use the logical virtual field instead for better readability."
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
        // Handle protocol version dynamically - respond with client's version or default
        var protocolVersion = "2024-11-05"; // Default version
        try
        {
            var requestBody = await req.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(requestBody))
            {
                var initRequest = JsonSerializer.Deserialize<JsonDocument>(requestBody);
                if (initRequest?.RootElement.TryGetProperty("params", out var paramsElement) == true &&
                    paramsElement.TryGetProperty("protocolVersion", out var versionElement))
                {
                    var clientVersion = versionElement.GetString();
                    if (!string.IsNullOrEmpty(clientVersion))
                    {
                        protocolVersion = clientVersion;
                    }
                }
            }
        }
        catch
        {
            // Fall back to default version if parsing fails
        }

        var result = new McpInitializeResult
        {
            ProtocolVersion = protocolVersion,
            Capabilities = new McpCapabilities
            {
                Tools = new { }
            },
            ServerInfo = new McpServerInfo
            {
                Name = "Dataverse MCP Server - .NET Enterprise",
                Version = "1.0.0"
            },
            Instructions = "Enterprise .NET MCP server for Microsoft Dataverse legal matters and IP matters using SQL4CDS. Supports comprehensive metadata queries, SQL execution, FetchXML conversion, and intelligent search with field-specific syntax mapping for enhanced ChatGPT integration. Always try to retrieve records which are in active state. Use SQL for querying with schema names (dbo or metadata). When ordering results, use highest to lowest for aggregated queries, and most recent to oldest by modifiedon for other queries. Always use lowercase for entity/table names and field names."
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
                Description = "Search for legal matters and IP matters in Dataverse. Returns relevant search results from legalops_matters table. Supports field-specific syntax like 'title:term', 'name:term', 'code:term', 'description:term'. Excludes highly confidential matters automatically.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["query"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Search query string. Can include matter names, codes, keywords, field-specific syntax (title:term, name:term, code:term), or natural language queries."
                        }
                    },
                    Required = new[] { "query" }
                }
            },
            new()
            {
                Name = "fetch",
                Description = "Retrieve detailed information about a specific legal matter record by its unique ID.",
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
            },
            new()
            {
                Name = "ExecuteSQL",
                Description = "Execute SQL query against Dataverse using SQL4CDS engine. Only SELECT statements are allowed for security.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["sqlQuery"] = new McpProperty
                        {
                            Type = "string",
                            Description = "SQL SELECT query to execute. Must use schema names (dbo or metadata). Use lowercase for table/field names."
                        }
                    },
                    Required = new[] { "sqlQuery" }
                }
            },
            new()
            {
                Name = "GetMetadataForAllTables",
                Description = "Get metadata for all tables in Dataverse. Useful for discovering available tables and their properties.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["metadataFieldNames"] = new McpProperty
                        {
                            Type = "array",
                            Description = "Array of metadata field names to retrieve (e.g. ['logicalname', 'displayname', 'description']). Empty array returns all fields.",
                            Items = new { type = "string" }
                        },
                        ["conditions"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Optional condition to filter tables (e.g. 'isactivity = 1 AND islogicalentity = 1')"
                        }
                    },
                    Required = new[] { "metadataFieldNames" }
                }
            },
            new()
            {
                Name = "GetMetadataByTableName",
                Description = "Get metadata for a specific table by its logical name.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["tableName"] = new McpProperty
                        {
                            Type = "string",
                            Description = "The table's logical name (e.g. 'contact', 'account', 'legalops_matters')"
                        },
                        ["metadataFieldNames"] = new McpProperty
                        {
                            Type = "array",
                            Description = "Array of metadata field names to retrieve. Empty array returns all fields.",
                            Items = new { type = "string" }
                        }
                    },
                    Required = new[] { "tableName", "metadataFieldNames" }
                }
            },
            new()
            {
                Name = "GetFieldMetadataByTableName",
                Description = "Get metadata for fields in a specific table. Essential for understanding field structure before querying.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["tableName"] = new McpProperty
                        {
                            Type = "string",
                            Description = "The table's logical name (e.g. 'contact', 'account', 'legalops_matters')"
                        },
                        ["metadataFieldNames"] = new McpProperty
                        {
                            Type = "array",
                            Description = "Array of metadata field names to retrieve (e.g. ['logicalname', 'displayname', 'attributetype']). Empty array returns all fields.",
                            Items = new { type = "string" }
                        },
                        ["conditions"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Optional condition to filter fields (e.g. 'isfilterable = 1 AND isvalidforupdate = 1')"
                        }
                    },
                    Required = new[] { "tableName", "metadataFieldNames" }
                }
            },
            new()
            {
                Name = "GetRowsForTable",
                Description = "Retrieve rows from a specific table with optional filtering and sorting.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["tableName"] = new McpProperty
                        {
                            Type = "string",
                            Description = "The table's logical name (e.g. 'contact', 'account', 'legalops_matters')"
                        },
                        ["fieldNames"] = new McpProperty
                        {
                            Type = "array",
                            Description = "Array of field names to retrieve. Empty array returns all fields.",
                            Items = new { type = "string" }
                        },
                        ["conditions"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Optional WHERE clause condition to filter rows"
                        },
                        ["sortOrder"] = new McpProperty
                        {
                            Type = "string",
                            Description = "Optional ORDER BY clause (e.g. 'fullname DESC', 'createdon DESC')"
                        },
                        ["rowCount"] = new McpProperty
                        {
                            Type = "integer",
                            Description = "Number of rows to retrieve (default: 50)"
                        }
                    },
                    Required = new[] { "tableName", "fieldNames" }
                }
            },
            new()
            {
                Name = "ConvertFetchXmlToSql",
                Description = "Convert FetchXML query to SQL query using SQL4CDS engine.",
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, McpProperty>
                    {
                        ["fetchXml"] = new McpProperty
                        {
                            Type = "string",
                            Description = "FetchXML query string to convert to SQL"
                        }
                    },
                    Required = new[] { "fetchXml" }
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
                "ExecuteSQL" => await HandleExecuteSQLTool(req, requestId, toolParams.Arguments),
                "GetMetadataForAllTables" => await HandleGetMetadataForAllTablesTool(req, requestId, toolParams.Arguments),
                "GetMetadataByTableName" => await HandleGetMetadataByTableNameTool(req, requestId, toolParams.Arguments),
                "GetFieldMetadataByTableName" => await HandleGetFieldMetadataByTableNameTool(req, requestId, toolParams.Arguments),
                "GetRowsForTable" => await HandleGetRowsForTableTool(req, requestId, toolParams.Arguments),
                "ConvertFetchXmlToSql" => await HandleConvertFetchXmlToSqlTool(req, requestId, toolParams.Arguments),
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
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available - check connection configuration");
            }

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
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available - check connection configuration");
            }

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

    // Comprehensive tool handlers
    private async Task<HttpResponseData> HandleExecuteSQLTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available");
            }

            var sqlQuery = ExtractStringParameter(arguments, "sqlQuery");
            if (string.IsNullOrEmpty(sqlQuery))
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'sqlQuery' parameter");
            }

            var result = await _dataverseService.ExecuteSqlQueryAsync(sqlQuery);
            var mcpResponse = new McpResponse { Id = requestId, Result = new { query = sqlQuery, result } };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteSQL tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"SQL execution failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseData> HandleGetMetadataForAllTablesTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available");
            }

            var metadataFieldNames = ExtractStringArrayParameter(arguments, "metadataFieldNames");
            var conditions = ExtractStringParameter(arguments, "conditions");

            var result = await _dataverseService.GetMetadataForAllTablesAsync(metadataFieldNames, conditions);
            var mcpResponse = new McpResponse { Id = requestId, Result = new { result } };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetMetadataForAllTables tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"Metadata retrieval failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseData> HandleGetMetadataByTableNameTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available");
            }

            var tableName = ExtractStringParameter(arguments, "tableName");
            if (string.IsNullOrEmpty(tableName))
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'tableName' parameter");
            }

            var metadataFieldNames = ExtractStringArrayParameter(arguments, "metadataFieldNames");
            var result = await _dataverseService.GetMetadataByTableNameAsync(tableName, metadataFieldNames);
            var mcpResponse = new McpResponse { Id = requestId, Result = new { tableName, result } };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetMetadataByTableName tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"Table metadata retrieval failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseData> HandleGetFieldMetadataByTableNameTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available");
            }

            var tableName = ExtractStringParameter(arguments, "tableName");
            if (string.IsNullOrEmpty(tableName))
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'tableName' parameter");
            }

            var metadataFieldNames = ExtractStringArrayParameter(arguments, "metadataFieldNames");
            var conditions = ExtractStringParameter(arguments, "conditions");

            var result = await _dataverseService.GetFieldMetadataByTableNameAsync(tableName, metadataFieldNames, conditions);
            var mcpResponse = new McpResponse { Id = requestId, Result = new { tableName, result } };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetFieldMetadataByTableName tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"Field metadata retrieval failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseData> HandleGetRowsForTableTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available");
            }

            var tableName = ExtractStringParameter(arguments, "tableName");
            if (string.IsNullOrEmpty(tableName))
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'tableName' parameter");
            }

            var fieldNames = ExtractStringArrayParameter(arguments, "fieldNames");
            var conditions = ExtractStringParameter(arguments, "conditions");
            var sortOrder = ExtractStringParameter(arguments, "sortOrder");
            var rowCount = ExtractIntParameter(arguments, "rowCount") ?? 50;

            var result = await _dataverseService.GetRowsForTableAsync(tableName, fieldNames, conditions, sortOrder, rowCount);
            var mcpResponse = new McpResponse { Id = requestId, Result = new { tableName, result } };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRowsForTable tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"Table rows retrieval failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseData> HandleConvertFetchXmlToSqlTool(HttpRequestData req, object? requestId, Dictionary<string, object> arguments)
    {
        try
        {
            if (_dataverseService == null)
            {
                return CreateMcpErrorResponse(req, requestId, -32603, "DataverseService not available");
            }

            var fetchXml = ExtractStringParameter(arguments, "fetchXml");
            if (string.IsNullOrEmpty(fetchXml))
            {
                return CreateMcpErrorResponse(req, requestId, -32602, "Missing or invalid 'fetchXml' parameter");
            }

            var result = await _dataverseService.ConvertFetchXmlToSqlAsync(fetchXml);
            var mcpResponse = new McpResponse { Id = requestId, Result = new { fetchXml, result } };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mcpResponse);
            return AddCorsHeaders(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConvertFetchXmlToSql tool failed");
            return CreateMcpErrorResponse(req, requestId, -32603, $"FetchXML conversion failed: {ex.Message}");
        }
    }

    // Parameter extraction helpers for ChatGPT JsonElement compatibility
    private string? ExtractStringParameter(Dictionary<string, object> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            string str => str,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value?.ToString()
        };
    }

    private string[] ExtractStringArrayParameter(Dictionary<string, object> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value))
            return Array.Empty<string>();

        return value switch
        {
            string[] array => array,
            JsonElement element when element.ValueKind == JsonValueKind.Array => 
                element.EnumerateArray().Select(e => e.GetString() ?? "").ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private int? ExtractIntParameter(Dictionary<string, object> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            int intValue => intValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt32(),
            string str when int.TryParse(str, out var parsed) => parsed,
            _ => null
        };
    }

    // Diagnostic endpoints
    private async Task<HttpResponseData> HandleDataverseTest(HttpRequestData req)
    {
        try
        {
            if (_dataverseService == null)
            {
                var result = new
                {
                    test = "dataverse_connection",
                    result = new { success = false, error = "DataverseService not available - check connection string and dependencies" },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(result);
                return AddCorsHeaders(response);
            }

            // Test SQL4CDS connection
            var testResult = await _dataverseService.ExecuteSqlQueryAsync("SELECT TOP 1 * FROM metadata.entity WHERE logicalname = 'contact'");
            
            var successResult = new
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

            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            await successResponse.WriteAsJsonAsync(successResult);
            return AddCorsHeaders(successResponse);
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
            if (_dataverseService == null)
            {
                var result = new
                {
                    test = "search_test",
                    query,
                    error = "DataverseService not available",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(result);
                return AddCorsHeaders(response);
            }

            var results = await _dataverseService.SearchMattersAsync(query, 10);
            
            var successResult = new
            {
                test = "search_test",
                query,
                results_count = results.Length,
                results,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            await successResponse.WriteAsJsonAsync(successResult);
            return AddCorsHeaders(successResponse);
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