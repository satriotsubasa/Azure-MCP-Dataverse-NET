using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using MarkMpn.Sql4Cds.Engine;
using DataverseMcp.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Dataverse MCP Server", Version = "v1" });
});

// Add memory caching
builder.Services.AddMemoryCache();

// Add HTTP client for network tests
builder.Services.AddHttpClient();

// Add CORS for ChatGPT integration
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Dataverse connection with optional registration (don't fail startup)
builder.Services.AddSingleton<ServiceClient?>(serviceProvider =>
{
    try
    {
        var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTIONSTRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine("DATAVERSE_CONNECTIONSTRING environment variable is not set.");
            return null;
        }
        
        return new ServiceClient(connectionString);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to initialize ServiceClient: {ex.Message}");
        return null;
    }
});

// Add SQL4CDS connection with optional registration
builder.Services.AddSingleton<Sql4CdsConnection?>(serviceProvider =>
{
    try
    {
        var dataverseClient = serviceProvider.GetService<ServiceClient>();
        if (dataverseClient == null)
        {
            Console.WriteLine("ServiceClient not available, skipping Sql4CdsConnection");
            return null;
        }
        return new Sql4CdsConnection(dataverseClient) { UseLocalTimeZone = true };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to initialize Sql4CdsConnection: {ex.Message}");
        return null;
    }
});

// Add Dataverse service with optional registration
builder.Services.AddScoped<DataverseService?>(serviceProvider =>
{
    try
    {
        var sql4CdsConnection = serviceProvider.GetService<Sql4CdsConnection>();
        if (sql4CdsConnection == null)
        {
            Console.WriteLine("Sql4CdsConnection not available, skipping DataverseService");
            return null;
        }
        
        return new DataverseService(
            sql4CdsConnection,
            serviceProvider.GetRequiredService<IMemoryCache>(),
            serviceProvider.GetRequiredService<ILogger<DataverseService>>()
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to initialize DataverseService: {ex.Message}");
        return null;
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable CORS
app.UseCors();

// Map controllers
app.MapControllers();

// Health check endpoint
app.MapGet("/", () => new
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
        diagnostic = "GET /api/diagnostic",
        network_test = "GET /api/network-test"
    }
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"Starting Dataverse MCP Server on port {port}");
app.Run();