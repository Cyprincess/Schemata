using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advices;

public interface IIdentityRegisterRequestAdvice : IAdvice<RegisterRequest, HttpContext>;
