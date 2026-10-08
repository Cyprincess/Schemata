using System;
using System.Threading.Tasks;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

/// <summary>
///     Issue #34 observable matrix over the gRPC transport: the same scheme-protected resource
///     as the HTTP suite must keep its anonymous exemptions, reject anonymous callers on
///     protected operations, and omit whitelisted-out operations from the discovered service.
/// </summary>
[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcOperationIdentityShould(WebAppFactory factory)
{
    [Fact]
    public async Task AnonymousExemptOperation_PassesSchemeAuthentication() {
        // The anonymous Get exemption carried by the per-method metadata: the call reaches the
        // handler and reports the miss, it is not rejected as unauthenticated.
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => CallAsync(new Method<GetRequest, LockedStudent>(
                                MethodType.Unary, Service(), "GetLockedStudent",
                                Marshaller<GetRequest>(), Marshaller<LockedStudent>()),
                            new() { CanonicalName = "lockedStudents/missing" }));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task SchemeOnlyOperation_RejectsAnonymousCallers() {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => CallAsync(new Method<ListRequest, ListResultBase<LockedStudent, LockedStudent>>(
                                MethodType.Unary, Service(), "ListLockedStudents",
                                Marshaller<ListRequest>(), Marshaller<ListResultBase<LockedStudent, LockedStudent>>()),
                            new()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task WhitelistedOutOperation_IsAbsentFromTheDiscoveredService() {
        // Create is outside the registered operations whitelist: no method is generated for it,
        // so even an authenticated caller finds no Create RPC on the discovered service. The
        // service-level scheme policy still guards the whole service for anonymous callers.
        var unauthenticated = await Assert.ThrowsAsync<RpcException>(
            () => CallAsync(new Method<LockedStudent, LockedStudent>(
                                MethodType.Unary, Service(), "CreateLockedStudent",
                                Marshaller<LockedStudent>(), Marshaller<LockedStudent>()),
                            new() { FullName = "Never" }));
        Assert.Equal(StatusCode.Unauthenticated, unauthenticated.StatusCode);

        var authenticated = await Assert.ThrowsAsync<RpcException>(
            () => CallAuthenticatedAsync(new Method<LockedStudent, LockedStudent>(
                                MethodType.Unary, Service(), "CreateLockedStudent",
                                Marshaller<LockedStudent>(), Marshaller<LockedStudent>()),
                            new() { FullName = "Never" }));
        Assert.Equal(StatusCode.Unimplemented, authenticated.StatusCode);
    }

    [Fact]
    public async Task AnonymousCustomVerb_PassesSchemeAuthentication() {
        // The registered announce verb is [Anonymous]: its metadata exempts it from the
        // service-level scheme policy, and the call reaches the handler's miss.
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => CallAsync(new Method<LockedAnnounceRequest, LockedStudent>(
                                MethodType.Unary, Service(), CustomMethod("announce"),
                                Marshaller<LockedAnnounceRequest>(), Marshaller<LockedStudent>()),
                            new() { CanonicalName = "lockedStudents/missing" }));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ProtectedCustomVerb_RejectsAnonymousCallers() {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => CallAsync(new Method<LockedSealRequest, LockedStudent>(
                                MethodType.Unary, Service(), CustomMethod("seal"),
                                Marshaller<LockedSealRequest>(), Marshaller<LockedStudent>()),
                            new() { CanonicalName = "lockedStudents/missing" }));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    private async Task CallAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, TRequest request)
        where TRequest : class
        where TResponse : class {
        await CallCoreAsync(method, request, authenticated: false);
    }

    private async Task CallAuthenticatedAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, TRequest request)
        where TRequest : class
        where TResponse : class {
        await CallCoreAsync(method, request, authenticated: true);
    }

    private async Task CallCoreAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, TRequest request, bool authenticated)
        where TRequest : class
        where TResponse : class {
        using var channel = factory.CreateGrpcChannel();
        var headers = authenticated
            ? new Metadata { { "X-Test-Auth", "valid" } }
            : new();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(headers), request);
        _ = await call.ResponseAsync;
    }

    private static string Service() {
        return GrpcResourceNaming.ServiceFullName(typeof(LockedStudent));
    }

    private static string CustomMethod(string verb) {
        return GrpcResourceNaming.CustomMethodName(ResourceNameDescriptor.ForType<LockedStudent>(), verb);
    }

    private static Marshaller<T> Marshaller<T>()
        where T : class {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(T));
        return GrpcMarshallers.Create<T>(model);
    }
}
