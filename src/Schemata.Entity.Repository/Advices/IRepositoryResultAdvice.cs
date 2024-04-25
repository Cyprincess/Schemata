using Schemata.Abstractions.Advices;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryResultAdvice<TEntity, TResult, T> : IAdvice<QueryContext<TEntity, TResult, T>>
    where TEntity : class;
