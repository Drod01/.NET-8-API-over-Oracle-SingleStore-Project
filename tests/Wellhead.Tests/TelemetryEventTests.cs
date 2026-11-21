using Wellhead.Api.Ingest;

namespace Wellhead.Api.Tests;

public class TelemetryEventTests
{
    private const string PythonPayload =
        "{\"well_id\":\"W-0001\",\"ts\":\"2026-06-01T00:00:00+00:00\",\"pressure_psi\":1845.174," +
        "\"temp_f\":148.348,\"flow_bbl\":816.2,\"source\":\"simulated-scada\"}";

    [Fact]
    public void Parses_the_python_producer_contract()
    {
        Assert.True(TelemetryEvent.TryParse(PythonPayload, out var parsed, out var error));

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal("W-0001", parsed!.WellId);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), parsed.Timestamp);
        Assert.Equal(1845.174, parsed.PressurePsi, 3);
        Assert.Equal("simulated-scada", parsed.Source);
    }

    [Fact]
    public void Missing_fields_are_reported_not_thrown()
    {
        Assert.False(TelemetryEvent.TryParse("{\"well_id\":\"W-0001\"}", out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Contains("missing required field", error);
    }

    [Fact]
    public void Malformed_json_is_reported_not_thrown()
    {
        Assert.False(TelemetryEvent.TryParse("not-json", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Bad_timestamp_is_reported()
    {
        var payload = PythonPayload.Replace("2026-06-01T00:00:00+00:00", "yesterday");

        Assert.False(TelemetryEvent.TryParse(payload, out _, out var error));
        Assert.Contains("ISO-8601", error);
    }

    [Fact]
    public void Validator_accepts_a_nominal_reading()
    {
        TelemetryEvent.TryParse(PythonPayload, out var parsed, out _);
        var outcome = new ReadingValidator().Validate(parsed!, DateTimeOffset.UtcNow);

        Assert.True(outcome.Ok);
        Assert.Empty(outcome.Reasons);
    }

    [Fact]
    public void Validator_reports_all_violations()
    {
        var reading = new TelemetryEvent("W-0001", DateTimeOffset.UtcNow, 99_000, 900, -5, "scada");

        var outcome = new ReadingValidator().Validate(reading, DateTimeOffset.UtcNow);

        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Reasons, r => r.StartsWith("pressure_out_of_range"));
        Assert.Contains(outcome.Reasons, r => r.StartsWith("temp_out_of_range"));
        Assert.Contains(outcome.Reasons, r => r.StartsWith("flow_out_of_range"));
    }

    [Fact]
    public void Validator_rejects_future_timestamps_and_bad_well_ids()
    {
        var future = new TelemetryEvent("W-0001", DateTimeOffset.UtcNow.AddHours(1), 1500, 150, 500, "scada");
        var badId = new TelemetryEvent("bad id!", DateTimeOffset.UtcNow, 1500, 150, 500, "scada");
        var validator = new ReadingValidator();

        Assert.Contains("timestamp_in_future", validator.Validate(future, DateTimeOffset.UtcNow).Reasons);
        Assert.Contains(validator.Validate(badId, DateTimeOffset.UtcNow).Reasons, r => r.StartsWith("invalid_well_id"));
    }

    [Fact]
    public void Validator_rejects_non_finite_values_immediately()
    {
        var reading = new TelemetryEvent("W-0001", DateTimeOffset.UtcNow, double.NaN, 150, 500, "scada");

        var outcome = new ReadingValidator().Validate(reading, DateTimeOffset.UtcNow);

        Assert.False(outcome.Ok);
        Assert.Single(outcome.Reasons);
        Assert.Equal("non_finite_value", outcome.Reasons[0]);
    }
}
