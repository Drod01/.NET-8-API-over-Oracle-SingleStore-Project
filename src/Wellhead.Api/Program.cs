using Dapper;
using Wellhead.Api.Contracts;
using Wellhead.Api.Data;
using Wellhead.Api.Endpoints;
using Wellhead.Api.Ingest;
using Wellhead.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Oracle and SingleStore return UPPER_SNAKE column names; this lets the same
// domain records map from both engines without per-dialect mapping code.
DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddProblemDetails();
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));

var oracleConnectionString = builder.Configuration.GetConnectionString("Oracle");
var singleStoreConnectionString = builder.Configuration.GetConnectionString("SingleStore");
var demoMode = string.IsNullOrWhiteSpace(oracleConnectionString) || string.IsNullOrWhiteSpace(singleStoreConnectionString);

var kafka = builder.Configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
var kafkaEnabled = !string.IsNullOrWhiteSpace(kafka.BootstrapServers);

// Demo mode is explicit and reported on /health: the service runs end to end
// with seeded in-memory repositories when no connection strings are supplied,
// so `dotnet run` and the integration tests need no infrastructure.
builder.Services.AddSingleton(new RuntimeInfo(demoMode ? "demo" : "oracle+singlestore", kafkaEnabled));

if (demoMode)
{
    builder.Services.AddSingleton<InMemoryWellRepository>();
    builder.Services.AddSingleton<IWellRepository>(sp => sp.GetRequiredService<InMemoryWellRepository>());
    builder.Services.AddSingleton<InMemoryTelemetryRepository>();
    builder.Services.AddSingleton<ITelemetryRepository>(sp => sp.GetRequiredService<InMemoryTelemetryRepository>());
}
else
{
    var oracleFactory = new OracleConnectionFactory(oracleConnectionString!);
    var singleStoreFactory = new SingleStoreConnectionFactory(singleStoreConnectionString!);
    builder.Services.AddSingleton<IWellRepository>(new OracleWellRepository(oracleFactory));
    builder.Services.AddSingleton<ITelemetryRepository>(new SingleStoreTelemetryRepository(singleStoreFactory));
}

builder.Services.AddSingleton<KpiService>();
builder.Services.AddSingleton<ForecastService>();

if (kafkaEnabled)
{
    builder.Services.AddHostedService<TelemetryStreamConsumer>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.MapWellEndpoints();
app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real pipeline in tests.</summary>
public partial class Program;
