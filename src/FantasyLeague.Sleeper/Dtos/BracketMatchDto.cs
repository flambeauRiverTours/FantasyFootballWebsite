using System.Text.Json;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One entry of <c>/v1/league/{id}/winners_bracket</c> (T69).
    /// </summary>
    /// <param name="R">The round.</param>
    /// <param name="M">The match id.</param>
    /// <param name="T1">A roster id, or a reference such as <c>{"w":1}</c>, or null: read tolerantly (A46).</param>
    /// <param name="T2">As <paramref name="T1"/>.</param>
    /// <param name="W">The winner's roster id once settled.</param>
    /// <param name="L">The loser's roster id once settled.</param>
    /// <param name="T1From"><c>{"w": m}</c> or <c>{"l": m}</c>: where the first side comes from.</param>
    /// <param name="T2From">As <paramref name="T1From"/>.</param>
    /// <param name="P">The place the match decides; undocumented, absent for none.</param>
    internal sealed record BracketMatchDto(
        int R,
        int M,
        JsonElement? T1,
        JsonElement? T2,
        int? W,
        int? L,
        JsonElement? T1From,
        JsonElement? T2From,
        int? P);
}
