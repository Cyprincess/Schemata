using System;
using Schemata.Entity.EntityFrameworkCore;

namespace Schemata.Entity.Tests;

public class EfCoreRepository<TEntity> : EntityFrameworkCoreRepository<TestingContext, TEntity>
    where TEntity : class
{
    public EfCoreRepository(IServiceProvider sp, TestingContext context) : base(sp, context) { }
}
