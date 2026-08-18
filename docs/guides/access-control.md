# Access Control

Gate the Student CRUD operations behind permission claims and filter list results per user, using
`UseSecurity()` and the resource builder's `WithAuthorization()`. This guide builds on
[Identity](identity.md) for the user store, and on [Object Mapping](object-mapping.md) for the
`StudentRequest`, `StudentDetail`, and `StudentSummary` types the access and entitlement providers
are declared against. Readers who skipped Object Mapping either add those DTOs first or substitute
`Student` for all four type parameters.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Security.Foundation`. To compose
manually:

```shell
dotnet add package --prerelease Schemata.Security.Foundation
```

## Enable security

Add `UseSecurity()` in `Program.cs`:

```csharp
schema.UseSecurity();
```

`SchemataSecurityFeature` registers four scoped services with `TryAdd`: an access provider, an
entitlement provider, a permission resolver, and a permission matcher. Then opt the `Student`
resource into authorization on the resource builder:

```csharp
schema.UseResource()
      .WithAuthorization()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

`WithAuthorization()` registers two advisor families per operation: the anonymous advisors (which
mark operations allowed by an entity's `[Anonymous]` attribute) run first, then the authorize
advisors (which call the access and entitlement providers). Without `WithAuthorization()`, the
providers are never invoked. Advisor orders are listed in [Security](../documents/security.md).

## How permissions work

`DefaultAccessProvider` denies any unauthenticated caller, then resolves a permission and asks the
matcher whether the principal holds it. `DefaultPermissionResolver` produces `{entity}.{operation}`
in kebab-case. For `Student`:

| Operation | Permission       |
| --------- | ---------------- |
| List      | `student.list`   |
| Get       | `student.get`    |
| Create    | `student.create` |
| Update    | `student.update` |
| Delete    | `student.delete` |

`DefaultPermissionMatcher` reads claims of the type in `SchemataSecurityOptions.PermissionClaimType`
(default `"role"`) and supports exact matches plus a single `*` wildcard segment:

- `student.*` grants every operation on `Student`.
- `*.list` grants `List` on any single-word entity.
- A bare `*` matches nothing.

## Assign permission claims

```csharp
var user = await userManager.FindByEmailAsync("alice@example.com");
await userManager.AddClaimAsync(user!, new Claim("role", "student.*"));
```

## Custom access provider

Register a closed-generic `IAccessProvider<Student, StudentRequest>` to authorize one entity; the
open-generic default covers the rest:

```csharp
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

public sealed class StudentAccessProvider : IAccessProvider<Student, StudentRequest>
{
    public Task<bool> HasAccessAsync(
        Student?                      entity,
        AccessContext<StudentRequest> context,
        ClaimsPrincipal?              principal,
        CancellationToken             ct = default)
    {
        // context.Operation is "List" / "Get" / "Create" / "Update" / "Delete"
        return Task.FromResult(principal?.Identity?.IsAuthenticated == true);
    }
}
```

A closed-generic registration takes precedence over the open-generic default no matter where it
appears in startup:

```csharp
schema.UseSecurity();
schema.ConfigureServices(services =>
    services.AddScoped<IAccessProvider<Student, StudentRequest>, StudentAccessProvider>());
```

## Row-level filtering

`IEntitlementProvider<T, TRequest>` returns a LINQ predicate composed into the repository query, or
`null` for no filter. The default returns `null`. To restrict each user to their own rows, first add
the `IOwnable` trait to `Student.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

[PrimaryKey(nameof(Uid))]
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IConcurrency, IOwnable
{
    // ... the properties from the earlier guides

    // IOwnable
    public string? Owner { get; set; }
}
```

Delete `app.db` so EF Core recreates the schema with the new `Owner` column. Then filter on it:

```csharp
using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

public sealed class StudentEntitlementProvider
    : IEntitlementProvider<Student, StudentRequest>
{
    public Task<Expression<Func<Student, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<StudentRequest> context,
        ClaimsPrincipal?              principal,
        CancellationToken             ct = default)
    {
        var id = principal?.FindFirstValue(IdentityClaims.Subject);

        Expression<Func<Student, bool>> filter = string.IsNullOrEmpty(id)
            ? _ => false
            : s => s.Owner == id;

        return Task.FromResult<Expression<Func<Student, bool>>?>(filter);
    }
}
```

The Identity bridge supplies `IdentityClaims.Subject` as the canonical user name (`users/{uid}`), so the
filter compares `Owner` with the same canonical reference used by the ownership advisor.

Populating `Owner` on create is your side of the contract — set it in an add advisor, or wire the
`Schemata.Entity.Owner` package's `UseOwner()` which fills it through an `IOwnerResolver` (see the
[ownership cookbook](../cookbook/ownership-and-row-acl.md)). Register the provider the same way as
the access provider. Entitlement filtering applies to List, Get, Update, and Delete; Create has no
entitlement step.

## Verify

```shell
dotnet run
```

```shell
# Login as Alice (Identity guide)
curl -X POST http://localhost:5000/Authenticate/Login \
     -H "Content-Type: application/json" \
     -d '{"username":"alice","password":"P@ssw0rd!"}'

# Create a student — succeeds with the student.* claim
curl -X POST http://localhost:5000/v1/students \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <alice_token>" \
     -d '{"full_name":"Bob","age":22}'

# Without a token the access provider denies the request: 403
curl http://localhost:5000/v1/students
```

## Next steps

- [Authorization](authorization.md) — issue OAuth 2.0 / OpenID Connect tokens that carry the same claims
- [Multi-Tenancy](multi-tenancy.md) — combine row-level security with per-tenant isolation
- [gRPC Transport](grpc-transport.md) — the same authorization advisors apply to gRPC

## See also

- [Security](../documents/security.md) — providers, permission resolution, advisor orders
- [Ownership](../documents/repository/ownership.md) — `UseOwner()` for automatic ownership filtering
