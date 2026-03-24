using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>
///     Advisor invoked after a user has been successfully created during registration.
/// </summary>
/// <remarks>
///     This is the final advisor in the registration pipeline, called after the user entity
///     has been persisted. Use it to perform post-registration tasks such as assigning roles,
///     sending welcome notifications, or triggering external workflows.
///     <list type="bullet">
///         <item><see cref="AdviseResult.Continue"/> -- proceed with the default registration response.</item>
///         <item><see cref="AdviseResult.Block"/> or <see cref="AdviseResult.Handle"/> -- suppress the default response (the advisor is responsible for writing a response).</item>
///     </list>
/// </remarks>
public interface IIdentityRegisterAdvisor : IAdvisor<SchemataUser, HttpContext>;
