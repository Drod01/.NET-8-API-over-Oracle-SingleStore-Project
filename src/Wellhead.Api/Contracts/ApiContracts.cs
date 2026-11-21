using Wellhead.Api.Domain;

namespace Wellhead.Api.Contracts;

public sealed record HealthResponse(
    string Status,
    string Mode,
    string Service,
    string ModelVersion,
    bool KafkaIngress,
    DateTimeOffset Time);

public sealed record IngestResponse(
    int Accepted,
    int Rejected,
    int Written,
    IReadOnlyList<string> Errors);

public sealed record AlertsResponse(
    string Window,
    IReadOnlyList<FlaggedWindow> Alerts);

/// <summary>Deployment mode, surfaced on /health so demo data is never mistaken for production.</summary>
public sealed record RuntimeInfo(string Mode, bool KafkaIngress);
