using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Advices;

public interface IIdentityRegisterAdvice : IAdvice<SchemataUser, HttpContext>;
