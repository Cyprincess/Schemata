# Publish a Resource with a Business Action

This cookbook is for an application developer adding the `publish` transition to an existing public `Course` resource. It uses one instance-scoped custom method, one handler as the business owner, and the resource pipeline for authorization, request replay, and freshness checks.

## What you'll build

`publish` moves one course from `draft` to `published`. It is not an ordinary Update: the request has one business meaning, permits one transition, and must fail when the course is already published. The public action is a custom method under [AIP-136](https://google.aip.dev/136), which requires a mutating action to use `POST` and a colon-delimited verb in its HTTP path.

The transition is synchronous and single-step, so the custom-method handler owns it. A Flow state machine would persist process, token, and transition rows for a lifecycle with no waits, branching, compensation, or external events, and would move the transition out of its single business owner. Flow becomes the owner when the process itself needs durable multi-step state; see [AIP Business Logic](../documents/resource/aip-business-logic.md).

| Concern | Owner | Status |
| --- | --- | --- |
| `draft` to `published` precondition and persistence | `PublishCourseHandler` | Application responsibility |
| Route target, authorization, request replay, and freshness gate | Resource operation and its advisors | Supported by extension point |
| Timestamp rotation for this resource's ETag | `CourseConcurrencyAdvisor` | Application responsibility |
| HTTP JSON and gRPC protobuf projection | Registered resource transports | Enforced |

## Prerequisites

- An ASP.NET Core application using Schemata resources and an EF Core repository.
- `Schemata.Resource.Foundation`, `Schemata.Resource.Http`, `Schemata.Resource.Grpc`, `Schemata.Security.Foundation`, `Schemata.Caching.Distributed`, `Schemata.Mapping.Mapster`, and `Schemata.Entity.EntityFrameworkCore`.
- An authorization policy implemented through `IAccessProvider<Course, PublishCourseRequest>` and, when row-level visibility is required, `IEntitlementProvider<Course, PublishCourseRequest>`.

## Step 1: Model the resource and action contract

Keep the persistent resource state on `Course`, use a dedicated custom-method request, and return the normal detail representation. `CourseDetail` is a static response shape; it is not AIP-157 partial response.

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Security.Skeleton;

[global::Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[ResourceMethod("publish", typeof(PublishCourseHandler), ResourceMethodScope.Instance)]
[CanonicalName("courses/{course}")]
public sealed class Course : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public Guid Uid { get; set; }
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }

    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
    public string? Title { get; set; }
    public CoursePublicationState State { get; set; }
}

public enum CoursePublicationState
{
    StateUnspecified,
    Draft,
    Published,
}

public sealed class CourseRequest : ICanonicalName
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? Title { get; set; }
}

public sealed class CourseDetail : ICanonicalName, IFreshness
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? EntityTag { get; set; }
    public string? Title { get; set; }
    public CoursePublicationState State { get; set; }
}

public sealed class CourseSummary : ICanonicalName
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? Title { get; set; }
    public CoursePublicationState State { get; set; }
}

public sealed class PublishCourseRequest : ICommand<CourseDetail>, ICanonicalName, IFreshness,
    IRequestIdentification, IRequestPrincipal
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? EntityTag { get; set; }
    public string? RequestId { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed class CoursePublishAccessProvider : IAccessProvider<Course, PublishCourseRequest>
{
    public Task<bool> HasAccessAsync(
        Course?                             course,
        AccessContext<PublishCourseRequest> context,
        ClaimsPrincipal?                    principal,
        CancellationToken                   ct = default)
    {
        return Task.FromResult(
            principal?.Identity?.IsAuthenticated == true
         && principal.HasClaim("permission", "course.publish"));
    }
}
```

`ResourceMethodAttribute` requires a handler implementing `IRequestHandler<TRequest, TResponse>`. Resource registration also requires its request to implement `IRequestPrincipal` and its response to implement `ICanonicalName`. `ICommand<CourseDetail>` supplies the dispatcher request shape. `IFreshness` and `IRequestIdentification` activate their advisor lanes when `EntityTag` or `RequestId` is nonempty.

The instance route is the authoritative target. The operation handler overwrites `PublishCourseRequest.CanonicalName` from the route before dispatch, so a body cannot select another course.

## Step 2: Put the transition in one handler
The resource pipeline first loads the target for instance authorization and freshness. The current custom-method dispatcher passes only the request to the business handler, so the handler performs a second repository read to own the transition and persistence. No controller, advisor, job, or Flow duplicates the publication decision.

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;

public sealed class PublishCourseHandler(IRepository<Course> courses)
    : IRequestHandler<PublishCourseRequest, CourseDetail>
{
    public async Task<CourseDetail> HandleAsync(
        PublishCourseRequest request,
        CancellationToken    ct = default)
    {
        var course = await courses.SingleOrDefaultAsync(
            query => query.Where(candidate => candidate.CanonicalName == request.CanonicalName),
            ct);

        if (course is null)
        {
            throw SchemataResourceErrors.NotFound<Course>(request.CanonicalName);
        }

        if (course.State != CoursePublicationState.Draft)
        {
            throw SchemataResourceErrors.PreconditionFailed<Course>(
                course.CanonicalName,
                subject: "state",
                description: "Only draft courses can be published.",
                reason: "COURSE_NOT_DRAFT");
        }

        course.State = CoursePublicationState.Published;
        await courses.UpdateAsync(course, ct);
        await courses.CommitAsync(ct);

        return new CourseDetail {
            Name          = course.Name,
            CanonicalName = course.CanonicalName,
            Title         = course.Title,
            State         = course.State,
        };
    }
}
```

`UpdateAsync` runs repository update advisors and `CommitAsync` persists pending changes. `SchemataResourceErrors.PreconditionFailed<Course>` creates a `FAILED_PRECONDITION` exception with resource and precondition details. The handler is the only code that decides whether `publish` is a legal state transition.

## Step 3: Rotate the concurrency token on every Course update

`AdviceMethodFreshness` compares a supplied `EntityTag` with the target entity's `IConcurrency.Timestamp`. The framework creates a timestamp during an add, but its built-in update advisor updates `ITimestamp.UpdateTime`, not `IConcurrency.Timestamp`. Add a repository update advisor so a successful `Course` write changes the weak ETag value used by the next action.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class CourseConcurrencyAdvisor : IRepositoryUpdateAdvisor<Course>
{
    public int Order => global::Schemata.Abstractions.SchemataConstants.Orders.Max + 10_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext       ctx,
        IRepository<Course> repository,
        Course              course,
        CancellationToken   ct = default)
    {
        course.Timestamp = Identifiers.NewUid();
        return Task.FromResult(AdviseResult.Continue);
    }
}
```

This advisor establishes the resource's persistence rule for all `Course` updates, including writes that do not arrive through `:publish`. It runs after the reserved built-in advisor range and contains no publication decision.

The freshness comparison is a resource-advisor check before dispatch, rather than an atomic provider-level compare-and-swap. A service requiring an atomic state-and-version write must implement that provider-specific invariant at its repository boundary.

## Step 4: Register persistence, mapping, policy lanes, and transports

Configure the repository, concurrency advisor, cache provider for request replay, Mapster mappings for CRUD, the closed publish access policy, and both transports before registering `Course`.
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Security.Skeleton;
var builder = WebApplication.CreateBuilder(args);

builder.UseSchemata(schema => {
    schema.UseControllers();
    schema.UseJsonSerializer();
    schema.UseSecurity();

    schema.UseMapster()
          .Map<CourseRequest, Course>()
          .Map<Course, CourseDetail>()
          .Map<Course, CourseSummary>();

    schema.ConfigureServices(services => {
        services.AddDistributedMemoryCache();
        services.AddDistributedCache();

        services.AddRepository<Course, EfCoreRepository<AppDbContext, Course>>()
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, options) => options.UseSqlite("Data Source=courses.db"));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryUpdateAdvisor<Course>, CourseConcurrencyAdvisor>());
        services.AddScoped<IAccessProvider<Course, PublishCourseRequest>, CoursePublishAccessProvider>();
    });

    schema.UseResource()
          .WithAuthorization()
          .MapHttp()
          .MapGrpc()
          .AddResource<Course>();
});

var app = builder.Build();
app.Run();

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
}
```

`WithAuthorization()` is required. It adds the coarse operation check at the dispatcher boundary and registers the access and entitlement advisor families for this resource's verbs, including the instance-stage access check on the loaded entity. `UseSecurity()` supplies default permission resolution and open-generic access and entitlement providers; the closed `CoursePublishAccessProvider` registration supplies the `publish` policy used by this action. The default entitlement provider leaves the target query unfiltered; replace it with a closed `IEntitlementProvider<Course, PublishCourseRequest>` when row-level visibility is required.
`AddResource<Course>()` reads the `[Resource]` and `[ResourceMethod]` attributes, registers the closed handler interface as scoped, and registers method idempotency and freshness advisors because `PublishCourseRequest` implements `ICanonicalName`. Idempotency then activates only when the request also implements `IRequestIdentification` and carries a nonempty `request_id`. The cache key includes resource type, lower-camel verb, caller, canonical name, request ID, and payload hash. An exact completed replay returns the cached detail; a competing unresolved reservation returns `ABORTED`. A changed payload produces a different cache key, so the application must validate any stricter request-ID reuse policy.

Authorization executes before persistence. The entitlement advisor filters the target query through the entitlement provider before rows load; the access advisor checks the `publish` operation against the caller, and the instance-stage advisor checks the loaded entity. Freshness and idempotency wraps run before the handler. These advisors are policy lanes, not alternate publication implementations.

## Step 5: Call the final wire contract

The internal route target reaches `ResourceMethodOperationHandler<Course, PublishCourseRequest, CourseDetail>`. It creates a `ResourceMethodRequest` carrying the verb, target, payload, and principal; dispatcher wraps run policy, then the Resource method dispatch handler loads the instance and invokes `PublishCourseHandler`.

### HTTP JSON

`MapHttp()` creates this endpoint:

```text
POST /v1/courses/{name}:publish
```

A client sends the route-relative leaf name and a bare request body:

```http
POST /v1/courses/course-42:publish
Content-Type: application/json

{
  "etag": "W/\"previous-tag\"",
  "request_id": "d6bd02c6-88f1-4740-a11c-70a92667781b"
}
```

The HTTP resource controller builds the canonical target `courses/course-42`. `SchemataJsonTraits` hides CLR `Name`, writes `CanonicalName` as `name`, and writes `EntityTag` as `etag`; the configured HTTP serializer writes remaining properties in `snake_case`, enums in `kebab-case`, and omits nulls. The response from this handler is a bare `CourseDetail`:

```json
{
  "name": "courses/course-42",
  "title": "Relational Design",
  "state": "published"
}
```

The custom-method response advisor receives no entity from `ResourceMethodOperationHandler`, so it does not calculate a new detail ETag for this response. Read the course afterward when the client needs the newly rotated ETag.

### gRPC protobuf

`MapGrpc()` registers a unary `PublishCourse` RPC on the generated `CourseService`. It receives the declared `PublishCourseRequest` and returns the declared `CourseDetail`; it does not generate `google.api.http` annotations or canonical protobuf request wrappers.

At registration, `RuntimeTypeModelConfigurator` calls `SchemataProtoModelConfigurator.ConfigureType` for both custom types. The protobuf-net runtime model applies trait aliases and snake_case field names recursively, and the descriptor bridge is built from that same model. `name`, `etag`, and `request_id` are therefore the protobuf field names for `CanonicalName`, `EntityTag`, and `RequestId`.

## When to escalate the action

Keep `:publish` synchronous when it performs one bounded state transition and a repository commit. Escalate based on durable execution needs, not on the transport name.

| Condition | Owner | Status | Reason |
| --- | --- | --- | --- |
| Publish must run later, repeat, recover after restart, or expose a persisted execution lifecycle | Scheduling Job | Supported by extension point | Register a job with `UseScheduling().WithJob<T>()`; its trigger and execution rows establish a background boundary. Schemata's `Operation` is not automatically `google.longrunning.Operation`, so a public AIP-151 contract remains Partial. |
| Publish has multiple durable steps, waits for human or external events, needs compensation, or branches through a process | Flow | Supported by extension point | Register a `ProcessDefinition` through `UseFlow().UseStateMachine().Use<TProcess>()`; Flow owns persistent process and token state. |
| The action still changes only this resource immediately | Custom-method handler | Application responsibility | Keep `PublishCourseHandler` as the one business owner. |

Do not move the same transition into a job or Flow while leaving it in the handler. The handler may start a chosen process or job, but the state machine must have one owner.

## Common pitfalls

- **Using Update for `state = published`.** Update expresses a representation change. `publish` has a separate verb, transition precondition, and permission, so it belongs in an instance custom method.
- **Omitting `WithAuthorization()`.** `UseSecurity()` registers provider defaults, but resource authorization advisors run only after `WithAuthorization()` registers them.
- **Trusting a body `name`.** The instance route is authoritative. The operation handler overwrites `CanonicalName` from the route before the handler runs.
- **Adding `IRequestIdentification` without a cache provider.** A request ID invokes `AdviceMethodRequestIdempotency`, which requires `ICacheProvider`; register one `AddDistributedCache()` implementation.
- **Treating an ETag as atomic locking.** The advisor compares the entity loaded by the resource pipeline before dispatch. Provider-specific conditional persistence remains necessary when the domain requires a single atomic state-and-version check.
- **Returning a static detail or summary type as AIP-157 support.** `CourseDetail` and `CourseSummary` are fixed mapped shapes, not a per-request partial-response protocol.
- **Returning `Schemata.Abstractions.Resource.Operation` as an AIP-151 operation.** Scheduling offers a durable Schemata operation resource, while AIP-151 requires `google.longrunning.Operation`, its annotations, and the Operations service.

## See also

- [Design a Resource API with Google AIPs](../guides/aip-resource-design.md)
- [Resource API Interactions and Google AIPs](../documents/resource/aip-interactions.md)
- [Business Logic and Google AIP](../documents/resource/aip-business-logic.md)
- [Custom Methods](../documents/resource/custom-methods.md)
- [Security](../documents/security.md)
- [Cron Jobs](cron-jobs.md)
- [Flow with Timers](flow-with-timers.md)
