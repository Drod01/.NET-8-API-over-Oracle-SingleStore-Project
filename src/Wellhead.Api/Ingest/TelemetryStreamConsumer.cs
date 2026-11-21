using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Wellhead.Api.Data;

namespace Wellhead.Api.Ingest;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = "well.telemetry";
    public string GroupId { get; set; } = "wellhead-api";
    public int BatchSize { get; set; } = 250;
    public int WindowSeconds { get; set; } = 300;
    public int LatenessSeconds { get; set; } = 90;
    public int ExpectedSamples { get; set; } = 5;
}

/// <summary>
/// Event-driven ingress: consume telemetry, validate, window, upsert.
/// </summary>
/// <remarks>
/// Registered as a hosted service only when <c>Kafka:BootstrapServers</c> is
/// configured, so the API also runs as a pure read service (and in demo mode)
/// with no broker present. Offsets are committed only after a successful batch
/// write, which makes a crash replay the batch instead of losing it; the
/// idempotent upsert absorbs the replay.
/// </remarks>
public sealed class TelemetryStreamConsumer(
    ITelemetryRepository telemetry,
    IOptions<KafkaOptions> options,
    ILogger<TelemetryStreamConsumer> logger) : BackgroundService
{
    private readonly KafkaOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            logger.LogInformation("Kafka ingress disabled: Kafka:BootstrapServers is not set");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            ClientId = "wellhead-api"
        };

        var accumulator = new WindowAccumulator(
            TimeSpan.FromSeconds(_options.WindowSeconds),
            TimeSpan.FromSeconds(_options.LatenessSeconds),
            _options.ExpectedSamples);
        var validator = new ReadingValidator();
        var buffer = new List<Domain.ProductionWindow>(_options.BatchSize);
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.Topic);
        logger.LogInformation(
            "Consuming {Topic} from {BootstrapServers} as {GroupId}",
            _options.Topic, _options.BootstrapServers, _options.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result?.Message?.Value is null)
                {
                    await FlushAsync(consumer, buffer, stoppingToken);
                    continue;
                }

                if (!TelemetryEvent.TryParse(result.Message.Value, out var telemetryEvent, out var error))
                {
                    logger.LogWarning("Dropping malformed event at {Offset}: {Error}", result.Offset, error);
                    continue;
                }

                var validation = validator.Validate(telemetryEvent!, DateTimeOffset.UtcNow);
                if (!validation.Ok)
                {
                    logger.LogWarning(
                        "Rejecting event for {WellId}: {Reasons}",
                        telemetryEvent!.WellId, string.Join("; ", validation.Reasons));
                    continue;
                }

                buffer.AddRange(accumulator.Add(telemetryEvent!));
                if (buffer.Count >= _options.BatchSize)
                {
                    await FlushAsync(consumer, buffer, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            buffer.AddRange(accumulator.Flush());
            await FlushAsync(consumer, buffer, stoppingToken);
            consumer.Close();
            logger.LogInformation(
                "Consumer stopped: {Closed} windows closed, {Late} late events dropped",
                accumulator.ClosedWindows, accumulator.LateDropped);
        }
    }

    private async Task FlushAsync(
        IConsumer<string, string> consumer,
        List<Domain.ProductionWindow> buffer,
        CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        try
        {
            var written = await telemetry.UpsertWindowsAsync(buffer, cancellationToken);
            consumer.Commit();
            logger.LogInformation("Upserted {Count} windows ({Written} affected rows)", buffer.Count, written);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No commit: the batch is replayed on restart, and the upsert is idempotent.
            logger.LogError(ex, "Window batch write failed; offsets left uncommitted for replay");
        }
        finally
        {
            buffer.Clear();
        }
    }
}
