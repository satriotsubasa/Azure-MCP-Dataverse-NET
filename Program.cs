using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using MarkMpn.Sql4Cds.Engine;
using DataverseMcp.FunctionApp.Services;
using Microsoft.Azure.Functions.Worker;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Add memory caching
        services.AddMemoryCache();
        
        // Add HTTP client for network tests
        services.AddHttpClient();
        
        // Add Dataverse connection with error handling
        services.AddSingleton<ServiceClient>(serviceProvider =>
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTIONSTRING");
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("DATAVERSE_CONNECTIONSTRING environment variable is not set.");
                }
                
                return new ServiceClient(connectionString);
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                Console.WriteLine($"Failed to initialize ServiceClient: {ex.Message}");
                throw;
            }
        });
        
        // Add SQL4CDS connection with error handling  
        services.AddSingleton<Sql4CdsConnection>(serviceProvider =>
        {
            try
            {
                var dataverseClient = serviceProvider.GetRequiredService<ServiceClient>();
                return new Sql4CdsConnection(dataverseClient) { UseLocalTimeZone = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize Sql4CdsConnection: {ex.Message}");
                throw;
            }
        });
        
        // Add Dataverse service with optional registration (don't fail startup)
        services.AddScoped<DataverseService>(serviceProvider =>
        {
            try
            {
                return new DataverseService(
                    serviceProvider.GetRequiredService<Sql4CdsConnection>(),
                    serviceProvider.GetRequiredService<IMemoryCache>(),
                    serviceProvider.GetRequiredService<ILogger<DataverseService>>()
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize DataverseService: {ex.Message}");
                return null; // Return null instead of throwing
            }
        });
    })
    .Build();

host.Run();