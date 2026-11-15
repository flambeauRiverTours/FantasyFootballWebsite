using System.Text.Json;
using FantasyLeague.Sleeper;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The captured Sleeper payloads in <c>tests/fixtures</c>, read as the app reads Sleeper.
    /// </summary>
    internal static class Fixtures
    {
        public static string Text(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

        public static T Read<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, SleeperJson.Options)!;
    }
}
