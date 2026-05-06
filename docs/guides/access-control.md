# Access Control

This guide adds role-based access control and row-level security to the Student CRUD app. Building on the Identity setup from the previous guide, you will gate CRUD operations behind claims and filter query results per user.

## Configuration

`Schemata.Application.Complex.Targets` already includes `Schemata.Security.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Security.Foundation
```

In `Program.cs`, add `UseSecurity()` and enable resource authorization:

```csharp
schema.UseSecurity();
```

Then chain `.WithAuthorization()` on the resource builder:

```csharp
schema.UseResource()
      .WithAuthorization()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

If you skipped [Object Mapping](object-mapping.md) and are using entity-only types, use `Use<Student, Student, Student, Student>()` instead.

`UseSecurity()` registers the default open-generic `IAccessProvider<,>` and `IEntitlementProvider<,>` implementations, plus the default `IPermissionResolver` and `IPermissionMatcher`. These defaults perform claims-based access checks: they resolve a permission string from `(entity, operation)` and match it against role claims on the principal.

`WithAuthorization()` registers `AdviceXxxRequestAnonymous` and `AdviceXxxRequestAuthorize` advisors for all five CRUD operations (List, Get, Create, Update, Delete). On every request, the authorize advisor calls `IAccessProvider<,>.HasAccessAsync`, which resolves and matches permissions.

## How permissions work

`DefaultPermissionResolver` produces permissions in the format:

```
{entity}.{operation}
```

Both parts are kebab-cased. For the `Student` entity, the resolved permissions are:

| Operation | Permission      |
| --------- | --------------- |
| List      | `student.list`  |
| Get       | `student.get`   |
| Create    | `student.create`|
| Update    | `student.update`|
| Delete    | `student.delete`|

`DefaultPermissionMatcher` checks claims of type `"role"` (configurable via `SchemataSecurityOptions.PermissionClaimType`). It supports exact match and wildcards:

- `student.*` — grants all operations on Student
- `*.list` — grants List on any entity

If no matching claim is found, access is denied.

## Assign claims to users

After registering a user (from the previous Identity guide), assign the required role claims using `UserManager`:

```csharp
using System.Security.Claims;

// In a seeding method or admin endpoint:
var user = await userManager.FindByEmailAsync("alice@example.com");
await userManager.AddClaimAsync(user!, new Claim("role", "student.*"));
```

This grants Alice all CRUD operations on Student. For read-only access, assign only the list and get claims:

```csharp
await userManager.AddClaimAsync(user!, new Claim("role", "student.list"));
await userManager.AddClaimAsync(user!, new Claim("role", "student.get"));
```

## Custom access provider

Replace the default `IAccessProvider<,>` with a custom implementation for fine-grained access logic:

```csharp
using System.Security.Claims;
using Schemata.Security.Skeleton;

public class StudentAccessProvider : IAccessProvider<Student, StudentRequest>
{
    public Task<bool> HasAccessAsync(
        Student?                entity,
        AccessContext<StudentRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct)
    {
        // Custom logic — e.g., only allow updates to entities the user owns
        return Task.FromResult(true);
    }
}
```

Register it before `UseSecurity()` so it takes precedence over the default open-generic:

```csharp
schema.ConfigureServices(services => {
    services.AddScoped<
        IAccessProvider<Student, StudentRequest>,
        StudentAccessProvider>();
});

schema.UseSecurity();
```

`SchemataSecurityFeature` uses `TryAddScoped` for the open-generic fallback, so any concrete registration made before it takes precedence.

## Row-level filtering with IEntitlementProvider

`IEntitlementProvider<T, TRequest>` generates LINQ expressions that are composed into repository queries, filtering rows at the data layer. The default implementation returns `_ => true` (no filtering).

To restrict students so each user only sees records they created, implement a custom entitlement provider. First, add a `CreatedBy` property to the `Student` entity:

```csharp
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    // ... existing properties ...

    public string? CreatedBy { get; set; }
}
```

Then implement the provider:

```csharp
using System.Linq.Expressions;
using System.Security.Claims;
using Schemata.Security.Skeleton;

public class StudentEntitlementProvider
    : IEntitlementProvider<Student, StudentRequest>
{
    public Task<Expression<Func<Student, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<StudentRequest>? context,
        ClaimsPrincipal?              principal,
        CancellationToken             ct = default)
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

Register it before `UseSecurity()`:

```csharp
schema.ConfigureServices(services => {
    services.AddScoped<
        IEntitlementProvider<Student, StudentRequest>,
        StudentEntitlementProvider>();
});

schema.UseSecurity();
```

## Verify

```shell
dotnet run
```

```shell
# Login as Alice (who has student.* claims)
curl -X POST http://localhost:5000/Authenticate/Login \
     -H "Content-Type: application/json" \
     -d '{"username":"alice@example.com","password":"P@ssw0rd!"}'

# Create a student (succeeds with the access claim)
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <alice_token>" \
     -d '{"full_name":"Bob","age":22}'

# List students — only sees students created by Alice
curl http://localhost:5000/students \
     -H "Authorization: Bearer <alice_token>"

# Without a token, requests are denied
curl http://localhost:5000/students
# Returns 403 Forbidden
```

## Next steps

- [Authorization](authorization.md) -- add an OAuth 2.0 / OpenID Connect authorization server
- For deeper technical details, see [Security](../documents/security.md)
- For a simpler alternative to custom `IEntitlementProvider` for row-level ownership filtering, see [Ownership](../documents/repository/ownership.md). The `UseOwner()` pattern auto-assigns an `IOwnable.Owner` field on create and auto-filters queries by the current principal without writing custom LINQ expressions.
