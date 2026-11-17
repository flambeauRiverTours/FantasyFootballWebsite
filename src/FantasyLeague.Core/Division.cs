namespace FantasyLeague.Core
{
    /// <summary>
    /// One of a head-to-head league's divisions (T72b).
    /// </summary>
    /// <param name="Number">Sleeper's division number, from 1.</param>
    /// <param name="Name">The commissioner's name for it, or "Division N" when they set none.</param>
    public sealed record Division(int Number, string Name);
}
