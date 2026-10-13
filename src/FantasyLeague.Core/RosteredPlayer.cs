using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyLeague.Core
{
    /// <summary>
    /// One player on a roster in one week, as the solver sees them.
    /// </summary>
    /// <param name="Id">Sleeper's player id.</param>
    /// <param name="Name">The player's name.</param>
    /// <param name="FantasyPositions">Every position the player may start at, which is what flex eligibility checks.</param>
    /// <param name="Points">What the player scored that week, zero when Sleeper has no entry.</param>
    public sealed record RosteredPlayer(
        PlayerId Id,
        string Name,
        IReadOnlyList<string> FantasyPositions,
        decimal Points);

    /// <summary>
    /// Sleeper's player id: numeric for most players, a team abbreviation such as <c>DEN</c> for a defence.
    /// </summary>
    /// <param name="Value">The id.</param>
    /// <remarks>
    /// Serialized as the bare string, so the blob, the SQL row and the cache all
    /// store a lineup as <c>["4046","BAL"]</c> rather than as wrapped objects.
    /// </remarks>
    [JsonConverter(typeof(PlayerIdJsonConverter))]
    public readonly record struct PlayerId(string Value);

    /// <summary>
    /// Reads and writes a <see cref="PlayerId"/> as its string.
    /// </summary>
    public sealed class PlayerIdJsonConverter : JsonConverter<PlayerId>
    {
        /// <inheritdoc />
        public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? throw new JsonException("A player id cannot be null."));

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
