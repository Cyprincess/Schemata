using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

public interface IRepositoryQueryAdvisor<TEntity, TResult, T> : IAdvisor<QueryContext<TEntity, TResult, T>>
    where TEntity : class;
