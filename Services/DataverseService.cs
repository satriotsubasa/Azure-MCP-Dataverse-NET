using MarkMpn.Sql4Cds.Engine;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DataverseMcp.WebApi.Models;

namespace DataverseMcp.WebApi.Services;

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
    /// Supports field-specific search syntax mapping
    /// </summary>
    public async Task<SearchResultItem[]> SearchMattersAsync(string query, int limit = 10)
    {
        var results = new List<SearchResultItem>();

        try
        {
            // Parse and map field-specific search syntax
            var (searchTerm, fieldMappings) = ParseSearchQuery(query);

            // Build WHERE clause based on field mappings or default search
            string whereClause;
            if (fieldMappings.Any())
            {
                var conditions = new List<string>();
                foreach (var mapping in fieldMappings)
                {
                    conditions.Add($"{mapping.Value} LIKE '%{mapping.Key}%'");
                }
                whereClause = $"({string.Join(" OR ", conditions)}) AND (legalops_highlyconfidential IS NULL OR legalops_highlyconfidential != 1)";
            }
            else
            {
                // Default search in both name and code fields
                whereClause = $"(legalops_name LIKE '%{searchTerm}%' OR legalops_code LIKE '%{searchTerm}%') AND (legalops_highlyconfidential IS NULL OR legalops_highlyconfidential != 1)";
            }

            // Search legal matters table - using correct column names from schema
            var mattersQuery = $"""
                SELECT TOP({limit}) 
                    legalops_mattersid, 
                    legalops_name, 
                    legalops_code, 
                    legalops_descriptionbasic
                FROM dbo.legalops_matters 
                WHERE {whereClause}
                """;

            var mattersResults = await ExecuteSqlQueryAsync(mattersQuery);
            results.AddRange(TransformToSearchResults(mattersResults, "matters"));
            
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
        // Fetching record by ID

        try
        {
            // Only support legalops_matters table with correct primary key column name
            string query = $"SELECT * FROM dbo.legalops_matters WHERE legalops_mattersid = '{{{recordId}}}'";

            var results = await ExecuteSqlQueryAsync(query);
            var transformedResults = TransformToSearchResults(results, "matters");

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

        _logger.LogInformation("SQL: {Query}", sqlQuery.Length > 100 ? sqlQuery.Substring(0, 100) + "..." : sqlQuery);

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

            _logger.LogInformation("Returned {RowCount} rows", table.Count);
            
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
    /// Get metadata for all tables in Dataverse - following reference implementation
    /// </summary>
    public async Task<string> GetMetadataForAllTablesAsync(string[] metadataFieldNames, string? conditions = null)
    {
        var cacheKey = $"GetMetadataForAllTables_{string.Join(",", metadataFieldNames)}_{conditions}";
        if (_cache.TryGetValue(cacheKey, out string? cachedResult))
        {
            return cachedResult!;
        }

        var query = metadataFieldNames.Length > 0 ? $"SELECT {string.Join(",", metadataFieldNames)} FROM metadata.entity" : $"SELECT * FROM metadata.entity";
        if (!string.IsNullOrEmpty(conditions))
        {
            query += $" WHERE ({conditions.ToLower()})";
        }
        var result = await ExecuteSqlQueryAsync(query);
        _cache.Set(cacheKey, result, DefaultCachingDuration);
        return result;
    }

    /// <summary>
    /// Get metadata for a specific table - following reference implementation
    /// </summary>
    public async Task<string> GetMetadataByTableNameAsync(string tableName, string[] metadataFieldNames)
    {
        var cacheKey = $"GetMetadataByTableName_{tableName}_{string.Join(",", metadataFieldNames)}";
        if (_cache.TryGetValue(cacheKey, out string? cachedResult))
        {
            return cachedResult!;
        }

        var query = metadataFieldNames.Length > 0 ? $"SELECT {string.Join(",", metadataFieldNames)} FROM metadata.entity" : $"SELECT * FROM metadata.entity";
        var result = await ExecuteSqlQueryAsync($"{query} WHERE logicalname = '{tableName}'");
        _cache.Set(cacheKey, result, DefaultCachingDuration);
        return result;
    }

    /// <summary>
    /// Get metadata for fields in a specific table - following reference implementation
    /// </summary>
    public async Task<string> GetFieldMetadataByTableNameAsync(string tableName, string[] metadataFieldNames, string? conditions = null)
    {
        var cacheKey = $"GetFieldMetadataByTableName_{tableName}_{string.Join(",", metadataFieldNames)}_{conditions}";
        if (_cache.TryGetValue(cacheKey, out string? cachedResult))
        {
            return cachedResult!;
        }

        var query = metadataFieldNames.Length > 0 ? $"SELECT {string.Join(",", metadataFieldNames)} FROM metadata.attribute" : $"SELECT * FROM metadata.attribute";
        query += $" WHERE entitylogicalname = '{tableName}'";
        if (!string.IsNullOrEmpty(conditions))
        {
            query += $" AND ({conditions.ToLower()})";
        }
        var result = await ExecuteSqlQueryAsync(query);
        _cache.Set(cacheKey, result, DefaultCachingDuration);
        return result;
    }

    /// <summary>
    /// Retrieve rows for a specific table - following reference implementation
    /// </summary>
    public async Task<string> GetRowsForTableAsync(string tableName, string[] fieldNames, string? conditions = null, string? sortOrder = null, int? rowCount = 50)
    {
        var query = fieldNames.Length > 0 ? $"SELECT TOP({rowCount}) {string.Join(",", fieldNames)} FROM dbo.{tableName}" : $"SELECT TOP({rowCount}) * FROM dbo.{tableName}";
        if (!string.IsNullOrEmpty(conditions))
        {
            query += $" WHERE ({conditions})";
        }
        if (!string.IsNullOrEmpty(sortOrder))
        {
            query += $" ORDER BY {sortOrder}";
        }
        var result = await ExecuteSqlQueryAsync(query);
        return result;
    }

    /// <summary>
    /// Convert FetchXml query to SQL query - following reference implementation
    /// </summary>
    public async Task<string> ConvertFetchXmlToSqlAsync(string fetchXml)
    {
        var result = await ExecuteSqlQueryAsync($"SELECT Response FROM FetchXMLToSQL('{fetchXml}',0)");
        return result;
    }

    /// <summary>
    /// Parse search query and map field-specific syntax to Dataverse column names
    /// Supports syntax like: title:term, name:term, code:term, description:term
    /// </summary>
    private (string searchTerm, Dictionary<string, string> fieldMappings) ParseSearchQuery(string query)
    {
        var fieldMappings = new Dictionary<string, string>();
        var searchTerm = query;

        // Define field mappings from search syntax to actual column names
        var syntaxMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Title/Name mappings - all map to legalops_name
            ["title"] = "legalops_name",
            ["name"] = "legalops_name", 
            ["matter"] = "legalops_name",
            ["subject"] = "legalops_name",
            
            // Code mappings
            ["code"] = "legalops_code",
            ["matter_code"] = "legalops_code",
            ["id"] = "legalops_code",
            
            // Description mappings
            ["description"] = "legalops_descriptionbasic",
            ["desc"] = "legalops_descriptionbasic",
            ["summary"] = "legalops_descriptionbasic",
            ["details"] = "legalops_descriptionbasic"
        };

        // Check for field-specific search patterns: field:term or "field:term"
        var fieldSearchPatterns = new[]
        {
            @"(\w+):([""']?)([^""'\s]+)\2", // Matches field:term, field:"term", field:'term'
            @"""(\w+):([^""]+)""",         // Matches "field:term with spaces"
            @"'(\w+):([^']+)'"             // Matches 'field:term with spaces'
        };

        foreach (var pattern in fieldSearchPatterns)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(query, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string fieldName, fieldValue;
                
                if (match.Groups.Count == 4) // field:term pattern
                {
                    fieldName = match.Groups[1].Value;
                    fieldValue = match.Groups[3].Value;
                }
                else if (match.Groups.Count == 3) // quoted patterns
                {
                    fieldName = match.Groups[1].Value;
                    fieldValue = match.Groups[2].Value;
                }
                else
                {
                    continue;
                }

                // Map the field name to the actual column name
                if (syntaxMappings.TryGetValue(fieldName, out var columnName))
                {
                    fieldMappings[fieldValue] = columnName;
                    
                    // Remove the field:term pattern from the search term for cleaner logging
                    searchTerm = query.Replace(match.Value, "").Trim();
                }
            }
        }

        // Handle special cases for natural language queries
        if (!fieldMappings.Any())
        {
            // Check for natural language patterns like "matter name contains X"
            var naturalLanguagePatterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["matter name contains"] = "legalops_name",
                ["title contains"] = "legalops_name",
                ["name contains"] = "legalops_name",
                ["code contains"] = "legalops_code",
                ["description contains"] = "legalops_descriptionbasic"
            };

            foreach (var pattern in naturalLanguagePatterns)
            {
                if (query.Contains(pattern.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var term = query.Replace(pattern.Key, "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(term))
                    {
                        fieldMappings[term] = pattern.Value;
                        searchTerm = term;
                        break;
                    }
                }
            }
        }

        // Clean up quoted search terms
        searchTerm = searchTerm.Trim('"', '\'').Trim();
        
        return (searchTerm, fieldMappings);
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
                Text = GetStringValue(record, "legalops_descriptionbasic") ?? GetStringValue(record, "legalops_code") ?? "No description available",
                Url = "https://azure-mcp-dataverse-net.onrender.com/api/mcp", // Render.com deployment
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
        // Use correct primary key field name from schema
        var idField = "legalops_mattersid";
        
        if (record.TryGetValue(idField, out var value))
        {
            // If it's a complex object (JsonElement), try to extract the "id" property
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
                if (jsonElement.TryGetProperty("id", out var idProperty))
                {
                    var id = idProperty.GetString() ?? "";
                    // Handle GUID format - remove curly braces if present
                    if (id.StartsWith("{") && id.EndsWith("}"))
                    {
                        id = id.Substring(1, id.Length - 2);
                    }
                    return id;
                }
            }
            
            // Fallback to string conversion
            var fallbackId = value?.ToString() ?? "";
            if (fallbackId.StartsWith("{") && fallbackId.EndsWith("}"))
            {
                fallbackId = fallbackId.Substring(1, fallbackId.Length - 2);
            }
            return fallbackId;
        }
        
        return "";
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