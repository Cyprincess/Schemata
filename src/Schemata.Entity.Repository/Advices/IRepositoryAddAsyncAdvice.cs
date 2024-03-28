using System.Threading;
using Schemata.Abstractions;

namespace Schemata.Entity.Repository.Advices;

public interface IRepositoryAddAsyncAdvice<in TEntity> : IAdvice<TEntity, CancellationToken>;
