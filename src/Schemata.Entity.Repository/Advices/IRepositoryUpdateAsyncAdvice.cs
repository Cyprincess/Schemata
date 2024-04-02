using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryUpdateAsyncAdvice<TEntity> : IAdvice<IRepository<TEntity>, TEntity>
    where TEntity : class;
