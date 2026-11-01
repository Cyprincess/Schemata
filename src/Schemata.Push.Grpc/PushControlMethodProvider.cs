using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Authorization;
using Schemata.Push.Skeleton.Control;

namespace Schemata.Push.Grpc;

/// <summary>
///     Registers the Push control-plane unary methods on the shared model and attaches the
///     per-verb authorization policies.
/// </summary>
internal sealed class PushControlMethodProvider(PushGrpcModel model) : IServiceMethodProvider<PushControlGrpcService>
{
    #region IServiceMethodProvider<PushControlGrpcService> Members

    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<PushControlGrpcService> context) {
        context.AddUnaryMethod(
            model.Create,
            Metadata(PushPolicies.Create),
            static (service, request, call) => service.CreateAsync(request, new(service, call)).AsTask());

        context.AddUnaryMethod(
            model.List,
            Metadata(PushPolicies.List),
            static (service, request, call) => service.ListAsync(request, new(service, call)).AsTask());

        context.AddUnaryMethod(
            model.Delete,
            Metadata(PushPolicies.Delete),
            static (service, request, call) => service.DeleteAsync(request, new(service, call)).AsTask());

        context.AddUnaryMethod(
            model.Send,
            Metadata(PushPolicies.Send),
            static (service, request, call) => service.SendAsync(request, new(service, call)).AsTask());

        return;

        static object[] Metadata(string policy) => [new AuthorizeAttribute(), new AuthorizeAttribute { Policy = policy }];
    }

    #endregion
}
