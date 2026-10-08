using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Schemata.Validation.Skeleton.Advisors;
using Xunit;

namespace Schemata.Resource.Tests;

/// <summary>
///     Dry-run (<see cref="IValidation.ValidateOnly" />) terminates at the independent terminator
///     stage regardless of validation installation: with validators installed a failing request
///     reports field violations, a passing request produces NO_CONTENT without any write, and
///     with the validation stage uninstalled (<c>WithoutCreateValidation</c>) or suppressed for a
///     single request the validator is never consulted while the terminator still ends the
///     dispatch.
/// </summary>
[Trait("Category", "Integration")]
public class ResourceDryRunShould
{
    [Trait("Layer", "Component")]
    [Fact]
    public async Task ValidateOnly_WithBlockingValidator_ThrowsValidationException_WithFieldViolation() {
        var host = Compose();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => host.Dispatch(new() { ValidateOnly = true }));

        var badRequest = ex.Details?.OfType<BadRequestDetail>().Single();
        var violations = badRequest?.FieldViolations;
        Assert.NotNull(violations);
        Assert.Contains(violations!, violation => violation.Field == nameof(Request.DisplayName));
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task ValidateOnly_WithPassingValidator_ThrowsNoContent_WithoutInvokingMutation() {
        var host = Compose();

        await Assert.ThrowsAsync<NoContentException>(
            () => host.Dispatch(new() { ValidateOnly = true, DisplayName = "Widget" }));

        Assert.Equal(1, host.Validator.Invoked);
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateOnly_WithoutValidationStage_StillTerminates_WithoutConsultingValidator(bool withoutFirst) {
        var host = Compose(installValidation: false, withoutFirst: withoutFirst);

        await Assert.ThrowsAsync<NoContentException>(
            () => host.Dispatch(new() { ValidateOnly = true }));

        Assert.Equal(0, host.Validator.Invoked);
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task ValidateOnly_WithSuppressionMarker_SkipsValidator_StillTerminates() {
        var host = Compose(plantSuppression: true);

        await Assert.ThrowsAsync<NoContentException>(
            () => host.Dispatch(new() { ValidateOnly = true }));

        Assert.Equal(0, host.Validator.Invoked);
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Host Compose(bool installValidation = true, bool withoutFirst = true, bool plantSuppression = false) {
        var services = new ServiceCollection();
        var schemata = new SchemataOptions();
        services.AddSchemataResources(schemata);

        var builder = new SchemataResourceBuilder(schemata, services);
        if (!installValidation && withoutFirst) {
            builder.WithoutCreateValidation();
        }

        builder.Use<Entity, Request, Detail, Summary>();
        if (!installValidation && !withoutFirst) {
            builder.WithoutCreateValidation();
        }

        var repository = new Mock<IRepository<Entity>>(MockBehavior.Loose);
        services.AddSingleton(repository.Object);
        services.AddSingleton(new Mock<ISimpleMapper>(MockBehavior.Loose).Object);
        services.AddSingleton(Mock.Of<ICacheProvider>());
        var mutation = ResourceMutationMock.Create<Entity>();
        services.AddSingleton(mutation.Object);

        var validator = new BlockingValidationAdvisor();
        services.AddSingleton<IValidationAdvisor<Request>>(validator);

        if (plantSuppression) {
            services.AddSingleton<IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
                new SuppressCreateValidationAdvisor());
        }

        return new(services.BuildServiceProvider(), mutation, validator);
    }

    private sealed record Host(ServiceProvider Provider, Mock<IResourceMutation<Entity>> Mutation, BlockingValidationAdvisor Validator)
    {
        public async Task Dispatch(Request request) {
            using var scope = Provider.CreateScope();
            var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);
            await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
                new(request, null));
        }
    }

    private sealed class BlockingValidationAdvisor : IValidationAdvisor<Request>
    {
        public int Order => 0;

        public int Invoked { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                ctx,
            Operations                   operation,
            Request                      request,
            IList<ErrorFieldViolation>   errors,
            CancellationToken            ct = default
        ) {
            Invoked++;
            if (string.IsNullOrEmpty(request.DisplayName)) {
                errors.Add(new() { Field = nameof(Request.DisplayName), Description = "Display name is required." });
                return Task.FromResult(AdviseResult.Block);
            }

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class SuppressCreateValidationAdvisor
        : IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>
    {
        public int Order => SecurityOrders.Sanitize + 1;

        public Task<CreateResultBase<Detail>> AdviseAsync(
            AdviceContext                                               ctx,
            CreateResourceRequest<Entity, Request, Detail>              request,
            RequestHandlerContinuation<CreateResultBase<Detail>>        next,
            CancellationToken                                           ct
        ) {
            ctx.Set(new CreateRequestValidationSuppressed());
            return next(ct);
        }
    }

    [CanonicalName("widgets/{widget}")]
    public sealed class Entity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName, IValidation
    {
        public bool    ValidateOnly  { get; set; }
        public string? DisplayName   { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Detail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
