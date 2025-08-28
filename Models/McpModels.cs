using System.Text.Json.Serialization;

namespace DataverseMcp.WebApi.Models;

// MCP Protocol Models for OpenAI compliance
public record McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
    
    [JsonPropertyName("id")]
    public object? Id { get; init; }
    
    [JsonPropertyName("method")]
    public string Method { get; init; } = string.Empty;
    
    [JsonPropertyName("params")]
    public object? Params { get; init; }
}

public record McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
    
    [JsonPropertyName("id")]
    public object? Id { get; init; }
    
    [JsonPropertyName("result")]
    public object? Result { get; init; }
    
    [JsonPropertyName("error")]
    public McpError? Error { get; init; }
}

public record McpError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }
    
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
    
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}

public record McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = "2024-11-05";
    
    [JsonPropertyName("capabilities")]
    public McpCapabilities Capabilities { get; init; } = new();
    
    [JsonPropertyName("serverInfo")]
    public McpServerInfo ServerInfo { get; init; } = new();
    
    [JsonPropertyName("instructions")]
    public string Instructions { get; init; } = string.Empty;
}

public record McpCapabilities
{
    [JsonPropertyName("tools")]
    public object Tools { get; init; } = new { };
}

public record McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

public record McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
    
    [JsonPropertyName("inputSchema")]
    public McpInputSchema InputSchema { get; init; } = new();
}

public record McpInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";
    
    [JsonPropertyName("properties")]
    public Dictionary<string, McpProperty> Properties { get; init; } = new();
    
    [JsonPropertyName("required")]
    public string[] Required { get; init; } = Array.Empty<string>();
}

public record McpProperty
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

public record McpToolsListResult
{
    [JsonPropertyName("tools")]
    public McpTool[] Tools { get; init; } = Array.Empty<McpTool>();
}

public record McpToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("arguments")]
    public Dictionary<string, object> Arguments { get; init; } = new();
}

public record McpSearchResult
{
    [JsonPropertyName("results")]
    public SearchResultItem[] Results { get; init; } = Array.Empty<SearchResultItem>();
}

public record SearchResultItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
    
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
    
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
    
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = new();
}