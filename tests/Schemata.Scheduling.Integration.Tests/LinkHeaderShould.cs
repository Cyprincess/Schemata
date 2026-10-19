using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Linking;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
        }
    }

    [Fact]
    public async Task Job_And_Execution_Expose_Relation_Links() {
        using var factory = new Factory();
        var job       = Job();
        var execution = Execution(job);
        using (var scope = factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            database.SchemataJobs.Add(job);
            database.SchemataJobExecutions.Add(execution);
            await database.SaveChangesAsync();
        }

        var client = factory.CreateClient();

        var jobResponse = await client.GetAsync($"/v1/jobs/{job.Name}");
        Assert.Equal(HttpStatusCode.OK, jobResponse.StatusCode);
        var jobLinks = Links(jobResponse);
        Assert.Single(jobLinks, link => link.Rel == "self");
        var executions = Assert.Single(jobLinks, link => link.Rel == "related" && link.Title == "executions");
        Assert.Contains("/v1/operations", executions.Target);
        var filter = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(executions.Target).Query);
        Assert.Equal($"job=\"{job.CanonicalName}\"", filter["filter"].ToString());

        var executionResponse = await client.GetAsync($"/v1/operations/{execution.Name}");
        Assert.Equal(HttpStatusCode.OK, executionResponse.StatusCode);
        var executionLinks = Links(executionResponse);
        Assert.Single(executionLinks, link => link.Rel == "self");
        var owner = Assert.Single(executionLinks, link => link.Rel == "related" && link.Title == "job");
        Assert.EndsWith($"/v1/{job.CanonicalName}", owner.Target);
    }

    [Fact]
    public async Task Missing_Job_Has_No_Link_Header() {
        using var factory = new Factory();
        var response      = await factory.CreateClient().GetAsync("/v1/jobs/missing-link-target");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(LinkHeader.Name));
    }

    private static SchemataJob Job() {
        var name = $"link-{Guid.NewGuid():n}";
        return new() {
            Uid           = Guid.NewGuid(),
            Name          = name,
            CanonicalName = $"jobs/{name}",
            Key           = name,
            JobKey        = "link-test",
            State         = JobState.Active,
            ScheduleType  = ScheduleType.OneTime,
            NextRunTime   = DateTime.UtcNow.AddDays(1),
        };
    }

    private static SchemataJobExecution Execution(SchemataJob job) {
        var name = $"link-{Guid.NewGuid():n}";
        return new() {
            Uid           = Guid.NewGuid(),
            Name          = name,
            CanonicalName = $"operations/{name}",
            Job           = job.CanonicalName,
            State         = ExecutionState.Succeeded,
            StartTime     = DateTime.UtcNow.AddMinutes(-1),
            EndTime       = DateTime.UtcNow,
        };
    }

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        Assert.True(response.Headers.TryGetValues(LinkHeader.Name, out var values), "The response carries no Link header.");
        return LinkHeader.Parse(string.Join(", ", values), response.RequestMessage!.RequestUri);
    }
}
