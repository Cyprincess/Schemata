using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Authorization.Tests
{
    public class BackChannelLogoutFeatureShould
    {
        [Fact]
        public void UseBackChannelLogout_DoesNotRegisterSchedulingFeature() {
            var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
            builder.UseAuthorization().UseBackChannelLogout();

            Assert.False(builder.Options.HasFeature<SchemataSchedulingFeature>());
        }

        [Fact]
        public void ConfigureServices_WithoutScheduling_DeclaresBackChannelLogoutJob() {
            var services = new ServiceCollection();
            var options  = new SchemataOptions();
            var feature  = new BackChannelLogoutFeature<SchemataApplication, SchemataToken>();

            feature.ConfigureServices(services, options, new Configurators());

            Assert.False(options.HasFeature<SchemataSchedulingFeature>());
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(BackChannelLogoutJob));

            using var provider = services.BuildServiceProvider();
            var registration = Assert.Single(
                provider.GetRequiredService<IOptions<SchemataSchedulingOptions>>().Value.Jobs,
                job => job.JobType == typeof(BackChannelLogoutJob));

            Assert.Null(registration.Schedule);
        }

        [Fact]
        public void ConfigureServices_WithoutScheduling_DeclaresScheduledTokenCleanupJob() {
            var services = new ServiceCollection();

            ConfigureAuthorization(services, new SchemataOptions());

            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TokenCleanupJob<SchemataToken>));

            using var provider = services.BuildServiceProvider();
            var registration = Assert.Single(
                provider.GetRequiredService<IOptions<SchemataSchedulingOptions>>().Value.Jobs,
                job => job.JobType == typeof(TokenCleanupJob<SchemataToken>));
            var schedule = Assert.IsType<CronSchedule>(registration.Schedule);

            Assert.Equal("0 * * * *", schedule.Expression);
        }

        private static void ConfigureAuthorization(IServiceCollection services, SchemataOptions options) {
            new SchemataAuthorizationFeature<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken>()
                .ConfigureServices(services, options, new Configurators(), new ConfigurationBuilder().Build(), null!);
        }
    }
}
