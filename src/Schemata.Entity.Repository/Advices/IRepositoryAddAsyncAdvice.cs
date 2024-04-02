using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryAddAsyncAdvice<TEntity> : IAdvice<IRepository<TEntity>, TEntity>
    where TEntity : class;
