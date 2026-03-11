using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Advisors;

public interface IIdentityRegisterAdvisor : IAdvisor<SchemataUser, HttpContext>;
