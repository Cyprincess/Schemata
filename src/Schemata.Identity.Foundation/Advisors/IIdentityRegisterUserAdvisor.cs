using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>
///     Advisor invoked after the user entity has been constructed but before it is persisted.
/// </summary>
/// <remarks>
///     This is the second advisor in the registration pipeline. The <see cref="SchemataUser"/> instance
///     has been populated from the request but has not yet been saved. Use it to modify user properties,
///     set default values, or reject the user before creation.
///     <list type="bullet">
///         <item><see cref="AdviseResult.Continue"/> -- proceed to persist the user.</item>
///         <item><see cref="AdviseResult.Block"/> or <see cref="AdviseResult.Handle"/> -- reject the user before it is saved.</item>
///     </list>
/// </remarks>
public interface IIdentityRegisterUserAdvisor : IAdvisor<SchemataUser, HttpContext>;
