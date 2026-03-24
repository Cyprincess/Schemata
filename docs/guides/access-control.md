# Access Control

This guide adds role-based access control and row-level security to the Student CRUD app. Building on the Identity setup from the previous guide, you will gate every CRUD operation behind claims and filter query results per user.

## Add the package

```shell
dotnet add package --prerelease Schemata.Security.Foundation
```

## Configure Security

In `Program.cs`, add `UseSecurity()` and enable resource authorization:

```csharp
schema.UseSecurity();
```

Then chain `.WithAuthorization()` on the resource builder:

```csharp
schema.UseResource()
      .WithAuthorization()
      .MapHttp()
      .Use<Student, Student, Student, Student>();
```

`UseSecurity()` registers the default open-generic `IAccessProvider<,>` and `IEntitlementProvider<,>` implementations. Without custom providers, all access is granted and no row filtering is applied. `WithAuthorization()` registers the built-in resource authorization advisors for all five CRUD operations (List, Get, Create, Update, Delete), which call into the access providers on every request.

## The built-in ResourceAccessProvider

The `Schemata.Resource.Foundation` package includes `ResourceAccessProvider<T, TRequest>`, a claims-based access provider that checks for role claims in the format:

```
resource-{operation}-{entity}
```

Both `{operation}` and `{entity}` are kebab-cased. The operation comes from the `Operations` enum (`List`, `Get`, `Create`, `Update`, `Delete`) converted to kebab-case, and the entity is the class name converted to kebab-case. For the `Student` entity, the claim values are:

| Operation | Required claim            |
| --------- | ------------------------- |
| List      | `resource-list-student`   |
| Get       | `resource-get-student`    |
| Create    | `resource-create-student` |
| Update    | `resource-update-student` |
| Delete    | `resource-delete-student` |

Two wildcard patterns are also supported:

- `resource-*-student` -- grants all operations on the Student entity
- `resource-list-*` -- grants the List operation on all entities

The provider checks for these values in `ClaimTypes.Role` claims on the current principal. If none match, access is denied.

`ResourceAccessProvider` is not registered by default. To use it, register it in your service configuration:

```csharp
schema.ConfigureServices(services => {
    services.AddAccessProvider<Student,
                               ResourceRequestContext<Student>,
                               ResourceAccessProvider<Student, Student>>();
});
```

## Assign claims to users

After registering a user (from the previous Identity guide), assign the required role claims using `UserManager`:

```csharp
using System.Security.Claims;

// In a seeding method or admin endpoint:
var user = await userManager.FindByEmailAsync("alice@example.com");
await userManager.AddClaimAsync(user!, new Claim(ClaimTypes.Role, "resource-*-student"));
```

This grants Alice all CRUD operations on Student. For read-only access, assign only the list and get claims:

```csharp
await userManager.AddClaimAsync(user!, new Claim(ClaimTypes.Role, "resource-list-student"));
await userManager.AddClaimAsync(user!, new Claim(ClaimTypes.Role, "resource-get-student"));
```

## Row-level filtering with IEntitlementProvider

`IEntitlementProvider<T, TContext>` generates LINQ expressions that are composed into repository queries, filtering rows at the data layer. The default implementation returns `_ => true` (no filtering).

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
using Schemata.Resource.Foundation;
using Schemata.Security.Skeleton;

public class StudentEntitlementProvider
    : IEntitlementProvider<Student, ResourceRequestContext<Student>>
{
    public Task<Expression<Func<Student, bool>>?> GenerateEntitlementExpressionAsync(
        ResourceRequestContext<Student>? context,
        ClaimsPrincipal?                 principal,
        CancellationToken                ct = default)
    {
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
        {
            // No authenticated user -- return nothing
            Expression<Func<Student, bool>> deny = _ => false;
            return Task.FromResult<Expression<Func<Student, bool>>?>(deny);
        }

        Expression<Func<Student, bool>> filter = s => s.CreatedBy == id;
        return Task.FromResult<Expression<Func<Student, bool>>?>(filter);
    }
}
```

Register it before `UseSecurity()` so it takes precedence over the default open-generic:

```csharp
schema.ConfigureServices(services => {
    services.AddScoped<
        IEntitlementProvider<Student, ResourceRequestContext<Student>>,
        StudentEntitlementProvider>();
});

schema.UseSecurity();
```

Because `SchemataSecurityFeature` uses `TryAddScoped` for the open-generic fallback, any concrete registration made before it will not be overwritten.

## Verify

```shell
dotnet run
```

```shell
# Login as Alice (who has resource-*-student claims)
curl -X POST http://localhost:5000/Authenticate/Login \
     -H "Content-Type: application/json" \
     -d '{"username":"alice@example.com","password":"P@ssw0rd!"}'

# Create a student (succeeds with the access claim)
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <alice_token>" \
     -d '{"full_name":"Bob","age":22}'

# List students -- only sees students created by Alice
curl http://localhost:5000/students \
     -H "Authorization: Bearer <alice_token>"

# Without a token, requests are denied
curl http://localhost:5000/students
# Returns 403 Forbidden
```

## Next steps

- [Authorization](authorization.md) -- add an OAuth 2.0 / OpenID Connect authorization server
- For deeper technical details, see [Security](../documents/security.md)
