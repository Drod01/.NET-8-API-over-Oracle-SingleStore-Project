using Wellhead.Api.Contracts;
using Wellhead.Api.Data;
using Wellhead.Api.Domain;
using Wellhead.Api.Services;

namespace Wellhead.Api.Endpoints;

/// <summary>
/// HTTP surface for internal consumers: master data, windows, KPIs, forecasts
/// and alert review.
/// </summary>
/// <remarks>
/// Handlers are static methods rather than inline lambdas so they can be read,
/// grepped and unit tested like any other method.
/// </remarks>
public static class WellEndpoints
{
    public static IEndpointRouteBuilder MapWellEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", Health);

        var api = app.MapGroup("/api");
        api.MapGet("/wells", ListWells);
        api.MapGet("/wells/{wellId}", GetWell);
        api.MapGet("/wells/{wellId}/windows", GetWindows);
        api.MapGet("/wells/{wellId}/forecast", GetForecast);
        api.MapGet("/kpis", GetKpis);
        api.MapGet("/alerts", GetAlerts);
        api.MapPost("/windows", IngestWindows);

        return app;
    }

    private static IResult Health(RuntimeInfo runtime) => Results.Ok(new HealthResponse(
        "ok",
        runtime.Mode,
        "wellhead-api",
        DeclineCurve.ModelVersion,
        runtime.KafkaIngress,
        DateTimeOffset.UtcNow));

    private static async Task<IResult> ListWells(IWellRepository wells, CancellationToken cancellationToken)
        => Results.Ok(await wells.ListWellsAsync(cancellationToken));

    private static async Task<IResult> GetWell(
        string wellId,
        IWellRepository wells,
        ITelemetryRepository telemetry,
        CancellationToken cancellationToken)
    {
        var well = await wells.GetWellAsync(wellId, cancellationToken);
        if (well is null)
        {
            return Results.NotFound(new { error = $"unknown well '{wellId}'" });
        }

        var windowCount = await telemetry.CountWindowsAsync(wellId, cancellationToken);
        return Results.Ok(new { well, windowCount });
    }

    private static async Task<IResult> GetWindows(
        string wellId,
        int? limit,
        ITelemetryRepository telemetry,
        IWellRepository wells,
        CancellationToken cancellationToken)
    {
        if (await wells.GetWellAsync(wellId, cancellationToken) is null)
        {
            return Results.NotFound(new { error = $"unknown well '{wellId}'" });
        }

        var windows = await telemetry.LatestWindowsAsync(wellId, limit ?? 96, cancellationToken);
        return Results.Ok(new { wellId, count = windows.Count, windows });
    }

    private static async Task<IResult> GetForecast(
        string wellId,
        int? days,
        ForecastService forecasts,
        CancellationToken cancellationToken)
    {
        var forecast = await forecasts.GetAsync(wellId, days ?? 7, cancellationToken);
        if (forecast is null)
        {
            return Results.NotFound(new { error = $"unknown well '{wellId}'" });
        }

        if (forecast.Points.Count == 0)
        {
            return Results.UnprocessableEntity(new
            {
                error = "not enough history to fit a decline curve",
                windowsUsed = forecast.WindowsUsed,
                model = forecast.ModelVersion
            });
        }

        return Results.Ok(forecast);
    }

    private static async Task<IResult> GetKpis(
        int? limit,
        KpiService kpis,
        CancellationToken cancellationToken)
    {
        var rows = await kpis.GetAsync(limit ?? 50, cancellationToken);
        return Results.Ok(new { count = rows.Count, kpis = rows });
    }

    private static async Task<IResult> GetAlerts(
        int? hours,
        int? limit,
        ITelemetryRepository telemetry,
        CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours ?? 24, 1, 720));
        var alerts = await telemetry.FlaggedWindowsAsync(since, limit ?? 100, cancellationToken);
        return Results.Ok(new AlertsResponse($"last {hours ?? 24}h", alerts));
    }

    private static async Task<IResult> IngestWindows(
        IReadOnlyList<ProductionWindow> windows,
        ITelemetryRepository telemetry,
        CancellationToken cancellationToken)
    {
        var valid = new List<ProductionWindow>(windows.Count);
        var errors = new List<string>();
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (string.IsNullOrWhiteSpace(window.WellId))
            {
                errors.Add($"window[{index}]: wellId is required");
                continue;
            }

            if (window.WindowEnd <= window.WindowStart)
            {
                errors.Add($"window[{index}]: windowEnd must be after windowStart");
                continue;
            }

            if (window.SampleCount < 0 || window.ExpectedSamples < 0)
            {
                errors.Add($"window[{index}]: sample counts cannot be negative");
                continue;
            }

            valid.Add(window);
        }

        var written = await telemetry.UpsertWindowsAsync(valid, cancellationToken);
        return Results.Ok(new IngestResponse(valid.Count, errors.Count, written, errors.Take(10).ToList()));
    }
}
