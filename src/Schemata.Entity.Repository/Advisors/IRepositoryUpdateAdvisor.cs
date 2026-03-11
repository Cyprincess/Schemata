using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

public interface IRepositoryUpdateAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;
