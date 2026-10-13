namespace FantasyLeague.Seasons
{
    /// <summary>
    /// The §09 response body for <c>/api/seasons</c>.
    /// </summary>
    /// <param name="Current">The live season.</param>
    /// <param name="Available">Every season that can be served, newest first (D8, A34).</param>
    public sealed record SeasonsPayload(
        int Current,
        IReadOnlyList<int> Available);
}
