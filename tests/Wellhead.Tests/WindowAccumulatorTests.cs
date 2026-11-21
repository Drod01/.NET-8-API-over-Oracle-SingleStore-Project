using Wellhead.Api.Ingest;

namespace Wellhead.Api.Tests;

public class WindowAccumulatorTests
{
    private static readonly DateTimeOffset Base = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static TelemetryEvent Reading(double minute, double flow = 100, double pressure = 1000, string well = "W-0001") =>
        new(well, Base.AddMinutes(minute), pressure, 150, flow, "scada");

    private static WindowAccumulator Accumulator(int latenessSeconds = 0) =>
        new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(latenessSeconds), expectedSamples: 5);

    [Fact]
    public void Window_closes_only_after_the_watermark_passes()
    {
        var accumulator = Accumulator();

        for (var minute = 0; minute < 5; minute++)
        {
            Assert.Empty(accumulator.Add(Reading(minute)));
        }

        var closed = accumulator.Add(Reading(5));

        var window = Assert.Single(closed);
        Assert.Equal(Base, window.WindowStart);
        Assert.Equal(Base.AddMinutes(5), window.WindowEnd);
        Assert.Equal(5, window.SampleCount);
        Assert.Equal(100, window.AvgFlowBbl, 6);
        Assert.Equal(5 * 100 * (60d / 86_400d), window.TotalVolumeBbl, 9);
        Assert.Equal(1d, window.CoverageRatio, 6);
        Assert.Equal(1, accumulator.ClosedWindows);
    }

    [Fact]
    public void Straggler_inside_the_lateness_budget_lands_in_the_right_window()
    {
        var accumulator = Accumulator(latenessSeconds: 120);
        accumulator.Add(Reading(1));
        accumulator.Add(Reading(4));

        Assert.Empty(accumulator.Add(Reading(2)));
        Assert.Equal(1, accumulator.OutOfOrder);

        var closed = accumulator.Add(Reading(7));
        Assert.Single(closed);
        Assert.Equal(3, closed[0].SampleCount);
    }

    [Fact]
    public void Events_after_close_are_counted_not_applied()
    {
        var accumulator = Accumulator();
        for (var minute = 0; minute < 5; minute++)
        {
            accumulator.Add(Reading(minute));
        }

        accumulator.Add(Reading(5));
        Assert.Empty(accumulator.Add(Reading(2)));
        Assert.Equal(1, accumulator.LateDropped);
    }

    [Fact]
    public void Wells_are_windowed_independently()
    {
        var accumulator = Accumulator();
        for (var minute = 0; minute < 5; minute++)
        {
            accumulator.Add(Reading(minute, well: "W-0001"));
            accumulator.Add(Reading(minute, well: "W-0002"));
        }

        var closed = accumulator.Add(Reading(5, well: "W-0001"));

        Assert.Equal(new[] { "W-0001", "W-0002" }, closed.Select(w => w.WellId).ToArray());
    }

    [Fact]
    public void Flush_closes_partial_windows_with_coverage()
    {
        var accumulator = Accumulator();
        for (var minute = 0; minute < 3; minute++)
        {
            accumulator.Add(Reading(minute));
        }

        var window = Assert.Single(accumulator.Flush());
        Assert.Equal(3, window.SampleCount);
        Assert.Equal(0.6, window.CoverageRatio, 6);
        Assert.False(window.IsComplete);
        Assert.Equal(0, accumulator.OpenWindows);
    }

    [Fact]
    public void Instability_shows_up_in_the_coefficient_of_variation()
    {
        var accumulator = Accumulator();
        var flows = new[] { 100d, 10d, 100d, 10d, 100d };
        for (var minute = 0; minute < flows.Length; minute++)
        {
            accumulator.Add(Reading(minute, flow: flows[minute]));
        }

        var window = Assert.Single(accumulator.Add(Reading(5, flow: 100)));
        Assert.True(window.FlowCv > 0.4);
        Assert.NotEqual(0, window.DailyRateBbl);
    }

    [Fact]
    public void Invalid_configuration_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowAccumulator(TimeSpan.Zero, TimeSpan.Zero, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowAccumulator(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(-1), 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowAccumulator(TimeSpan.FromMinutes(5), TimeSpan.Zero, 0));
    }
}
