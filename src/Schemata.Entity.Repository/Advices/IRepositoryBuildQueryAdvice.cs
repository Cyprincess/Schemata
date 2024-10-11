using Schemata.Abstractions.Advices;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryBuildQueryAdvice<TEntity> : IAdvice<QueryContainer<TEntity>> where TEntity : class;
