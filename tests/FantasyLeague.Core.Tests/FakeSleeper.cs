using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Sleeper without a network. Records every request that reaches the socket,
    /// optionally slowly so concurrent callers genuinely overlap, and answers with
    /// the smallest body that binds for the path unless told otherwise.
    /// </summary>
    /// <param name="respond">The answer to every request, in place of the smallest body.</param>
    /// <param name="delay">How long each request takes, honouring cancellation.</param>
    internal sealed class FakeSleeper(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        TimeSpan delay = default) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<HttpRequestMessage> requests = new();

        public IReadOnlyList<HttpRequestMessage> Requests => [.. requests];

        public int Attempts => requests.Count;

        /// <summary>A fake that gives every request the same answer.</summary>
        public static FakeSleeper Answering(HttpStatusCode status, string body = "{}", TimeSpan delay = default) =>
            new(_ => Json(status, body), delay);

        public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (respond ?? SmallestBody)(request);
        }

        /// <summary>The least JSON that binds for the type each path returns.</summary>
        private static HttpResponseMessage SmallestBody(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body =
                path.EndsWith("state/nfl", StringComparison.Ordinal) ? """{"season":"2026","week":1}""" :
                path.EndsWith("players/nfl", StringComparison.Ordinal) ? "{}" :
                path.Contains("/users", StringComparison.Ordinal) ||
                path.Contains("/rosters", StringComparison.Ordinal) ||
                path.Contains("/matchups/", StringComparison.Ordinal) ||
                path.EndsWith("/winners_bracket", StringComparison.Ordinal) ? "[]" :
                """{"league_id":"L1","season":"2026","roster_positions":["QB","BN"]}""";

            return Json(HttpStatusCode.OK, body);
        }
    }
}
