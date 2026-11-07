using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>Enforces final username, email, and phone alias uniqueness in the manager's serializable transaction.</summary>
public sealed class AdviceAliasUniqueness<TUser> : IIdentityAliasUniquenessAdvisor<TUser>
    where TUser : SchemataUser
{
    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext context,
        Operations operation,
        TUser user,
        IUnitOfWork transaction,
        System.Collections.Generic.IList<ErrorFieldViolation> errors,
        CancellationToken ct
    ) {
        if (operation is not (Operations.Create or Operations.Update)) return AdviseResult.Continue;

        var users = context.ServiceProvider.GetRequiredService<IRepository<TUser>>();
        users.Join(transaction);
        Guid? excluded = operation == Operations.Update ? user.Uid : null;
        if (!string.IsNullOrWhiteSpace(user.NormalizedUserName)) {
            var duplicateName = excluded is { } userUid
                ? await users.FirstOrDefaultAsync(query => query.Where(row =>
                    row.NormalizedUserName == user.NormalizedUserName && row.Uid != userUid), ct)
                : await users.FirstOrDefaultAsync(query => query.Where(row =>
                    row.NormalizedUserName == user.NormalizedUserName), ct);
            if (duplicateName is not null) throw SchemataResourceErrors.AlreadyExists<TUser>(duplicateName.CanonicalName);
        }

        if (!string.IsNullOrWhiteSpace(user.NormalizedEmail)) {
            var duplicate = excluded is { } uid
                ? await users.FirstOrDefaultAsync(query => query.Where(row =>
                    row.NormalizedEmail == user.NormalizedEmail && row.Uid != uid), ct)
                : await users.FirstOrDefaultAsync(query => query.Where(row =>
                    row.NormalizedEmail == user.NormalizedEmail), ct);
            if (duplicate is not null) throw SchemataResourceErrors.AlreadyExists<TUser>(duplicate.CanonicalName);
        }

        if (string.IsNullOrWhiteSpace(user.PhoneNumber)) return AdviseResult.Continue;
        var conflict = excluded is { } excludedUid
            ? await users.FirstOrDefaultAsync(query => query.Where(row =>
                row.PhoneNumber == user.PhoneNumber && row.Uid != excludedUid), ct)
            : await users.FirstOrDefaultAsync(query => query.Where(row =>
                row.PhoneNumber == user.PhoneNumber), ct);
        if (conflict is not null) throw SchemataResourceErrors.AlreadyExists<TUser>(conflict.CanonicalName);
        return AdviseResult.Continue;
    }
}
