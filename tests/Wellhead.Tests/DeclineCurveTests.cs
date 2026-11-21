using Wellhead.Api.Domain;

namespace Wellhead.Api.Tests;

public class DeclineCurveTests
{
    private static List<double> Arps(double qi, double di, int points, double intervalDays = 1d) =>
        Enumerable.Range(0, points).Select(i => qi * Math.Exp(-di * i * intervalDays)).ToList();

    [Fact]
    public void Fit_recovers_known_parameters()
    {
        var fit = DeclineCurve.Fit(Arps(1_000, 0.01, 20), 1d);

        Assert.NotNull(fit);
        Assert.Equal(1_000, fit!.QiBpd, 1);
        Assert.Equal(0.01, fit.DiPerDay, 4);
        Assert.True(fit.RSquared > 0.999);
        Assert.Equal(20, fit.Points);
    }

    [Fact]
    public void Five_minute_windows_are_supported()
    {
        var interval = 5d / 1440d;
        var rates = Arps(800, 0.02, 24, interval);

        var fit = DeclineCurve.Fit(rates, interval);

        Assert.NotNull(fit);
        Assert.Equal(800, fit!.QiBpd, 1);
        Assert.Equal(0.02, fit.DiPerDay, 4);
    }

    [Fact]
    public void Forecast_produces_ordered_bands_that_decline()
    {
        var rates = Arps(1_000, 0.005, 24);

        var points = DeclineCurve.Forecast(rates, 1d, 7, new DateOnly(2026, 6, 1));

        Assert.Equal(7, points.Count);
        Assert.All(points, point =>
        {
            Assert.True(point.P10Bbl <= point.P50Bbl);
            Assert.True(point.P50Bbl <= point.P90Bbl);
            Assert.True(point.P50Bbl >= 0);
        });
        Assert.True(points[0].P50Bbl > points[^1].P50Bbl);
        Assert.Equal(new DateOnly(2026, 6, 2), points[0].Date);
    }

    [Fact]
    public void Insufficient_history_yields_no_fit()
    {
        Assert.Null(DeclineCurve.Fit(new[] { 100d, 90d, 80d }, 1d));
        Assert.Empty(DeclineCurve.Forecast(new[] { 100d, 90d, 80d }, 1d, 7, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Zero_rates_are_skipped_and_rising_rates_are_clamped()
    {
        var withGaps = new List<double> { 0, 1_000, 0, 990, 980, 970 };
        var fit = DeclineCurve.Fit(withGaps, 1d);
        Assert.NotNull(fit);

        var rising = DeclineCurve.Fit(new[] { 100d, 110d, 121d, 133d, 146d }, 1d);
        Assert.NotNull(rising);
        Assert.Equal(0d, rising!.DiPerDay);
        Assert.Equal(0d, rising.MonthlyDeclinePct, 6);
    }

    [Fact]
    public void Monthly_decline_matches_the_definition()
    {
        var fit = DeclineCurve.Fit(Arps(1_000, 0.01, 10), 1d);
        var expected = (1 - Math.Exp(-0.01 * 30)) * 100;

        Assert.NotNull(fit);
        Assert.Equal(expected, fit!.MonthlyDeclinePct, 6);
        Assert.Equal(expected, DeclineCurve.MonthlyDeclinePct(Arps(1_000, 0.01, 10), 1d), 6);
    }
}
