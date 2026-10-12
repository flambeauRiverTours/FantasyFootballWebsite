using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Core.Tests
{
    public class LeagueOptionsBindingTests
    {
        private static ServiceProvider Provider(params (string Key, string? Value)[] settings)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSleeperClient();

            return services.BuildServiceProvider();
        }

        private static LeagueOptions Resolve(ServiceProvider provider) =>
            provider.GetRequiredService<IOptions<LeagueOptions>>().Value;

        [Fact]
        public void The_league_id_binds_from_the_configured_section()
        {
            using var provider = Provider(("League:CurrentLeagueId", "1234567890123456789"));

            Resolve(provider).CurrentLeagueId.Should().Be("1234567890123456789");
        }

        [Fact]
        public void A_nineteen_digit_id_survives_as_a_string()
        {
            // parsing it would overflow an int by six orders of magnitude
            using var provider = Provider(("League:CurrentLeagueId", "1234567890123456789"));

            Resolve(provider).CurrentLeagueId.Should().HaveLength(19);
        }

        [Fact]
        public void The_double_underscore_form_binds_the_same_way()
        {
            // how it is written in local.settings.json and in an Azure App Setting
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .AddEnvironmentVariables()
                .Build();

            Environment.SetEnvironmentVariable("League__CurrentLeagueId", "env-league");
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder().AddEnvironmentVariables().Build());
                services.AddLogging();
                services.AddSleeperClient();

                using var provider = services.BuildServiceProvider();
                Resolve(provider).CurrentLeagueId.Should().Be("env-league");
            }
            finally
            {
                Environment.SetEnvironmentVariable("League__CurrentLeagueId", null);
            }
        }

        [Fact]
        public void A_missing_league_id_fails_validation()
        {
            // ValidateOnStart turns this into a failed deployment rather than a
            // 500 for whoever opens the page first
            using var provider = Provider();

            FluentActions.Invoking(() => Resolve(provider))
                .Should().Throw<OptionsValidationException>();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_league_id_fails_validation(string value)
        {
            using var provider = Provider(("League:CurrentLeagueId", value));

            FluentActions.Invoking(() => Resolve(provider))
                .Should().Throw<OptionsValidationException>();
        }

        [Fact]
        public void The_wrong_section_name_does_not_bind()
        {
            using var provider = Provider(("Leagues:CurrentLeagueId", "wrong-section"));

            FluentActions.Invoking(() => Resolve(provider))
                .Should().Throw<OptionsValidationException>();
        }

        [Fact]
        public void The_wrong_key_name_does_not_bind()
        {
            using var provider = Provider(("League:LeagueId", "wrong-key"));

            FluentActions.Invoking(() => Resolve(provider))
                .Should().Throw<OptionsValidationException>();
        }
    }

    public class HostCompositionTests
    {
        private static ServiceProvider Provider(bool validate = true)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["League:CurrentLeagueId"] = "1234567890123456789",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSleeperClient();

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = validate,
                ValidateScopes = validate,
            });
        }

        [Fact]
        public void The_container_validates_on_build() =>
            // catches a missing registration or a singleton capturing something
            // scoped, at test time rather than at the first request
            FluentActions.Invoking(() => Provider()).Should().NotThrow();

        [Theory]
        [InlineData(typeof(ISeasonService))]
        [InlineData(typeof(IPlayerMapRefresher))]
        [InlineData(typeof(IHttpClientFactory))]
        [InlineData(typeof(IOptions<LeagueOptions>))]
        public void Every_service_the_host_needs_resolves(Type service)
        {
            using var provider = Provider();

            provider.GetRequiredService(service).Should().NotBeNull();
        }

        [Fact]
        public void The_player_map_is_shared_across_resolutions()
        {
            using var provider = Provider();

            provider.GetRequiredService<IPlayerMapRefresher>()
                .Should().BeSameAs(provider.GetRequiredService<IPlayerMapRefresher>());
        }

        [Fact]
        public void The_season_service_is_not_shared()
        {
            using var provider = Provider();

            provider.GetRequiredService<ISeasonService>()
                .Should().NotBeSameAs(provider.GetRequiredService<ISeasonService>());
        }

        [Fact]
        public void Resolving_from_a_scope_works_as_it_does_per_invocation()
        {
            using var provider = Provider();
            using var scope = provider.CreateScope();

            scope.ServiceProvider.GetRequiredService<ISeasonService>().Should().NotBeNull();
            scope.ServiceProvider.GetRequiredService<IPlayerMapRefresher>()
                .Should().BeSameAs(provider.GetRequiredService<IPlayerMapRefresher>());
        }

        [Fact]
        public void The_sleeper_client_keeps_its_base_address()
        {
            using var provider = Provider();

            provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(SleeperClient))
                .BaseAddress.Should().Be(new Uri("https://api.sleeper.app/v1/"));
        }

        [Fact]
        public void Registering_twice_does_not_duplicate_the_bucket()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["League:CurrentLeagueId"] = "L1",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSleeperClient();
            services.AddSleeperClient();

            using var provider = services.BuildServiceProvider();

            provider.GetServices<System.Threading.RateLimiting.RateLimiter>()
                .Should().ContainSingle();
        }
    }
}
