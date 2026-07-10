using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Pure winner-selection logic for simultaneously active profiles.</summary>
public static class ProfileConflictResolver
{
    public sealed record Candidate(AppProfile Profile, long ActivationOrder, string? DetectedLauncherChild = null);

    public static Candidate? SelectWinner(
        IEnumerable<Candidate> candidates,
        ProfileConflictRule rule)
        => OrderCandidates(candidates, rule).FirstOrDefault();

    public static IEnumerable<Candidate> OrderCandidates(
        IEnumerable<Candidate> candidates,
        ProfileConflictRule rule)
    {
        var source = candidates ?? Enumerable.Empty<Candidate>();

        return rule switch
        {
            ProfileConflictRule.MostRecentlyActivated => source
                .OrderByDescending(c => c.ActivationOrder)
                .ThenByDescending(c => c.Profile.Priority)
                .ThenBy(c => c.Profile.Id, StringComparer.Ordinal),

            _ => source
                .OrderByDescending(c => c.Profile.Priority)
                .ThenByDescending(c => c.ActivationOrder)
                .ThenBy(c => c.Profile.Id, StringComparer.Ordinal),
        };
    }
}
