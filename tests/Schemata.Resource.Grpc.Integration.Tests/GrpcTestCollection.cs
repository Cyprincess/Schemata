using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

/// <summary>
///     Shares one <see cref="WebAppFactory" /> across gRPC integration tests because
///     <c>RuntimeTypeModelConfigurator</c> keeps process-wide configured type state.
/// </summary>
[CollectionDefinition("GrpcIntegration")]
public class GrpcTestCollection : ICollectionFixture<WebAppFactory>;
