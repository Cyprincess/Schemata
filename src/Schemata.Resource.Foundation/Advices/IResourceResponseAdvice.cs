using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceResponseAdvice<TDetail> : IAdvice<TDetail?, HttpContext>
    where TDetail : class, IIdentifier;
