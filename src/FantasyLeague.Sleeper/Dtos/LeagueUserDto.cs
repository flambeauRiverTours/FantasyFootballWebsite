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
    /// The design says a manager who never set a team name has an empty string
    /// rather than a missing key. That is not what this league returns: for two of
    /// six managers the <c>team_name</c> key is absent from <c>metadata</c>
    /// altogether. Resolution has to handle absent, null and empty alike — see
    /// <see cref="SleeperMapper.ResolveTeamName"/>.
    /// </remarks>
    internal sealed record LeagueUserMetadataDto(string? TeamName);
}
