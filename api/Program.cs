using api.Middleware;
using api.Models;
using api.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.UseMiddleware<AuthMiddleware>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Options
builder.Services.Configure<CosmosDbOptions>(builder.Configuration.GetSection("CosmosDb"));
builder.Services.Configure<AzureAiOptions>(builder.Configuration.GetSection("AzureAi"));

// CosmosDB
builder.Services.AddSingleton(sp =>
{
    var config = builder.Configuration;
    var endpoint = config["CosmosDb:Endpoint"]!;
    var key = config["CosmosDb:Key"]!;
    var options = new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    };
    return new CosmosClient(endpoint, key, options);
});

builder.Services.AddSingleton<CosmosContainers>();

// Services
builder.Services.AddSingleton<StatementService>();
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddHttpClient<SpendingAnalyzerService>();

builder.Build().Run();
