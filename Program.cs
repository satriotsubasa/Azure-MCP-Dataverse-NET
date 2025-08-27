using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
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
        
        // Add Dataverse connection
        services.AddSingleton<ServiceClient>(serviceProvider =>
        {
            var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTIONSTRING") 
                ?? throw new InvalidOperationException("DATAVERSE_CONNECTIONSTRING environment variable is not set.");
            
            return new ServiceClient(connectionString);
        });
        
        // Add SQL4CDS connection
        services.AddSingleton<Sql4CdsConnection>(serviceProvider =>
        {
            var dataverseClient = serviceProvider.GetRequiredService<ServiceClient>();
            return new Sql4CdsConnection(dataverseClient) { UseLocalTimeZone = true };
        });
        
        // Add Dataverse service
        services.AddScoped<DataverseService>();
    })
    .Build();

host.Run();