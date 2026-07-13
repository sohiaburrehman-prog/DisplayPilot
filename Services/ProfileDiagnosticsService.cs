using System.Text;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Builds an explainable snapshot of profile matching and winner selection.</summary>
public static class ProfileDiagnosticsService
{
    public sealed class Entry
    {
        public string ProfileId { get; init; } = string.Empty;
        public string ProfileName { get; init; } = string.Empty;
        public int Priority { get; init; }
        public bool Matched { get; init; }
        public bool IsWinner { get; init; }
        public int? Rank { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string WinnerReason { get; init; } = string.Empty;
        public ProfileMatcher.ProfileEvaluation Evaluation { get; init; } = new();
    }

    public sealed class Snapshot
    {
        public ProfileConflictRule ConflictRule { get; init; }
        public string? WinnerProfileId { get; init; }
        public string? WinnerProfileName { get; init; }
        public IReadOnlyList<Entry> Profiles { get; init; } = Array.Empty<Entry>();
        public DateTime CapturedUtc { get; init; }
    }

    public static Snapshot Capture(
        AppSettings settings,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<LauncherChildTracker.RunningProcess> processes,
        string? liveWinnerProfileId = null,
        IReadOnlySet<string>? liveMatchedProfileIds = null)
    {
        var names = processes.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evaluations = settings.Profiles.Select(profile =>
        {
            var scene = string.IsNullOrWhiteSpace(profile.DisplaySceneId)
                ? null
                : settings.LayoutPresets.FirstOrDefault(s =>
                    string.Equals(s.Id, profile.DisplaySceneId, StringComparison.Ordinal));
            var effective = profile.Clone();
            if (!string.IsNullOrWhiteSpace(profile.DisplaySceneId))
            {
                effective.TargetMonitorDeviceName = scene?.PrimaryMonitorDeviceName
                    ?? $"missing-scene:{profile.DisplaySceneId}";
                effective.TargetMonitorName = scene?.Name ?? "Missing display scene";
            }

            return new
            {
                Profile = profile,
                Scene = scene,
                Evaluation = ProfileMatcher.Evaluate(effective, names, monitors, runningProcesses: processes),
            };
        }).ToList();

        var candidates = evaluations
            .Where(x => x.Profile.Enabled && x.Evaluation.ProcessRunning && x.Evaluation.TargetConnected)
            .Select(x => new ProfileConflictResolver.Candidate(
                x.Profile,
                x.Profile.LastTriggeredUtc == DateTime.MinValue ? 0 : x.Profile.LastTriggeredUtc.Ticks))
            .ToList();
        var ordered = ProfileConflictResolver.OrderCandidates(candidates, settings.ProfileConflictRule).ToList();
        var inferredWinner = ordered.FirstOrDefault()?.Profile.Id;
        var winnerId = string.IsNullOrWhiteSpace(liveWinnerProfileId) ? inferredWinner : liveWinnerProfileId;
        var rankById = ordered
            .Select((candidate, index) => (candidate.Profile.Id, Rank: index + 1))
            .ToDictionary(x => x.Id, x => x.Rank, StringComparer.Ordinal);

        var entries = evaluations.Select(x =>
        {
            var matched = liveMatchedProfileIds?.Contains(x.Profile.Id)
                ?? (x.Profile.Enabled && x.Evaluation.ProcessRunning && x.Evaluation.TargetConnected);
            var isWinner = string.Equals(x.Profile.Id, winnerId, StringComparison.Ordinal);
            rankById.TryGetValue(x.Profile.Id, out var rank);
            return new Entry
            {
                ProfileId = x.Profile.Id,
                ProfileName = x.Profile.DisplayLabel,
                Priority = x.Profile.Priority,
                Matched = matched,
                IsWinner = isWinner,
                Rank = rank == 0 ? null : rank,
                Summary = x.Evaluation.Summary,
                Action = !string.IsNullOrWhiteSpace(x.Profile.DisplaySceneId)
                    ? x.Scene is null ? "Scene: missing" : $"Scene: {x.Scene.Name}"
                    : $"Primary: {x.Profile.TargetMonitorName}",
                WinnerReason = DescribeWinnerReason(x.Profile, settings.ProfileConflictRule, isWinner, matched, rank),
                Evaluation = x.Evaluation,
            };
        }).ToList();

        var winner = settings.Profiles.FirstOrDefault(p => string.Equals(p.Id, winnerId, StringComparison.Ordinal));
        return new Snapshot
        {
            ConflictRule = settings.ProfileConflictRule,
            WinnerProfileId = winner?.Id,
            WinnerProfileName = winner?.DisplayLabel,
            Profiles = entries,
            CapturedUtc = DateTime.UtcNow,
        };
    }

    public static string FormatReport(Snapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Conflict rule: {DescribeRule(snapshot.ConflictRule)}");
        builder.AppendLine(snapshot.WinnerProfileName is null
            ? "Winner: none"
            : $"Winner: {snapshot.WinnerProfileName}");
        builder.AppendLine();

        if (snapshot.Profiles.Count == 0)
        {
            builder.AppendLine("No profiles are configured.");
            return builder.ToString().TrimEnd();
        }

        foreach (var profile in snapshot.Profiles)
        {
            var state = profile.IsWinner ? "CONTROLLING" : profile.Matched ? "MATCHED · WAITING" : "NOT MATCHED";
            builder.AppendLine($"[{state}] {profile.ProfileName} · priority {profile.Priority}");
            builder.AppendLine($"  Action: {profile.Action}");
            builder.AppendLine($"  {profile.Summary}");
            if (!string.IsNullOrWhiteSpace(profile.WinnerReason))
            {
                builder.AppendLine($"  {profile.WinnerReason}");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeWinnerReason(
        AppProfile profile,
        ProfileConflictRule rule,
        bool isWinner,
        bool matched,
        int rank)
    {
        if (!matched)
        {
            return string.Empty;
        }
        if (isWinner)
        {
            return rule == ProfileConflictRule.HighestPriority
                ? "Selected because it ranks highest by priority; activation order breaks ties."
                : "Selected because it was activated most recently; priority breaks ties.";
        }

        return rank > 0
            ? $"Waiting at conflict rank {rank}; it will take over if profiles above it exit."
            : "Matched but not selected by the active conflict rule.";
    }

    private static string DescribeRule(ProfileConflictRule rule) => rule switch
    {
        ProfileConflictRule.MostRecentlyActivated => "most recently activated wins (priority breaks ties)",
        _ => "highest priority wins (activation order breaks ties)",
    };
}
