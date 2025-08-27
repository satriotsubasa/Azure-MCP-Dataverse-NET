using MarkMpn.Sql4Cds.Engine;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DataverseMcp.FunctionApp.Models;

namespace DataverseMcp.FunctionApp.Services;

public class DataverseService
{
    private readonly Sql4CdsConnection _sql4CdsConnection;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DataverseService> _logger;
    private static readonly TimeSpan DefaultCachingDuration = TimeSpan.FromMinutes(2);

    public DataverseService(
        Sql4CdsConnection sql4CdsConnection,
        IMemoryCache cache,
        ILogger<DataverseService> logger)
    {
        _sql4CdsConnection = sql4CdsConnection;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Search for legal matters and IP matters using SQL4CDS approach
    /// Follows exact pattern from working example
    /// </summary>
    public async Task<SearchResultItem[]> SearchMattersAsync(string query, int limit = 10)
    {
        _logger.LogInformation("Searching for '{Query}' with limit {Limit}", query, limit);

        var results = new List<SearchResultItem>();

        try
        {
            // Search legal matters table - using SQL4CDS pattern from working example
            var mattersQuery = $"""
                SELECT TOP({limit}) 
                    legalops_matterid, 
                    legalops_name, 
                    legalops_code, 
                    legalops_description 
                FROM dbo.legalops_matters 
                WHERE (legalops_name LIKE '%{query}%' OR legalops_code LIKE '%{query}%') 
                    AND (legalops_highlyconfidential IS NULL OR legalops_highlyconfidential != 1)
                """;

            var mattersResults = await ExecuteSqlQueryAsync(mattersQuery);
            results.AddRange(TransformToSearchResults(mattersResults, "matters"));

            // Search IP matters table
            var ipQuery = $"""
                SELECT TOP({limit}) 
                    legalops_mattersipid, 
                    legalops_name, 
                    legalops_code, 
                    legalops_description 
                FROM dbo.legalops_mattersip 
                WHERE (legalops_name LIKE '%{query}%' OR legalops_code LIKE '%{query}%') 
                    AND (legalops_highlyconfidential IS NULL OR legalops_highlyconfidential != 1)
                """;

            var ipResults = await ExecuteSqlQueryAsync(ipQuery);
            results.AddRange(TransformToSearchResults(ipResults, "ip"));

            _logger.LogInformation("Found {Count} total results for query '{Query}'", results.Count, query);
            
            return results.Take(limit).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for matters with query '{Query}'", query);
            return Array.Empty<SearchResultItem>();
        }
    }

    /// <summary>
    /// Fetch detailed record by ID
    /// </summary>
    public async Task<SearchResultItem?> FetchRecordAsync(string recordId, string tableType)
    {
        _logger.LogInformation("Fetching record {RecordId} from {TableType} table", recordId, tableType);

        try
        {
            string query = tableType.ToLower() == "matters"
                ? $"SELECT * FROM dbo.legalops_matters WHERE legalops_matterid = '{{{recordId}}}'"
                : $"SELECT * FROM dbo.legalops_mattersip WHERE legalops_mattersipid = '{{{recordId}}}'";

            var results = await ExecuteSqlQueryAsync(query);
            var transformedResults = TransformToSearchResults(results, tableType);

            return transformedResults.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching record {RecordId} from {TableType}", recordId, tableType);
            return null;
        }
    }

    /// <summary>
    /// Execute SQL query using SQL4CDS - exact pattern from working example
    /// </summary>
    public async Task<string> ExecuteSqlQueryAsync(string sqlQuery)
    {
        if (!sqlQuery.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only SELECT statements are allowed.");
        }

        _logger.LogInformation("Executing SQL: {Query}", sqlQuery);

        try
        {
            using var cmd = _sql4CdsConnection.CreateCommand();
            cmd.CommandText = sqlQuery;
            
            var table = new List<Dictionary<string, object>>();
            var reader = await cmd.ExecuteReaderAsync();
            int rowCount = 1;
            
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (i == 0)
                        row["#"] = rowCount++;
                    row[reader.GetName(i) ?? $"column_{i + 1}"] = reader.GetValue(i) ?? "";
                }
                table.Add(row);
            }

            var result = JsonSerializer.Serialize(table, new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });

            _logger.LogInformation("SQL query returned {RowCount} rows", table.Count);
            
            return $"""
                <environment>
                    https://{_sql4CdsConnection.DataSource}
                </environment>
                <json_output>
                    {result}
                </json_output>
                """;
        }
        catch (Sql4CdsException ex)
        {
            _logger.LogError(ex, "SQL4CDS error executing query: {Query}", sqlQuery);
            return $"""
                <error>
                    {ex.Message}
                </error>
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SQL query: {Query}", sqlQuery);
            return $"""
                <error>
                    {ex.Message}
                </error>
                """;
        }
    }

    /// <summary>
    /// Get table metadata - from working example
    /// </summary>
    public async Task<string> GetTableMetadataAsync(string? tableName = null, string[]? fieldNames = null)
    {
        var cacheKey = $"GetTableMetadata_{tableName}_{string.Join(",", fieldNames ?? Array.Empty<string>())}";
        
        if (_cache.TryGetValue(cacheKey, out string? cachedResult))
        {
            return cachedResult!;
        }

        var fields = fieldNames?.Length > 0 
            ? string.Join(",", fieldNames) 
            : "*";
            
        var query = $"SELECT {fields} FROM metadata.entity";
        
        if (!string.IsNullOrEmpty(tableName))
        {
            query += $" WHERE logicalname = '{tableName}'";
        }

        var result = await ExecuteSqlQueryAsync(query);
        _cache.Set(cacheKey, result, DefaultCachingDuration);
        
        return result;
    }

    /// <summary>
    /// Transform SQL4CDS results to MCP search format
    /// </summary>
    private SearchResultItem[] TransformToSearchResults(string sqlResult, string tableType)
    {
        try
        {
            // Extract JSON from the SQL4CDS response format
            var startIndex = sqlResult.IndexOf("<json_output>") + "<json_output>".Length;
            var endIndex = sqlResult.IndexOf("</json_output>");
            
            if (startIndex < 0 || endIndex < 0)
                return Array.Empty<SearchResultItem>();

            var jsonContent = sqlResult.Substring(startIndex, endIndex - startIndex).Trim();
            var records = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonContent);

            if (records == null)
                return Array.Empty<SearchResultItem>();

            return records.Select(record => new SearchResultItem
            {
                Id = ExtractRecordId(record, tableType),
                Title = GetStringValue(record, "legalops_name") ?? "Untitled Matter",
                Text = GetStringValue(record, "legalops_description") ?? GetStringValue(record, "legalops_code") ?? "No description available",
                Url = "https://fa-auae-dsdev-lgca-dig04.azurewebsites.net/api/HttpTrigger", // Will be updated when deployed
                Metadata = new Dictionary<string, object>
                {
                    ["table_type"] = tableType,
                    ["code"] = GetStringValue(record, "legalops_code") ?? "",
                    ["name"] = GetStringValue(record, "legalops_name") ?? "",
                    ["confidentiality"] = "standard",
                    ["full_record"] = record
                }
            }).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transforming SQL results to search format");
            return Array.Empty<SearchResultItem>();
        }
    }

    private string ExtractRecordId(Dictionary<string, object> record, string tableType)
    {
        var idField = tableType == "matters" ? "legalops_matterid" : "legalops_mattersipid";
        var id = GetStringValue(record, idField) ?? "";
        
        // Handle GUID format - remove curly braces if present
        if (id.StartsWith("{") && id.EndsWith("}"))
        {
            id = id.Substring(1, id.Length - 2);
        }
        
        return id;
    }

    private string? GetStringValue(Dictionary<string, object> record, string key)
    {
        if (record.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }
}