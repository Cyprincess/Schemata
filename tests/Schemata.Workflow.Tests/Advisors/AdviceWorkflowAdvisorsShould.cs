using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Advisors;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;
using Xunit;

namespace Schemata.Workflow.Tests.Advisors;

public class AdviceWorkflowAdvisorsShould
{
    private static ClaimsPrincipal CreateUser() { return new(new ClaimsIdentity("Test")); }

    private static AdviceContext CreateContext() { return new(new ServiceCollection().BuildServiceProvider()); }

    private static SchemataWorkflow CreateWorkflow(Guid id) {
        return new() { Uid = id, InstanceType = "Order", InstanceId = id };
    }

    #region StatusAnonymous

    [Fact]
    public async Task StatusAnonymous_NoAttribute_DoesNotSetAnonymousGranted() {
        var advisor  = new AdviceStatusAnonymous();
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());

        var result = await advisor.AdviseAsync(ctx, workflow, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(ctx.Has<AnonymousGranted>());
    }

    #endregion

    #region SubmitAnonymous

    [Fact]
    public async Task SubmitAnonymous_NoAttribute_DoesNotSetAnonymousGranted() {
        var advisor = new AdviceSubmitAnonymous();
        var ctx     = CreateContext();
        var request = new WorkflowRequest<IStateful>();

        var result = await advisor.AdviseAsync(ctx, request, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(ctx.Has<AnonymousGranted>());
    }

    #endregion

    #region RaiseAnonymous

    [Fact]
    public async Task RaiseAnonymous_NoAttribute_DoesNotSetAnonymousGranted() {
        var advisor  = new AdviceRaiseAnonymous();
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());
        var request  = new OrderEvent { Event = "Approve" };

        var result = await advisor.AdviseAsync(ctx, workflow, request, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(ctx.Has<AnonymousGranted>());
    }

    #endregion

    #region StatusAuthorize

    [Fact]
    public async Task StatusAuthorize_WithAnonymousGranted_SkipsAccessCheck() {
        var access      = new Mock<IAccessProvider<SchemataWorkflow, Guid>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, Guid>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<Guid>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<SchemataWorkflow, bool>>?)null);

        var advisor = new AdviceStatusAuthorize(access.Object, entitlement.Object);
        var ctx     = CreateContext();
        ctx.Set(new AnonymousGranted());
        var workflow = CreateWorkflow(Guid.NewGuid());

        var result = await advisor.AdviseAsync(ctx, workflow, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task StatusAuthorize_Unauthorized_ThrowsAuthorizationException() {
        var access = new Mock<IAccessProvider<SchemataWorkflow, Guid>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<SchemataWorkflow?>(),
                         It.IsAny<AccessContext<Guid>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, Guid>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<Guid>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<SchemataWorkflow, bool>>?)null);

        var advisor  = new AdviceStatusAuthorize(access.Object, entitlement.Object);
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, workflow, CreateUser()));
    }

    [Fact]
    public async Task StatusAuthorize_Authorized_ReturnsContinue() {
        var access = new Mock<IAccessProvider<SchemataWorkflow, Guid>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<SchemataWorkflow?>(),
                         It.IsAny<AccessContext<Guid>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, Guid>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<Guid>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<SchemataWorkflow, bool>>?)null);

        var advisor  = new AdviceStatusAuthorize(access.Object, entitlement.Object);
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());

        var result = await advisor.AdviseAsync(ctx, workflow, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task StatusAuthorize_EntitlementDenied_ThrowsAuthorizationException() {
        var access      = new Mock<IAccessProvider<SchemataWorkflow, Guid>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, Guid>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<Guid>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync(w => w.Uid == Guid.NewGuid());

        var advisor  = new AdviceStatusAuthorize(access.Object, entitlement.Object);
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, workflow, CreateUser()));
    }

    [Fact]
    public async Task StatusAuthorize_EntitlementDenied_Anonymous_StillThrows() {
        var access      = new Mock<IAccessProvider<SchemataWorkflow, Guid>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, Guid>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<Guid>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync(w => w.Uid == Guid.NewGuid());

        var advisor = new AdviceStatusAuthorize(access.Object, entitlement.Object);
        var ctx     = CreateContext();
        ctx.Set(new AnonymousGranted());
        var workflow = CreateWorkflow(Guid.NewGuid());

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, workflow, CreateUser()));
    }

    #endregion

    #region SubmitAuthorize

    [Fact]
    public async Task SubmitAuthorize_WithAnonymousGranted_SkipsAccessCheck() {
        var access = new Mock<IAccessProvider<SchemataWorkflow, WorkflowRequest<IStateful>>>(MockBehavior.Strict);

        var advisor = new AdviceSubmitAuthorize(access.Object);
        var ctx     = CreateContext();
        ctx.Set(new AnonymousGranted());
        var request = new WorkflowRequest<IStateful>();

        var result = await advisor.AdviseAsync(ctx, request, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task SubmitAuthorize_Unauthorized_ThrowsAuthorizationException() {
        var access = new Mock<IAccessProvider<SchemataWorkflow, WorkflowRequest<IStateful>>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<SchemataWorkflow?>(),
                         It.IsAny<AccessContext<WorkflowRequest<IStateful>>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var advisor = new AdviceSubmitAuthorize(access.Object);
        var ctx     = CreateContext();
        var request = new WorkflowRequest<IStateful>();

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, request, CreateUser()));
    }

    #endregion

    #region RaiseAuthorize

    [Fact]
    public async Task RaiseAuthorize_WithAnonymousGranted_SkipsAccessCheck() {
        var access      = new Mock<IAccessProvider<SchemataWorkflow, ITransition>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, ITransition>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<ITransition>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<SchemataWorkflow, bool>>?)null);

        var advisor = new AdviceRaiseAuthorize(access.Object, entitlement.Object);
        var ctx     = CreateContext();
        ctx.Set(new AnonymousGranted());
        var workflow = CreateWorkflow(Guid.NewGuid());
        var request  = new OrderEvent { Event = "Approve" };

        var result = await advisor.AdviseAsync(ctx, workflow, request, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task RaiseAuthorize_Unauthorized_ThrowsAuthorizationException() {
        var access = new Mock<IAccessProvider<SchemataWorkflow, ITransition>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<SchemataWorkflow?>(),
                         It.IsAny<AccessContext<ITransition>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, ITransition>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<ITransition>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<SchemataWorkflow, bool>>?)null);

        var advisor  = new AdviceRaiseAuthorize(access.Object, entitlement.Object);
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());
        var request  = new OrderEvent { Event = "Approve" };

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, workflow, request, CreateUser())
        );
    }

    [Fact]
    public async Task RaiseAuthorize_Authorized_ReturnsContinue() {
        var access = new Mock<IAccessProvider<SchemataWorkflow, ITransition>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<SchemataWorkflow?>(),
                         It.IsAny<AccessContext<ITransition>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, ITransition>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<ITransition>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<SchemataWorkflow, bool>>?)null);

        var advisor  = new AdviceRaiseAuthorize(access.Object, entitlement.Object);
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());
        var request  = new OrderEvent { Event = "Approve" };

        var result = await advisor.AdviseAsync(ctx, workflow, request, CreateUser());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task RaiseAuthorize_EntitlementDenied_ThrowsAuthorizationException() {
        var access      = new Mock<IAccessProvider<SchemataWorkflow, ITransition>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<SchemataWorkflow, ITransition>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<ITransition>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync(w => w.Uid == Guid.NewGuid());

        var advisor  = new AdviceRaiseAuthorize(access.Object, entitlement.Object);
        var ctx      = CreateContext();
        var workflow = CreateWorkflow(Guid.NewGuid());
        var request  = new OrderEvent { Event = "Approve" };

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, workflow, request, CreateUser())
        );
    }

    #endregion
}
