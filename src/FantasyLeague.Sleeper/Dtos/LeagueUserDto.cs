namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One entry from <c>/v1/league/{id}/users</c>.
    /// </summary>
    /// <remarks>
    /// Every field here is personal data. A capture of this endpoint must never be
    /// committed as a fixture (A2).
    /// </remarks>
    /// <param name="UserId">The only identifier stable across seasons, and therefore what A1's exclusion list matches on.</param>
    /// <param name="DisplayName">The manager's Sleeper handle. The fallback when no team name is set.</param>
    /// <param name="Metadata">Per-league user settings, including the team name. Can be null.</param>
    internal sealed record LeagueUserDto(
        string UserId,
        string? DisplayName,
        LeagueUserMetadataDto? Metadata);

    /// <summary>
    /// The <c>metadata</c> object on a league user.
    /// </summary>
    /// <param name="TeamName">
    /// The manager's chosen team name, or null.
    /// </param>
    /// <remarks>
    /// Absent when the manager never set a name (A6); see
    /// <see cref="SleeperMapper.ResolveTeamName"/>.
    /// </remarks>
    internal sealed record LeagueUserMetadataDto(string? TeamName);
}
