using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryRemoveAsyncAdvice<in TEntity> : IAdvice<TEntity>;
