using System.Text.Json;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/league/{id}</c> — the league's own settings.
    /// </summary>
    /// <param name="Name">The league's display name.</param>
    /// <param name="RosterPositions">Every slot, startable and bench, one entry per slot. The startable ones drive both the solver and the radar axes, and this is per-season data (A3).</param>
    /// <param name="Season">The season this league plays. Sent as a string; binds through <see cref="SleeperJson.Options"/>.</param>
    /// <param name="PreviousLeagueId">The league this one rolled over from, or null for a first season (A4). The season chain walks it (D8).</param>
    /// <param name="Settings">The league's settings, of which the playoff start and the median game are read.</param>
    /// <param name="Metadata">
    /// Free-form commissioner and app data, of which only the division names are read (T72b).
    /// Kept as one raw value: Sleeper promises neither an object nor string values, and neither may fail the read.
    /// </param>
    internal sealed record LeagueDto(
        string? Name,
        IReadOnlyList<string>? RosterPositions,
        int Season,
        string? PreviousLeagueId,
        LeagueSettingsDto? Settings,
        JsonElement? Metadata = null);

    /// <summary>
    /// The part of a league's <c>settings</c> object this app reads.
    /// </summary>
    /// <param name="PlayoffWeekStart">The first playoff week. Zero, or absent, when the league has no playoffs.</param>
    /// <param name="LeagueAverageMatch">1 when the league plays a median game each week (Q12), else 0 or absent.</param>
    /// <param name="PlayoffRoundType">How many weeks each playoff round takes (T69); see <see cref="Core.Playoffs.RoundWeeks"/>.</param>
    /// <param name="Divisions">How many divisions the league has (T72b). Absent, 0 or 1 means none.</param>
    internal sealed record LeagueSettingsDto(int? PlayoffWeekStart, int? LeagueAverageMatch, int? PlayoffRoundType, int? Divisions = null);
}
