namespace Wellhead.Api.Domain;

public sealed record DeclineFit(double QiBpd, double DiPerDay, double RSquared, double RmseBpd, int Points)
{
    public double Predict(double dayOffset) => QiBpd * Math.Exp(-DiPerDay * Math.Max(0d, dayOffset));

    public double MonthlyDeclinePct => (1d - Math.Exp(-DiPerDay * 30d)) * 100d;
}

/// <summary>
/// Arps exponential decline: <c>q(t) = qi * exp(-Di * t)</c>.
/// </summary>
/// <remarks>
/// Exponential decline is the standard first-pass forecasting model for a
/// producing well. The fit is a least-squares line through <c>ln(q)</c> versus
/// time, so it is deterministic, explainable, and needs no training data - the
/// reason it is the baseline forecast exposed by this API and used as an
/// anomaly feature in the Python pipeline.
/// </remarks>
public static class DeclineCurve
{
    public const string ModelVersion = "arps-exponential-v1";
    private const double Z80 = 1.2816; // one-sided 90% band => 80% central interval

    public static DeclineFit? Fit(IReadOnlyList<double> ratesBpd, double intervalDays)
    {
        if (intervalDays <= 0 || ratesBpd.Count < 4)
        {
            return null;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        for (var i = 0; i < ratesBpd.Count; i++)
        {
            if (ratesBpd[i] <= 0)
            {
                continue;
            }

            xs.Add(i * intervalDays);
            ys.Add(Math.Log(ratesBpd[i]));
        }

        if (xs.Count < 4)
        {
            return null;
        }

        var meanX = xs.Average();
        var meanY = ys.Average();
        double sxx = 0, sxy = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            sxx += Math.Pow(xs[i] - meanX, 2);
            sxy += (xs[i] - meanX) * (ys[i] - meanY);
        }

        if (sxx <= 0)
        {
            return null;
        }

        var slope = sxy / sxx;
        var intercept = meanY - slope * meanX;
        var qi = Math.Exp(intercept);
        if (!double.IsFinite(qi) || qi <= 0)
        {
            return null;
        }

        // A rising rate is not a decline; clamp so the model cannot forecast growth.
        var di = Math.Max(0d, -slope);
        double ssRes = 0, meanObserved = ratesBpd.Where(r => r > 0).Average(), ssTot = 0;
        var predicted = new List<double>(xs.Count);
        for (var i = 0; i < xs.Count; i++)
        {
            var p = qi * Math.Exp(-di * xs[i]);
            predicted.Add(p);
            ssRes += Math.Pow(ratesBpd.Where(r => r > 0).ElementAt(i) - p, 2);
            ssTot += Math.Pow(ratesBpd.Where(r => r > 0).ElementAt(i) - meanObserved, 2);
        }

        var r2 = ssTot > 0 ? 1d - ssRes / ssTot : 0d;
        var rmse = Math.Sqrt(ssRes / xs.Count);
        return new DeclineFit(qi, di, r2, rmse, xs.Count);
    }

    public static IReadOnlyList<ForecastPoint> Forecast(
        IReadOnlyList<double> ratesBpd,
        double intervalDays,
        int horizonDays,
        DateOnly startDate)
    {
        var fit = Fit(ratesBpd, intervalDays);
        if (fit is null)
        {
            return Array.Empty<ForecastPoint>();
        }

        // Residual spread becomes the prediction band: an honest, cheap way to
        // express uncertainty without pretending to a probabilistic model.
        var positives = ratesBpd.Where(r => r > 0).ToList();
        var residuals = new List<double>(positives.Count);
        for (var i = 0; i < positives.Count; i++)
        {
            residuals.Add(positives[i] - fit.Predict(i * intervalDays));
        }

        var sigma = residuals.Count > 1
            ? Math.Sqrt(residuals.Sum(r => r * r) / (residuals.Count - 1))
            : 0d;
        var band = Z80 * sigma;

        var points = new List<ForecastPoint>(Math.Max(0, horizonDays));
        for (var day = 1; day <= horizonDays; day++)
        {
            var p50 = Math.Max(0d, fit.Predict(day));
            points.Add(new ForecastPoint(
                startDate.AddDays(day),
                Math.Max(0d, p50 - band),
                p50,
                p50 + band));
        }

        return points;
    }

    /// <summary>Annualised-to-monthly decline percentage for KPI reporting.</summary>
    public static double MonthlyDeclinePct(IReadOnlyList<double> ratesBpd, double intervalDays)
    {
        var fit = Fit(ratesBpd, intervalDays);
        return fit?.MonthlyDeclinePct ?? 0d;
    }
}
