using Azure.Storage.Blobs;
using DataIntegrationIngestionApp.Options;
using DataIntegrationIngestionApp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services
    .AddOptions<IngestionOptions>()
    .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName));

// Blob client for reading large payloads referenced by a message. Registered only when a
// blob storage connection string is configured; PayloadReader falls back to null otherwise.
var blobConnectionString = builder.Configuration
    .GetSection(IngestionOptions.SectionName)[nameof(IngestionOptions.BlobStorageConnectionString)];

if (!string.IsNullOrWhiteSpace(blobConnectionString))
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
}

builder.Services.AddSingleton<IPayloadReader, PayloadReader>();
builder.Services.AddSingleton<IRecordDeserializer, RecordDeserializer>();
builder.Services.AddSingleton<ISqlBulkInserter, SqlBulkInserter>();
builder.Services.AddSingleton<IRetryDelayPolicy, RetryDelayPolicy>();

builder.Build().Run();
