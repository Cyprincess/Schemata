using Schemata.Abstractions.Advices;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryQueryAsyncAdvice<TEntity> : IAdvice<IRepository<TEntity>, QueryContainer<TEntity>>
    where TEntity : class;
