using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wellhead.Api.Ingest;

/// <summary>The Kafka/HTTP event contract; identical to the Python producer's.</summary>
public sealed record TelemetryEvent(
    string WellId,
    DateTimeOffset Timestamp,
    double PressurePsi,
    double TempF,
    double FlowBbl,
    string Source)
{
    private static readonly Regex WellIdPattern = new("^[A-Z0-9][A-Z0-9-]{2,31}$", RegexOptions.Compiled);

    /// <summary>Parses the wire format without throwing on bad payloads.</summary>
    public static bool TryParse(string json, out TelemetryEvent? telemetryEvent, out string? error)
    {
        telemetryEvent = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "payload is not a JSON object";
                return false;
            }

            if (!root.TryGetProperty("well_id", out var wellId) ||
                !root.TryGetProperty("ts", out var ts) ||
                !root.TryGetProperty("pressure_psi", out var pressure) ||
                !root.TryGetProperty("temp_f", out var temp) ||
                !root.TryGetProperty("flow_bbl", out var flow))
            {
                error = "missing required field (well_id, ts, pressure_psi, temp_f, flow_bbl)";
                return false;
            }

            if (!DateTimeOffset.TryParse(ts.GetString(), out var timestamp))
            {
                error = "ts is not an ISO-8601 timestamp";
                return false;
            }

            telemetryEvent = new TelemetryEvent(
                wellId.GetString() ?? string.Empty,
                timestamp,
                pressure.GetDouble(),
                temp.GetDouble(),
                flow.GetDouble(),
                root.TryGetProperty("source", out var source) ? source.GetString() ?? "scada" : "scada");
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool HasValidWellId() => WellIdPattern.IsMatch(WellId);
}

/// <summary>Field-level quality gates applied before a window closes.</summary>
public sealed class ReadingValidator
{
    public double MaxPressurePsi { get; init; } = 5_000d;
    public double MinPressurePsi { get; init; }
    public double MaxTempF { get; init; } = 400d;
    public double MinTempF { get; init; } = -40d;
    public double MaxFlowBpd { get; init; } = 50_000d;
    public TimeSpan FutureTolerance { get; init; } = TimeSpan.FromMinutes(5);

    public ValidationOutcome Validate(TelemetryEvent telemetryEvent, DateTimeOffset now)
    {
        var reasons = new List<string>(capacity: 3);

        if (!telemetryEvent.HasValidWellId())
        {
            reasons.Add($"invalid_well_id:{telemetryEvent.WellId}");
        }

        if (!double.IsFinite(telemetryEvent.PressurePsi) ||
            !double.IsFinite(telemetryEvent.TempF) ||
            !double.IsFinite(telemetryEvent.FlowBbl))
        {
            reasons.Add("non_finite_value");
            return new ValidationOutcome(false, reasons);
        }

        if (telemetryEvent.PressurePsi < MinPressurePsi || telemetryEvent.PressurePsi > MaxPressurePsi)
        {
            reasons.Add($"pressure_out_of_range:{telemetryEvent.PressurePsi:F1}");
        }

        if (telemetryEvent.TempF < MinTempF || telemetryEvent.TempF > MaxTempF)
        {
            reasons.Add($"temp_out_of_range:{telemetryEvent.TempF:F1}");
        }

        if (telemetryEvent.FlowBbl < 0 || telemetryEvent.FlowBbl > MaxFlowBpd)
        {
            reasons.Add($"flow_out_of_range:{telemetryEvent.FlowBbl:F1}");
        }

        if (telemetryEvent.Timestamp - now > FutureTolerance)
        {
            reasons.Add("timestamp_in_future");
        }

        return new ValidationOutcome(reasons.Count == 0, reasons);
    }
}

public sealed record ValidationOutcome(bool Ok, IReadOnlyList<string> Reasons)
{
    public static readonly ValidationOutcome Valid = new(true, Array.Empty<string>());
}
