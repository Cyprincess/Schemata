using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryRemoveAsyncAdvice<TEntity> : IAdvice<IRepository<TEntity>, TEntity>
    where TEntity : class;
