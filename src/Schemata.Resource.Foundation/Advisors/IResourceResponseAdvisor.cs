using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceResponseAdvisor<in TEntity, in TDetail> : IAdvisor<TEntity?, TDetail?, HttpContext?>
    where TEntity : class, IIdentifier
    where TDetail : class, IIdentifier;
