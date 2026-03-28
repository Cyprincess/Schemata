using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Security.Tests.Fixtures;

[Anonymous(Operations.Create, Operations.List)]
public class PublicProduct : Product;
