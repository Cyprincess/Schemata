# Access Control

Gate CRUD operations behind claims and filter query results per user using `UseSecurity`. This guide builds on [Identity](identity.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Security.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Security.Foundation
```

## Enable security

Add `UseSecurity()` in `Program.cs`. `SchemataSecurityFeature` runs at `Order = Priority = 400_000_000`:

```csharp
schema.UseSecurity();
```

Then chain `.WithAuthorization()` on the resource builder to activate the authorize advisors:

```csharp
schema.UseResource()
      .WithAuthorization()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

`WithAuthorization()` registers `AdviceXxxRequestAnonymous` (Order 80M) and `AdviceXxxRequestAuthorize` (Order 100M) for all five CRUD operations. The authorize advisor calls `IAccessProvider<TEntity, TRequest>.HasAccessAsync` on every request.

## How permissions work

`DefaultPermissionResolver` produces permissions in the format `{entity}.{operation}` (both kebab-cased). For `Student`:

| Operation | Permission |
| --------- | ---------- |
| List | `student.list` |
| Get | `student.get` |
| Create | `student.create` |
| Update | `student.update` |
| Delete | `student.delete` |

`DefaultPermissionMatcher` checks claims of type `"role"`. It supports exact match and wildcards:

- `student.*` — grants all operations on Student
- `*.list` — grants List on any entity

## Assign claims to users

```csharp
var user = await userManager.FindByEmailAsync("alice@example.com");
await userManager.AddClaimAsync(user!, new Claim("role", "student.*"));
```

## Custom access provider

Replace the default open-generic `IAccessProvider<,>` with a concrete implementation:

```csharp
using System.Security.Claims;
using Schemata.Security.Skeleton;

public sealed class StudentAccessProvider : IAccessProvider<Student, StudentRequest>
{
    public Task<bool> HasAccessAsync(
        Student?                      entity,
        AccessContext<StudentRequest> context,
        ClaimsPrincipal?              principal,
        CancellationToken             ct)
    {
        // custom logic
        return Task.FromResult(true);
    }
}
```

Register it before `UseSecurity()` — `SchemataSecurityFeature` uses `TryAddScoped` for the open-generic fallback, so any concrete registration made first takes precedence:

```csharp
schema.ConfigureServices(services => {
    services.AddScoped<IAccessProvider<Student, StudentRequest>, StudentAccessProvider>();
});
schema.UseSecurity();
```

## Row-level filtering

`IEntitlementProvider<T, TRequest>` generates LINQ expressions composed into repository queries. The default returns `_ => true`. To restrict each user to their own records:

```csharp
public sealed class StudentEntitlementProvider
    : IEntitlementProvider<Student, StudentRequest>
{
    public Task<Expression<Func<Student, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<StudentRequest>? context,
        ClaimsPrincipal?               principal,
        CancellationToken              ct = default)
    {
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
        {
            Expression<Func<Student, bool>> deny = _ => false;
            return Task.FromResult<Expression<Func<Student, bool>>?>(deny);
        }

        Expression<Func<Student, bool>> filter = s => s.CreatedBy == id;
        return Task.FromResult<Expression<Func<Student, bool>>?>(filter);
    }
}
```

Register it before `UseSecurity()` in the same way as the access provider.

## Verify

```shell
dotnet run
```

```shell
# Login as Alice
curl -X POST http://localhost:5000/Authenticate/Login \
     -H "Content-Type: application/json" \
     -d '{"username":"alice@example.com","password":"P@ssw0rd!"}'

# Create a student (succeeds with student.* claim)
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <alice_token>" \
     -d '{"full_name":"Bob","age":22}'

# Without a token, returns 403
curl http://localhost:5000/students
```

## See also

- [Identity](identity.md) — user registration and login
- [Authorization](authorization.md) — OAuth 2.0 / OpenID Connect server
- [Security](../documents/security.md) — `IAccessProvider`, `IEntitlementProvider`, permission resolution
- [Ownership](../documents/repository/ownership.md) — `UseOwner()` for automatic row-level ownership filtering
