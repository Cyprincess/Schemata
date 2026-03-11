using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

public interface IRepositoryBuildQueryAdvisor<TEntity> : IAdvisor<QueryContainer<TEntity>>
    where TEntity : class;
