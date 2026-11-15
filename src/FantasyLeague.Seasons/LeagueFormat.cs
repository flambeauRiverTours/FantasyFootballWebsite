using System.Text.Json.Serialization;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// How the league decides its winner (A38). Every season, past ones included,
    /// is shown in the deployment's format (Q10).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LeagueFormat>))]
    public enum LeagueFormat
    {
        /// <summary>The most points over the regular season wins. The table only.</summary>
        TotalPoints,

        /// <summary>Weekly matchups decide a record, then a playoff bracket the champion.</summary>
        HeadToHead,
    }
}
