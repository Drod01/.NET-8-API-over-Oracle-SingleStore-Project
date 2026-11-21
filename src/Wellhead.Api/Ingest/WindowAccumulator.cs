using Wellhead.Api.Domain;

namespace Wellhead.Api.Ingest;

/// <summary>
/// Keyed, event-time tumbling windows with a watermark.
/// </summary>
/// <remarks>
/// The same operator contract as the Python service: windows close on event
/// time, stragglers inside the lateness budget still land in the right window,
/// and anything later is counted rather than silently changing an emitted row.
/// Written without a library so the behaviour (and its cost) is visible.
/// </remarks>
public sealed class WindowAccumulator
{
    private sealed class State
    {
        public int Count;
        public double FlowSum;
        public double FlowSquareSum;
        public double VolumeSum;
        public double PressureSum;
        public double PressureMax;
        public double TempSum;
    }

    private readonly TimeSpan _window;
    private readonly TimeSpan _allowedLateness;
    private readonly int _expectedSamples;
    private readonly Dictionary<(string WellId, DateTimeOffset Start), State> _state = new();
    private DateTimeOffset? _maxEventTime;
    private DateTimeOffset? _watermark;

    public WindowAccumulator(TimeSpan window, TimeSpan allowedLateness, int expectedSamples)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "window must be positive");
        }

        if (allowedLateness < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(allowedLateness), "lateness cannot be negative");
        }

        if (expectedSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSamples), "expected samples must be positive");
        }

        _window = window;
        _allowedLateness = allowedLateness;
        _expectedSamples = expectedSamples;
    }

    public int LateDropped { get; private set; }

    public int OutOfOrder { get; private set; }

    public int ClosedWindows { get; private set; }

    public int OpenWindows => _state.Count;

    public DateTimeOffset? Watermark => _watermark;

    public IReadOnlyList<ProductionWindow> Add(TelemetryEvent telemetryEvent)
    {
        if (_maxEventTime is { } max && telemetryEvent.Timestamp < max)
        {
            OutOfOrder++;
        }

        var start = Floor(telemetryEvent.Timestamp);
        var end = start + _window;
        var late = _watermark is { } watermark && end <= watermark;

        AdvanceWatermark(telemetryEvent.Timestamp);
        if (late)
        {
            LateDropped++;
            return Array.Empty<ProductionWindow>();
        }

        var key = (telemetryEvent.WellId, start);
        if (!_state.TryGetValue(key, out var state))
        {
            state = new State();
            _state[key] = state;
        }

        var cadenceSeconds = _window.TotalSeconds / _expectedSamples;
        state.Count++;
        state.FlowSum += telemetryEvent.FlowBbl;
        state.FlowSquareSum += telemetryEvent.FlowBbl * telemetryEvent.FlowBbl;
        state.VolumeSum += telemetryEvent.FlowBbl * (cadenceSeconds / 86_400d);
        state.PressureSum += telemetryEvent.PressurePsi;
        state.PressureMax = Math.Max(state.PressureMax, telemetryEvent.PressurePsi);
        state.TempSum += telemetryEvent.TempF;

        return CloseReady();
    }

    public IReadOnlyList<ProductionWindow> Flush()
    {
        var keys = _state.Keys.OrderBy(k => k.Start).ThenBy(k => k.WellId, StringComparer.Ordinal).ToList();
        var closed = new List<ProductionWindow>(keys.Count);
        foreach (var key in keys)
        {
            closed.Add(ToWindow(key.WellId, key.Start, _state[key]));
            _state.Remove(key);
        }

        ClosedWindows += closed.Count;
        return closed;
    }

    private void AdvanceWatermark(DateTimeOffset timestamp)
    {
        if (_maxEventTime is null || timestamp > _maxEventTime)
        {
            _maxEventTime = timestamp;
        }

        var candidate = _maxEventTime!.Value - _allowedLateness;
        if (_watermark is null || candidate > _watermark)
        {
            _watermark = candidate;
        }
    }

    private IReadOnlyList<ProductionWindow> CloseReady()
    {
        if (_watermark is not { } watermark)
        {
            return Array.Empty<ProductionWindow>();
        }

        var ready = _state.Keys
            .Where(key => key.Start + _window <= watermark)
            .OrderBy(key => key.Start)
            .ThenBy(key => key.WellId, StringComparer.Ordinal)
            .ToList();
        if (ready.Count == 0)
        {
            return Array.Empty<ProductionWindow>();
        }

        var closed = new List<ProductionWindow>(ready.Count);
        foreach (var key in ready)
        {
            closed.Add(ToWindow(key.WellId, key.Start, _state[key]));
            _state.Remove(key);
        }

        ClosedWindows += closed.Count;
        return closed;
    }

    private ProductionWindow ToWindow(string wellId, DateTimeOffset start, State state)
    {
        var average = state.FlowSum / state.Count;
        double stdev = 0;
        if (state.Count > 1)
        {
            var variance = Math.Max(0d,
                (state.FlowSquareSum - state.Count * average * average) / (state.Count - 1));
            stdev = Math.Sqrt(variance);
        }

        return new ProductionWindow
        {
            WellId = wellId,
            WindowStart = start,
            WindowEnd = start + _window,
            SampleCount = state.Count,
            ExpectedSamples = _expectedSamples,
            AvgFlowBbl = average,
            TotalVolumeBbl = state.VolumeSum,
            FlowCv = average > 0 ? stdev / average : 0d,
            AvgPressurePsi = state.PressureSum / state.Count,
            MaxPressurePsi = state.PressureMax,
            AvgTempF = state.TempSum / state.Count
        };
    }

    private DateTimeOffset Floor(DateTimeOffset timestamp)
    {
        var seconds = (long)_window.TotalSeconds;
        var epoch = timestamp.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(epoch - epoch % seconds);
    }
}
