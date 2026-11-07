using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Schemata.Entity.Repository;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Scheduling.Foundation.Linking;
using Schemata.Scheduling.Skeleton;
using Schemata.Transport.Grpc.Linking;
using Grpc.Core;
using Grpc.Net.Client;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Transport.Grpc.Proto;
using Schemata.Transport.Grpc;
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
[Trait("Layer", "Integration")]
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
        job.Subject = job.CanonicalName;
        var execution = Execution(job);
        using (var scope = factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            database.SchemataJobs.Add(job);
            database.SchemataJobExecutions.Add(execution);
            database.SchemataJobExecutions.Add(Execution(Job()));
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
        Assert.EndsWith($"/v1/{job.CanonicalName}", Assert.Single(jobLinks, link => link.Title == "subject").Target);
        using var pageResponse = await client.GetAsync(executions.Target);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        using var page = JsonDocument.Parse(await pageResponse.Content.ReadAsStringAsync());
        Assert.Equal(execution.CanonicalName, Assert.Single(page.RootElement.GetProperty("operations").EnumerateArray()).GetProperty("name").GetString());

        var executionResponse = await client.GetAsync($"/v1/operations/{execution.Name}");
        Assert.Equal(HttpStatusCode.OK, executionResponse.StatusCode);
        var executionLinks = Links(executionResponse);
        Assert.Single(executionLinks, link => link.Rel == "self");
        var owner = Assert.Single(executionLinks, link => link.Rel == "related" && link.Title == "job");
        Assert.EndsWith($"/v1/{job.CanonicalName}", owner.Target);
        Assert.EndsWith($"/v1/{job.Subject}", Assert.Single(executionLinks, link => link.Title == "subject").Target);
    }

    [Fact]
    public async Task Missing_Job_Has_No_Link_Header() {
        using var factory = new Factory();
        var response      = await factory.CreateClient().GetAsync("/v1/jobs/missing-link-target");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(LinkHeader.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unregistered/subject")]
    public async Task Missing_Or_Unregistered_Subject_Emits_No_Subject_Link(string? subject) {
        using var factory = new Factory();
        var job = Job();
        job.Subject = subject;
        job.ArgsJson = "jobs/decoy";
        job.Variables = new() { ["subject"] = "jobs/decoy", ["tenant"] = "tenants/decoy" };
        var execution = Execution(job);
        execution.Output = JsonSerializer.Serialize("jobs/decoy");
        execution.MessageContext = new() { ["subject"] = "jobs/decoy" };
        using (var scope = factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            database.SchemataJobs.Add(job);
            database.SchemataJobExecutions.Add(execution);
            await database.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        Assert.DoesNotContain(Links(await client.GetAsync($"/v1/{job.CanonicalName}")), link => link.Title == "subject");
        var response = await client.GetAsync($"/v1/{execution.CanonicalName}");
        Assert.DoesNotContain(Links(response), link => link.Title == "subject");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metadata = json.RootElement.GetProperty("metadata");
        if (subject is null) {
            Assert.True(!metadata.TryGetProperty("subject", out var value) || value.ValueKind == JsonValueKind.Null);
        } else {
            Assert.Equal(subject, metadata.GetProperty("subject").GetString());
        }
    }

    [Fact]
    public void Derived_Resources_Project_Full_Rpcs_With_Required_Inputs() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<MyJob>(), []);
        registry.Add(new ResourceAttribute<MyExecution> { Operations = [Operations.Get, Operations.List] }, []);
        var contributor = new SchedulingLinkContributor<MyJob, MyExecution>(registry);
        var projector = new GrpcLinkProjector([new ResourceGrpcLinkTargetResolver(registry)]);
        var job = new MyJob { CanonicalName = "jobs/owner", Subject = "jobs/subject" };
        var jobContext = new LinkContext(new ResourceInstanceTarget(typeof(MyJob), job.CanonicalName), job, registry.GetResource(typeof(MyJob)));
        var jobLinks = contributor.GetLinks(jobContext).Select(link => projector.Project(link, jobContext)).ToArray();
        var executions = Assert.Single(jobLinks, link => link?.Title == "executions");
        Assert.NotNull(executions);
        Assert.Equal($"/{GrpcResourceNaming.ServiceFullName(typeof(MyExecution))}/ListOperations", executions.Target);
        Assert.Equal("job=\"jobs/owner\"", executions.Extensions?["filter"]);
        Assert.Equal($"/{GrpcResourceNaming.ServiceFullName(typeof(MyJob))}/GetJob", Assert.Single(jobLinks, link => link?.Title == "subject")?.Target);
        var operation = OperationMapper.FromExecution(new MyExecution { Job = job.CanonicalName, Subject = job.Subject });
        var executionContext = new LinkContext(new ResourceInstanceTarget(typeof(MyExecution), "operations/run"), operation, registry.GetResource(typeof(MyExecution)));
        var links = contributor.GetLinks(executionContext).Select(link => projector.Project(link, executionContext)).ToArray();
        var owner = Assert.Single(links, link => link?.Title == "job");
        Assert.Equal($"/{GrpcResourceNaming.ServiceFullName(typeof(MyJob))}/GetJob", owner?.Target);
        Assert.Equal("jobs/owner", owner?.Extensions?["name"]);
        Assert.Equal("jobs/subject", Assert.Single(links, link => link?.Title == "subject")?.Extensions?["name"]);
        var runContext = executionContext with {
            Target = new ResourceInstanceTarget(typeof(MyJob), job.CanonicalName, "run"),
            Resource = registry.GetResource(typeof(MyJob)),
        };
        var runLinks = contributor.GetLinks(runContext).Select(link => projector.Project(link, runContext)).ToArray();
        Assert.Equal("jobs/owner", Assert.Single(runLinks, link => link?.Title == "job")?.Extensions?["name"]);
        Assert.Equal("jobs/subject", Assert.Single(runLinks, link => link?.Title == "subject")?.Extensions?["name"]);
    }

    [Theory]
    [InlineData("unregistered")]
    [InlineData("disabled")]
    [InlineData("excluded")]
    public void Unavailable_Resources_Produce_No_Projected_Domain_Links(string reason) {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<MyJob> {
            Endpoints = reason == "disabled" ? [HttpResourceAttribute.Name] : null,
            Operations = reason == "excluded" ? [Operations.List] : null,
        }, []);
        if (reason != "unregistered") registry.Add(new ResourceAttribute<MyExecution> {
            Endpoints = reason == "disabled" ? [HttpResourceAttribute.Name] : null,
            Operations = reason == "excluded" ? [Operations.Get] : null,
        }, []);
        var job = new MyJob { CanonicalName = "jobs/owner", Subject = "missing/subject" };
        var context = new LinkContext(new ResourceInstanceTarget(typeof(MyJob), job.CanonicalName), job, registry.GetResource(typeof(MyJob)));
        var contributor = new SchedulingLinkContributor<MyJob, MyExecution>(registry);
        var projector = new GrpcLinkProjector([new ResourceGrpcLinkTargetResolver(registry)]);
        Assert.Empty(contributor.GetLinks(context).Select(link => projector.Project(link, context)).OfType<WebLink>());
    }


    [Theory]
    [InlineData(null)]
    [InlineData("jobs/subject")]
    public async Task Schedule_And_Trigger_Preserve_Subject_In_Fresh_Persisted_Projection(string? subject) {
        var fixture = new SchedulingFixture();
        await fixture.InitializeAsync();
        try {
            var scheduler = fixture.Services.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>();
            var job = new SchemataJob {
                Name = "subject", JobKey = BlockingJob.Key, Subject = subject,
                State = JobState.Active, ScheduleType = ScheduleType.OneTime,
                NextRunTime = fixture.Clock.Now.AddHours(1), ArgsJson = "jobs/decoy",
                Variables = new() { ["subject"] = "jobs/decoy", ["tenant"] = "tenants/decoy" },
            };
            await scheduler.ScheduleAsync(job, CancellationToken.None);
            Assert.Equal(subject, (await fixture.JobAsync("subject"))?.Subject);
            using (var scope = fixture.Services.CreateScope()) {
                var rows = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
                var row = await rows.FirstOrDefaultAsync(query => query.Where(value => value.Job == job.CanonicalName), CancellationToken.None);
                Assert.NotNull(row);
                var operation = OperationMapper.FromExecution(row);
                Assert.Equal(job.CanonicalName, operation.Metadata?.Job);
                Assert.Equal(subject, operation.Metadata?.Subject);
            }
            var triggered = await scheduler.TriggerAsync<BlockingJob>(new() { Job = job.CanonicalName }, CancellationToken.None);
            using (var scope = fixture.Services.CreateScope()) {
                var rows = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
                var row = await rows.FirstOrDefaultAsync(query => query.Where(value => value.CanonicalName == triggered.CanonicalName), CancellationToken.None);
                Assert.NotNull(row);
                Assert.Equal(subject, OperationMapper.FromExecution(row).Metadata?.Subject);
            }
            job.Subject = subject is null ? "jobs/updated" : null;
            await scheduler.ScheduleAsync(job, CancellationToken.None);
            Assert.Equal(job.Subject, (await fixture.JobAsync("subject"))?.Subject);
        } finally {
            await fixture.DisposeAsync();
        }
    }
    [Theory]
    [InlineData(null)]
    [InlineData("jobs/subject")]
    public async Task Grpc_Job_And_Execution_Trailers_Provide_Replayable_Rpc_Inputs(string? subject) {
        using var factory = new Factory();
        var job = Job();
        job.Subject = subject;
        var execution = Execution(job);
        using (var scope = factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            database.SchemataJobs.Add(job);
            database.SchemataJobExecutions.Add(execution);
            database.SchemataJobExecutions.Add(Execution(Job()));
            await database.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        using var channel = GrpcChannel.ForAddress(client.BaseAddress ?? new Uri("http://localhost"), new() { HttpClient = client });
        var invoker = channel.CreateCallInvoker();
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        foreach (var type in new[] { typeof(GetRequest), typeof(ListRequest), typeof(SchemataJob), typeof(Operation), typeof(ListResultBase<SchemataJobExecution, Operation>) })
            SchemataProtoModelConfigurator.ConfigureType(model, type);
        var getJob = new Method<GetRequest, SchemataJob>(MethodType.Unary, GrpcResourceNaming.ServiceFullName(typeof(SchemataJob)),
            "GetJob", GrpcMarshallers.Create<GetRequest>(model), GrpcMarshallers.Create<SchemataJob>(model));
        using var readJob = invoker.AsyncUnaryCall(getJob, null, new(), new GetRequest { CanonicalName = job.CanonicalName });
        Assert.Equal(job.Subject, (await readJob.ResponseAsync).Subject);
        var jobLinks = LinkHeader.Parse(readJob.GetTrailers().Single(entry => entry.Key == "link").Value);
        var executions = Assert.Single(jobLinks, link => link.Title == "executions");
        var list = new Method<ListRequest, ListResultBase<SchemataJobExecution, Operation>>(MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataJobExecution)), "ListOperations",
            GrpcMarshallers.Create<ListRequest>(model), GrpcMarshallers.Create<ListResultBase<SchemataJobExecution, Operation>>(model));
        Assert.Equal(list.FullName, executions.Target);
        Assert.Equal($"job=\"{job.CanonicalName}\"", executions.Extensions?["filter"]);
        using var filtered = invoker.AsyncUnaryCall(list, null, new(), new ListRequest { Filter = executions.Extensions?["filter"] });
        var page = await filtered.ResponseAsync;
        Assert.Equal(execution.CanonicalName, Assert.Single(page.Entities ?? []).CanonicalName);
        var getOperation = new Method<GetRequest, Operation>(MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataJobExecution)), "GetOperation",
            GrpcMarshallers.Create<GetRequest>(model), GrpcMarshallers.Create<Operation>(model));
        using var readExecution = invoker.AsyncUnaryCall(getOperation, null, new(), new GetRequest { CanonicalName = execution.CanonicalName });
        Assert.Equal(job.Subject, (await readExecution.ResponseAsync).Metadata?.Subject);
        var links = LinkHeader.Parse(readExecution.GetTrailers().Single(entry => entry.Key == "link").Value);
        var owner = Assert.Single(links, link => link.Title == "job");
        Assert.Equal(getJob.FullName, owner.Target);
        Assert.Equal(job.CanonicalName, owner.Extensions?["name"]);
        if (subject is null) {
            Assert.DoesNotContain(links, link => link.Title == "subject");
            Assert.DoesNotContain(jobLinks, link => link.Title == "subject");
        } else {
            var subjectLink = Assert.Single(links, link => link.Title == "subject");
            Assert.Equal(getJob.FullName, subjectLink.Target);
            Assert.Equal(subject, subjectLink.Extensions?["name"]);
            Assert.Equal(subject, Assert.Single(jobLinks, link => link.Title == "subject").Extensions?["name"]);
        }
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
            Subject       = job.Subject,
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
