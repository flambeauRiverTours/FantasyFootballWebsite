using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FantasyLeague.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    internal sealed class FailingContainer(int status) : BlobContainerClient
    {
        public override BlobClient GetBlobClient(string blobName) => new FailingBlob(status);

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new RequestFailedException(status, "failed");
    }

    internal sealed class FailingBlob(int status) : BlobClient
    {
        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new RequestFailedException(status, "failed");
    }

    /// <summary>
    /// A container that keeps its blobs in memory, for the paths that succeed.
    /// </summary>
    internal sealed class MemoryContainer : BlobContainerClient
    {
        public Dictionary<string, byte[]> Blobs { get; } = [];

        public override BlobClient GetBlobClient(string blobName) => new MemoryBlob(Blobs, blobName);

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Response<BlobContainerInfo>>(null!);
    }

    internal sealed class MemoryBlob(Dictionary<string, byte[]> blobs, string name) : BlobClient
    {
        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions? options = null, CancellationToken cancellationToken = default) =>
            blobs.TryGetValue(name, out var bytes)
                ? Task.FromResult(Response.FromValue(BlobsModelFactory.BlobDownloadStreamingResult(new MemoryStream(bytes)), null!))
                : throw new RequestFailedException(404, "not found");

        public override async Task<Response<BlobContentInfo>> UploadAsync(
            Stream content, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            blobs[name] = copy.ToArray();
            return null!;
        }
    }

    public class BlobStoreFailureTests
    {
        private static BlobFinalizedWeekStore SeasonStore(int status) =>
            new(new FailingContainer(status), TimeProvider.System, NullLogger<BlobFinalizedWeekStore>.Instance);

        private static BlobPlayerMapStore PlayerMapStore(int status) =>
            new(new FailingContainer(status), "players.json", NullLogger<BlobPlayerMapStore>.Instance);

        private static TeamWeek Week() =>
            new(1, 1, 100m, 120m, [], [], [], true, new Dictionary<string, decimal>());

        [Theory]
        [InlineData(0)]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(503)]
        public async Task A_transient_season_read_failure_falls_back(int status) => (await SeasonStore(status).ReadAsync("L1", 2026, TestContext.Current.CancellationToken)).Should().BeNull();

        [Fact]
        public async Task An_unauthorised_season_read_throws_rather_than_falling_back()
        {
            await SeasonStore(403).Invoking(s => s.ReadAsync("L1", 2026, default))
                .Should().ThrowAsync<RequestFailedException>();
        }

        [Theory]
        [InlineData(503)]
        [InlineData(403)]
        public async Task A_failed_season_write_never_fails_the_caller(int status)
        {
            await SeasonStore(status).Invoking(s => s.WriteAsync("L1", 2026, [Week()], default))
                .Should().NotThrowAsync();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(503)]
        public async Task A_transient_player_map_read_failure_falls_back(int status) => (await PlayerMapStore(status).ReadAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        [Fact]
        public async Task An_unauthorised_player_map_read_throws_rather_than_falling_back()
        {
            await PlayerMapStore(403).Invoking(s => s.ReadAsync(default))
                .Should().ThrowAsync<RequestFailedException>();
        }

        [Theory]
        [InlineData(503)]
        [InlineData(403)]
        public async Task A_failed_player_map_write_never_fails_the_caller(int status)
        {
            var map = new StoredPlayerMap(DateTimeOffset.UnixEpoch, new Dictionary<string, PlayerRef>());

            await PlayerMapStore(status).Invoking(s => s.WriteAsync(map, default))
                .Should().NotThrowAsync();
        }
    }
    public class BlobPlayerMapStoreTests
    {
        [Fact]
        public async Task A_stored_map_reads_back()
        {
            var store = new BlobPlayerMapStore(new MemoryContainer(), "players.json", NullLogger<BlobPlayerMapStore>.Instance);
            var map = new StoredPlayerMap(
                DateTimeOffset.UnixEpoch,
                new Dictionary<string, PlayerRef> { ["4046"] = new("Patrick Mahomes", "KC", ["QB"]) });

            await store.WriteAsync(map, TestContext.Current.CancellationToken);
            var read = await store.ReadAsync(TestContext.Current.CancellationToken);

            read!.Players["4046"].FantasyPositions.Should().Equal("QB");
        }

        [Fact]
        public async Task Nothing_stored_reads_back_null() => (await new BlobPlayerMapStore(new MemoryContainer(), "players.json", NullLogger<BlobPlayerMapStore>.Instance).ReadAsync(TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
