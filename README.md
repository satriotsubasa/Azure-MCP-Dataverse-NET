# Dataverse MCP Server - .NET Enterprise

A .NET Azure Function App implementation of the Model Context Protocol (MCP) server for Microsoft Dataverse integration with ChatGPT Enterprise.

## Features

- **OpenAI MCP Compliant**: Fully compliant with OpenAI MCP specification for ChatGPT Enterprise connector
- **SQL4CDS Integration**: Uses the same SQL4CDS engine as the working desktop example
- **Direct ServiceClient**: Leverages Microsoft's official Dataverse ServiceClient for authentication
- **Legal Matter Search**: Searches both `legalops_matters` and `legalops_mattersip` tables
- **Confidentiality Filter**: Automatically excludes highly confidential matters
- **Real-time Caching**: Memory caching for metadata and frequently accessed data

## Architecture

This implementation follows the working example from `Dataverse MCP references/mcp-dataverse` but adapts it for:

1. **Azure Functions HTTP trigger** instead of CLI stdio transport
2. **OpenAI MCP protocol** instead of Claude Desktop MCP protocol  
3. **ChatGPT Enterprise connector** integration
4. **REST API endpoints** for health checks and diagnostics

## Key Components

### DataverseService
- Uses `Sql4CdsConnection` exactly like the working example
- Executes SQL queries: `SELECT * FROM dbo.legalops_matters WHERE legalops_name LIKE '%Project Aqua%'`
- Transforms results to MCP search format
- Handles authentication via ServiceClient connection string

### HttpTriggerFunction  
- Implements complete MCP protocol handlers:
  - `initialize` - Server capabilities and info
  - `tools/list` - Available search and fetch tools
  - `tools/call` - Execute search and fetch operations
- CORS support for ChatGPT Enterprise
- Health check and diagnostic endpoints

## Deployment

1. **Create Azure Function App**:
   ```bash
   # Create with .NET 8 runtime
   az functionapp create --resource-group rg-auae-dsdev-lgca-dig04 \
     --consumption-plan-location "Australia East" \
     --runtime dotnet --runtime-version 8 \
     --functions-version 4 --name fa-auae-dsdev-lgca-dig04 \
     --storage-account stagcadigauae01
   ```

2. **Configure Environment Variables**:
   ```bash
   az functionapp config appsettings set --name fa-auae-dsdev-lgca-dig04 \
     --resource-group rg-auae-dsdev-lgca-dig04 \
     --settings DATAVERSE_CONNECTIONSTRING="AuthType=ClientSecret;Url=https://rt-pp-legal-dev.crm.dynamics.com;ClientId=your-client-id;ClientSecret=your-client-secret"
   ```

3. **Deploy Function**:
   ```bash
   dotnet build --configuration Release
   func azure functionapp publish fa-auae-dsdev-lgca-dig04
   ```

## ChatGPT Enterprise Registration

Register the MCP server in ChatGPT Enterprise:

**Server URL**: `https://fa-auae-dsdev-lgca-dig04.azurewebsites.net/api/HttpTrigger`

The server provides the correct MCP descriptor format that ChatGPT expects.

## Testing

### Health Check
```bash
curl https://fa-auae-dsdev-lgca-dig04.azurewebsites.net/api/HttpTrigger
```

### Connection Test  
```bash
curl "https://fa-auae-dsdev-lgca-dig04.azurewebsites.net/api/HttpTrigger?dataverse-test"
```

### Search Test
```bash
curl "https://fa-auae-dsdev-lgca-dig04.azurewebsites.net/api/HttpTrigger?test-search&q=Project%20Aqua"
```

### MCP Protocol Test
```bash
curl -X POST "https://fa-auae-dsdev-lgca-dig04.azurewebsites.net/api/HttpTrigger" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"Project Aqua"}}}'
```

## SQL Queries Used

The server executes these SQL queries using SQL4CDS (same as working example):

```sql
-- Legal matters search
SELECT TOP(10) legalops_matterid, legalops_name, legalops_code, legalops_description 
FROM dbo.legalops_matters 
WHERE (legalops_name LIKE '%Project Aqua%' OR legalops_code LIKE '%Project Aqua%') 
  AND (legalops_highlyconfidential IS NULL OR legalops_highlyconfidential != 1)

-- IP matters search  
SELECT TOP(10) legalops_mattersipid, legalops_name, legalops_code, legalops_description 
FROM dbo.legalops_mattersip 
WHERE (legalops_name LIKE '%Project Aqua%' OR legalops_code LIKE '%Project Aqua%') 
  AND (legalops_highlyconfidential IS NULL OR legalops_highlyconfidential != 1)
```

## Advantages over Python Version

1. ✅ **Direct SQL4CDS Library**: No manual SQL parsing or OData conversion
2. ✅ **Proven Authentication**: ServiceClient handles all Dataverse auth complexity  
3. ✅ **Better Performance**: Compiled .NET vs interpreted Python
4. ✅ **Exact Working Patterns**: Same code patterns as proven desktop example
5. ✅ **Built-in Caching**: IMemoryCache integration
6. ✅ **Type Safety**: Strong typing prevents runtime errors

This implementation should work immediately with your Dataverse environment since it uses the exact same libraries and authentication patterns as the working desktop example.