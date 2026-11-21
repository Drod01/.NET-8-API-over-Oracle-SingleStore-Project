using Wellhead.Api.Services;

namespace Wellhead.Api.Tests;

public class KpiServiceTests
{
    [Fact]
    public async Task Kpi_aggregates_windows_for_a_well()
    {
        var windows = TestWindows.Arps(count: 24, qi: 1_000, diPerDay: 0.01);
        var service = new KpiService(new FakeTelemetryRepository(windows), new FakeWellRepository());

        var kpis = await service.GetAsync(10, CancellationToken.None);

        var kpi = Assert.Single(kpis);
        Assert.Equal("W-0001", kpi.WellId);
        Assert.Equal(24, kpi.Windows);
        Assert.Equal(1d, kpi.CoverageRatio, 6);
        Assert.True(kpi.VolumeBbl > 0);
        Assert.InRange(kpi.AvgFlowBpd, 990, 1_001);
        Assert.InRange(kpi.MonthlyDeclinePct, 25d, 27d);
    }

    [Fact]
    public async Task Wells_without_windows_are_skipped()
    {
        var service = new KpiService(new FakeTelemetryRepository(Array.Empty<Wellhead.Api.Domain.ProductionWindow>()), new FakeWellRepository());

        var kpis = await service.GetAsync(10, CancellationToken.None);

        Assert.Empty(kpis);
    }
}

public class ForecastServiceTests
{
    [Fact]
    public async Task Forecast_fits_and_projects_a_band()
    {
        var windows = TestWindows.Arps(count: 24, qi: 1_000, diPerDay: 0.01);
        var service = new ForecastService(new FakeTelemetryRepository(windows), new FakeWellRepository());

        var forecast = await service.GetAsync("W-0001", 7, CancellationToken.None);

        Assert.NotNull(forecast);
        Assert.Equal(24, forecast!.WindowsUsed);
        Assert.Equal(7, forecast.Points.Count);
        Assert.Equal(1_000, forecast.QiBpd, 0);
        Assert.Equal(0.01, forecast.DiPerDay, 3);
        Assert.Equal("arps-exponential-v1", forecast.ModelVersion);
        Assert.All(forecast.Points, point =>
        {
            Assert.True(point.P10Bbl <= point.P50Bbl);
            Assert.True(point.P50Bbl <= point.P90Bbl);
        });
        Assert.True(forecast.Points[0].P50Bbl > forecast.Points[^1].P50Bbl);
    }

    [Fact]
    public async Task Unknown_well_returns_null()
    {
        var service = new ForecastService(new FakeTelemetryRepository(Array.Empty<Wellhead.Api.Domain.ProductionWindow>()), new FakeWellRepository());

        Assert.Null(await service.GetAsync("W-9999", 7, CancellationToken.None));
    }

    [Fact]
    public async Task Insufficient_history_returns_an_empty_forecast()
    {
        var windows = TestWindows.Arps(count: 2);
        var service = new ForecastService(new FakeTelemetryRepository(windows), new FakeWellRepository());

        var forecast = await service.GetAsync("W-0001", 7, CancellationToken.None);

        Assert.NotNull(forecast);
        Assert.Empty(forecast!.Points);
        Assert.Equal(2, forecast.WindowsUsed);
    }
}
