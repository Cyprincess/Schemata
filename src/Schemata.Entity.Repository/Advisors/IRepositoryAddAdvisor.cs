using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

public interface IRepositoryAddAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;
