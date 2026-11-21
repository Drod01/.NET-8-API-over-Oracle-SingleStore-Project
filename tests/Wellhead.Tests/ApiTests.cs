using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Wellhead.Api.Contracts;

namespace Wellhead.Api.Tests;

/// <summary>
/// Boots the real ASP.NET Core pipeline in demo mode (no Oracle, no SingleStore,
/// no Kafka) and exercises the HTTP contract end to end.
/// </summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    public async Task Health_reports_demo_mode_and_model_version()
    {
        var response = await CreateClient().GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.Equal("ok", health!.Status);
        Assert.Equal("demo", health.Mode);
        Assert.Equal("arps-exponential-v1", health.ModelVersion);
        Assert.False(health.KafkaIngress);
    }

    [Fact]
    public async Task Wells_endpoint_lists_the_asset_base()
    {
        var response = await CreateClient().GetAsync("/api/wells");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.True(document.RootElement.GetArrayLength() >= 6);
    }

    [Fact]
    public async Task Kpi_endpoint_returns_aggregates()
    {
        var response = await CreateClient().GetAsync("/api/kpis?limit=10");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("count").GetInt32() > 0);
        var first = document.RootElement.GetProperty("kpis")[0];
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("wellId").GetString()));
        Assert.True(first.GetProperty("volumeBbl").GetDouble() > 0);
    }

    [Fact]
    public async Task Forecast_endpoint_returns_a_p10_p50_p90_band()
    {
        var response = await CreateClient().GetAsync("/api/wells/W-0001/forecast?days=7");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("arps-exponential-v1", document.RootElement.GetProperty("modelVersion").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("points").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_well_returns_not_found()
    {
        var response = await CreateClient().GetAsync("/api/wells/W-9999/forecast");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Alerts_endpoint_returns_the_flagged_windows()
    {
        var response = await CreateClient().GetAsync("/api/alerts?hours=48");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("alerts").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Window_ingest_validates_and_accepts_partial_batches()
    {
        var payload = new object[]
        {
            new
            {
                wellId = "W-0100",
                windowStart = "2026-06-01T00:00:00Z",
                windowEnd = "2026-06-01T00:05:00Z",
                sampleCount = 5,
                expectedSamples = 5,
                avgFlowBbl = 120.0,
                totalVolumeBbl = 0.4167,
                flowCv = 0.01,
                avgPressurePsi = 1500.0,
                maxPressurePsi = 1520.0,
                avgTempF = 150.0
            },
            new
            {
                wellId = "",
                windowStart = "2026-06-01T00:05:00Z",
                windowEnd = "2026-06-01T00:10:00Z",
                sampleCount = 5,
                expectedSamples = 5,
                avgFlowBbl = 120.0,
                totalVolumeBbl = 0.4167,
                flowCv = 0.01,
                avgPressurePsi = 1500.0,
                maxPressurePsi = 1520.0,
                avgTempF = 150.0
            }
        };

        var response = await CreateClient().PostAsJsonAsync("/api/windows", payload);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IngestResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result!.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task Windows_endpoint_pages_by_limit()
    {
        var response = await CreateClient().GetAsync("/api/wells/W-0001/windows?limit=12");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(12, document.RootElement.GetProperty("count").GetInt32());
    }
}
