using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

public interface ITenantServiceScopeFactory<TTenant, TKey> : IServiceScopeFactory where TTenant : SchemataTenant<TKey>
                                                                                  where TKey : struct, IEquatable<TKey>;
