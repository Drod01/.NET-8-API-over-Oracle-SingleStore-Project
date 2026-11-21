using Wellhead.Api.Domain;

namespace Wellhead.Api.Demo;

/// <summary>
/// Deterministic demo dataset so the API and its integration tests run with no
/// Oracle instance, no SingleStore cluster and no Kafka broker.
/// </summary>
/// <remarks>
/// This is explicitly not a fake of the database: it is a seed for the in-memory
/// repositories, and demo mode is reported by <c>/health</c> so nobody mistakes
/// it for a connected environment.
/// </remarks>
public static class DemoData
{
    public sealed record Profile(string WellId, string Name, string Basin, string County, double QiBpd, double DiPerDay, double PressurePsi, double TempF);

    public static readonly IReadOnlyList<Profile> Profiles = new[]
    {
        new Profile("W-0001", "Rattlesnake 12H", "Delaware", "Loving", 820, 0.0021, 1850, 148),
        new Profile("W-0002", "Rattlesnake 13H", "Delaware", "Loving", 610, 0.0026, 1640, 151),
        new Profile("W-0003", "Eagle Ford 4H", "Eagle Ford", "Karnes", 430, 0.0034, 1320, 163),
        new Profile("W-0004", "Wolfcamp 21H", "Midland", "Martin", 1150, 0.0017, 2240, 142),
        new Profile("W-0005", "Wolfcamp 22H", "Midland", "Martin", 980, 0.0019, 2080, 144),
        new Profile("W-0006", "Bone Spring 8H", "Delaware", "Reeves", 350, 0.0039, 1120, 158)
    };

    public static IReadOnlyList<Well> Wells() => Profiles
        .Select(p => new Well
        {
            WellId = p.WellId,
            Name = p.Name,
            Basin = p.Basin,
            County = p.County,
            Status = "producing",
            SpudDate = new DateOnly(2022, 3, 14)
        })
        .ToList();

    /// <summary>Five-minute windows for the last 24 hours, with two injected production dips.</summary>
    public static IReadOnlyList<ProductionWindow> Windows(int windowsPerWell = 288)
    {
        var anchor = DateTimeOffset.UtcNow;
        var epoch = anchor.ToUnixTimeSeconds();
        var end = DateTimeOffset.FromUnixTimeSeconds(epoch - epoch % 300);
        var windowMinutes = 5;
        var random = new Random(20260601);
        var windows = new List<ProductionWindow>(Profiles.Count * windowsPerWell);

        for (var profileIndex = 0; profileIndex < Profiles.Count; profileIndex++)
        {
            var profile = Profiles[profileIndex];
            for (var index = 0; index < windowsPerWell; index++)
            {
                var start = end.AddMinutes(-windowMinutes * (windowsPerWell - index));
                var day = index * windowMinutes / 1440d;
                var rate = profile.QiBpd * Math.Exp(-profile.DiPerDay * day) * (1 + 0.01 * (random.NextDouble() - 0.5));
                var injected = profileIndex < 2 && (index == 40 || index == 164);
                if (injected)
                {
                    rate *= 0.28;
                }

                var samples = index % 37 == 0 ? 4 : 5; // a couple of coverage gaps
                var volume = rate * samples * (windowMinutes / 1440d) / 5d;
                windows.Add(new ProductionWindow
                {
                    WellId = profile.WellId,
                    WindowStart = start,
                    WindowEnd = start.AddMinutes(windowMinutes),
                    SampleCount = samples,
                    ExpectedSamples = 5,
                    AvgFlowBbl = Math.Round(rate, 3),
                    TotalVolumeBbl = Math.Round(volume, 4),
                    FlowCv = injected ? 0.42 : 0.02,
                    AvgPressurePsi = Math.Round(profile.PressurePsi * (0.72 + 0.28 * rate / profile.QiBpd), 2),
                    MaxPressurePsi = Math.Round(profile.PressurePsi * (0.75 + 0.28 * rate / profile.QiBpd), 2),
                    AvgTempF = Math.Round(profile.TempF + 0.4 * Math.Sin(index / 12d), 2)
                });
            }
        }

        return windows;
    }
}
