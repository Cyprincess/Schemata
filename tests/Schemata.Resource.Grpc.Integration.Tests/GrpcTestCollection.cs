using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[CollectionDefinition("GrpcIntegration")]
public class GrpcTestCollection : ICollectionFixture<WebAppFactory>;
