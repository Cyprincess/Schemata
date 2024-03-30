using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryQueryAsyncAdvice<TEntity> : IAdvice<QueryContainer<TEntity>>;
