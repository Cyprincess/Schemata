using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>
///     Advisor invoked at the start of the registration flow to validate or modify the incoming request.
/// </summary>
/// <remarks>
///     This is the first advisor in the registration pipeline, called before the user entity is created.
///     Use it to enforce custom validation rules, rate limiting, or request transformation.
///     <list type="bullet">
///         <item><see cref="AdviseResult.Continue"/> -- proceed with user creation.</item>
///         <item><see cref="AdviseResult.Block"/> or <see cref="AdviseResult.Handle"/> -- reject the request before any user is created.</item>
///     </list>
/// </remarks>
public interface IIdentityRegisterRequestAdvisor : IAdvisor<RegisterRequest, HttpContext>;
