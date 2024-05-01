using System;
using Schemata.Entity.EntityFrameworkCore;

namespace Schemata.Entity.Tests;

public class EfCoreRepository<TEntity>(IServiceProvider sp, TestingContext context) : EntityFrameworkCoreRepository<TestingContext, TEntity>(sp, context)
    where TEntity : class;
