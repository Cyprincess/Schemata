using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

/// <summary>
///     Puts all gRPC integration test classes in the same collection so they share a single
///     <see cref="WebAppFactory" /> instance.  This avoids the static-Configured-set problem in
///     <c>RuntimeTypeModelConfigurator</c>, which prevents a second factory from registering
///     the same types in its own <c>RuntimeTypeModel</c>.
/// </summary>
[CollectionDefinition("GrpcIntegration")]
public class GrpcTestCollection : ICollectionFixture<WebAppFactory>
{ }
