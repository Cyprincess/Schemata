using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryUpdateAsyncAdvice<in TEntity> : IAdvice<TEntity>;
