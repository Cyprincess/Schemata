using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

public interface IIdentityRegisterRequestAdvisor : IAdvisor<RegisterRequest, HttpContext>;
