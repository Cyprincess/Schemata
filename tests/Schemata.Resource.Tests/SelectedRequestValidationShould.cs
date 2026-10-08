using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Validation.Skeleton.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Resource.Foundation.Commands;
using Xunit;

namespace Schemata.Resource.Tests;

public sealed class SelectedRequestValidationShould
{
    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_Each_Payload_Validator_Exactly_Once_In_Either_Registration_Order(bool genericFirst) {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        services.AddSingleton(Mock.Of<Schemata.Caching.Skeleton.ICacheProvider>());
        var runs = 0;
        ResourceValidator.Runs = 0;
        var validator = new InlineValidator<ResourcePayload>();
        validator.RuleFor(value => value.Name).Custom((_, _) => runs++);
        services.AddSingleton<IValidator<ResourcePayload>>(validator);
        services.AddValidator<ResourcePayload, ResourceValidator>();
        if (genericFirst) services.AddRequestValidation<CreateResourceRequest<ResourceEntity, ResourcePayload, ResourceEntity>, CreateResultBase<ResourceEntity>>(Operations.Create);
        new SchemataResourceBuilder(options, services).Use<ResourceEntity, ResourcePayload, ResourceEntity, ResourceEntity>();
        if (!genericFirst) services.AddRequestValidation<CreateResourceRequest<ResourceEntity, ResourcePayload, ResourceEntity>, CreateResultBase<ResourceEntity>>(Operations.Create);
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<NoContentException>(() => new InProcessRequestDispatcher(provider)
            .SendAsync<CreateResourceRequest<ResourceEntity, ResourcePayload, ResourceEntity>, CreateResultBase<ResourceEntity>>(new(new() { ValidateOnly = true }, null)));
        Assert.Equal(1, runs);
        Assert.Equal(1, ResourceValidator.Runs);
    }

    [CanonicalName("validation-resources/{resource}")]
    public sealed class ResourceEntity : ICanonicalName {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }
    public sealed class ResourcePayload : ICanonicalName, IValidation {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public bool ValidateOnly { get; set; }
    }
    public sealed class ResourceValidator : AbstractValidator<ResourcePayload>
    {
        public static int Runs;

        public ResourceValidator() { RuleFor(value => value.Name).Custom((_, _) => Runs++); }
    }

    [Fact]
    public async Task Validate_Selected_Plain_Request_Asynchronously_Without_Constructing_Unrelated_Wraps() {
        var services = new ServiceCollection();
        services.AddValidator<Plain, PlainValidator>();
        services.AddRequestValidation<Plain, string>(Operations.Create);
        services.AddScoped<IRequestPipelineAdvisor<Plain, string>>(_ => throw new InvalidOperationException("Unselected wrap constructed"));
        var handler = new Mock<IRequestHandler<Plain, string>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<Plain>(), It.IsAny<CancellationToken>())).ReturnsAsync("accepted");
        services.AddSingleton(handler.Object);
        using var provider = services.BuildServiceProvider();
        var dispatcher = new InProcessRequestDispatcher(provider);
        var error = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(() => dispatcher.SendAsync<Plain, string>(new("")));
        Assert.Equal("name", Assert.Single(Assert.Single(error.Details!.OfType<BadRequestDetail>()).FieldViolations!).Field);
        handler.Verify(value => value.HandleAsync(It.IsAny<Plain>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("accepted", await dispatcher.SendAsync<Plain, string>(new("valid")));
    }

    [Fact]
    public async Task Leave_Unselected_Plain_Request_Unvalidated() {
        var services = new ServiceCollection();
        services.AddValidator<Plain, PlainValidator>();
        var handler = new Mock<IRequestHandler<Plain, string>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<Plain>(), It.IsAny<CancellationToken>())).ReturnsAsync("unselected");
        services.AddSingleton(handler.Object);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("unselected", await new InProcessRequestDispatcher(provider).SendAsync<Plain, string>(new("")));
    }

    [Fact]
    public async Task Accumulate_Multiple_Collectors_Before_Final_Block_For_Selected_Command() {
        var services = new ServiceCollection();
        services.AddRequestValidation<Command, string>(Operations.Create);
        services.AddSingleton<IValidationAdvisor<Command>>(Collector("first", 1));
        services.AddSingleton<IValidationAdvisor<Command>>(Collector("second", 2));
        using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(() => new InProcessRequestDispatcher(provider).SendAsync<Command, string>(new()));
        Assert.Equal(new[] { "first", "second" }, Assert.Single(error.Details!.OfType<BadRequestDetail>()).FieldViolations!.Select(value => value.Field));
    }

    [Fact]
    public async Task Reject_Selected_Stream_Before_Handler_And_Dispose_Validation_Scope() {
        var disposed = 0;
        var services = new ServiceCollection();
        services.AddSchemataStreams();
        services.AddScoped(_ => new ValidationLease(() => disposed++));
        services.AddValidator<StreamRequest, StreamValidator>();
        services.AddStreamValidation<StreamRequest, string>(Operations.List);
        services.AddScoped<IStreamRequestHandler<StreamRequest, string>>(_ => throw new InvalidOperationException("Handler must stay lazy"));
        using var provider = services.BuildServiceProvider();
        var sequence = provider.GetRequiredService<IStreamDispatcher>().Stream<StreamRequest, string>(new());
        Assert.Equal(0, disposed);
        await using var enumerator = sequence.GetAsyncEnumerator();
        await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(() => enumerator.MoveNextAsync().AsTask());
        Assert.Equal(1, disposed);
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Aggregate_Violations_From_Every_Cooperating_Validator_In_Registration_Order(bool emailFirst) {
        var services = new ServiceCollection();
        if (emailFirst) {
            services.AddValidator<Contact, ContactEmailValidator>();
            services.AddValidator<Contact, ContactAgeValidator>();
        } else {
            services.AddValidator<Contact, ContactAgeValidator>();
            services.AddValidator<Contact, ContactEmailValidator>();
        }
        services.AddRequestValidation<Contact, string>(Operations.Create);
        var handler = new Mock<IRequestHandler<Contact, string>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>())).ReturnsAsync("accepted");
        services.AddSingleton(handler.Object);
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(
            () => new InProcessRequestDispatcher(provider).SendAsync<Contact, string>(new("", 500)));

        var violations = Assert.Single(error.Details!.OfType<BadRequestDetail>()).FieldViolations!;
        Assert.Equal(emailFirst ? new[] { "email", "age" } : new[] { "age", "email" },
                     violations.Select(value => value.Field));
        handler.Verify(value => value.HandleAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Run_A_Repeatedly_Installed_Validator_Only_Once() {
        var services = new ServiceCollection();
        services.AddValidator<Plain, PlainValidator>();
        services.AddValidator<Plain, PlainValidator>();
        services.AddRequestValidation<Plain, string>(Operations.Create);
        var handler = new Mock<IRequestHandler<Plain, string>>();
        services.AddSingleton(handler.Object);
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(
            () => new InProcessRequestDispatcher(provider).SendAsync<Plain, string>(new("")));

        Assert.Single(Assert.Single(error.Details!.OfType<BadRequestDetail>()).FieldViolations!);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Register_Every_Closed_Interface_Of_A_Multi_Validator_Implementation() {
        var services = new ServiceCollection();
        services.AddValidator<MultiValidator>();
        services.AddRequestValidation<MultiName, string>(Operations.Create);
        services.AddRequestValidation<MultiCode, string>(Operations.Create);
        var nameHandler = new Mock<IRequestHandler<MultiName, string>>();
        nameHandler.Setup(value => value.HandleAsync(It.IsAny<MultiName>(), It.IsAny<CancellationToken>())).ReturnsAsync("name-accepted");
        services.AddSingleton(nameHandler.Object);
        var codeHandler = new Mock<IRequestHandler<MultiCode, string>>();
        codeHandler.Setup(value => value.HandleAsync(It.IsAny<MultiCode>(), It.IsAny<CancellationToken>())).ReturnsAsync("code-accepted");
        services.AddSingleton(codeHandler.Object);
        using var provider = services.BuildServiceProvider();
        var dispatcher = new InProcessRequestDispatcher(provider);

        var nameError = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(
            () => dispatcher.SendAsync<MultiName, string>(new("")));
        Assert.Equal("name", Assert.Single(Assert.Single(nameError.Details!.OfType<BadRequestDetail>()).FieldViolations!).Field);

        var codeError = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(
            () => dispatcher.SendAsync<MultiCode, string>(new("")));
        Assert.Equal("code", Assert.Single(Assert.Single(codeError.Details!.OfType<BadRequestDetail>()).FieldViolations!).Field);

        Assert.Equal("name-accepted", await dispatcher.SendAsync<MultiName, string>(new("ok")));
        Assert.Equal("code-accepted", await dispatcher.SendAsync<MultiCode, string>(new("ok")));
    }

    public sealed record Contact(string Email, int Age) : IRequest<string>;

    public sealed class ContactEmailValidator : AbstractValidator<Contact>
    {
        public ContactEmailValidator() { RuleFor(value => value.Email).NotEmpty(); }
    }

    public sealed class ContactAgeValidator : AbstractValidator<Contact>
    {
        public ContactAgeValidator() { RuleFor(value => value.Age).InclusiveBetween(0, 150); }
    }

    public sealed record MultiName(string Name) : IRequest<string>;
    public sealed record MultiCode(string Code) : IRequest<string>;

    public sealed class MultiValidator : IValidator<MultiName>, IValidator<MultiCode>
    {
        public Task<ValidationResult> ValidateAsync(IValidationContext context, CancellationToken cancellation) {
            ValidationFailure? failure = context.InstanceToValidate switch {
                MultiName { Name: "" } => new("name", "required"),
                MultiCode { Code: "" } => new("code", "required"),
                _                      => null,
            };

            return Task.FromResult(failure is null ? new ValidationResult() : new ValidationResult([failure]));
        }

        public ValidationResult Validate(IValidationContext context) {
            return ValidateAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        public IValidatorDescriptor CreateDescriptor() { throw new NotSupportedException(); }

        public bool CanValidateInstancesOfType(Type type) { return type == typeof(MultiName) || type == typeof(MultiCode); }

        public ValidationResult Validate(MultiName instance) { return Validate(new ValidationContext<MultiName>(instance)); }

        public Task<ValidationResult> ValidateAsync(MultiName instance, CancellationToken cancellation) {
            return ValidateAsync(new ValidationContext<MultiName>(instance), cancellation);
        }

        public ValidationResult Validate(MultiCode instance) { return Validate(new ValidationContext<MultiCode>(instance)); }

        public Task<ValidationResult> ValidateAsync(MultiCode instance, CancellationToken cancellation) {
            return ValidateAsync(new ValidationContext<MultiCode>(instance), cancellation);
        }
    }

    public sealed record StreamRequest : IStreamRequest<string>;
    public sealed class ValidationLease(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    public sealed class StreamValidator : AbstractValidator<StreamRequest>
    {
        public StreamValidator(ValidationLease lease) {
            RuleFor(value => value).CustomAsync(async (_, context, ct) => {
                await Task.Yield(); ct.ThrowIfCancellationRequested(); context.AddFailure("request", "rejected");
            });
        }
    }

    private static IValidationAdvisor<Command> Collector(string field, int order) {
        var advisor = new Mock<IValidationAdvisor<Command>>();
        advisor.SetupGet(value => value.Order).Returns(order);
        advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), Operations.Create, It.IsAny<Command>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
            .Callback<AdviceContext, Operations, Command, IList<ErrorFieldViolation>, CancellationToken>((_, _, _, errors, _) => errors.Add(new() { Field = field, Reason = "REQUIRED" }))
            .ReturnsAsync(AdviseResult.Continue);
        return advisor.Object;
    }

    public sealed record Plain(string Name) : IRequest<string>;
    public sealed record Command : ICommand<string>;
    public sealed class PlainValidator : AbstractValidator<Plain>
    {
        public PlainValidator() { RuleFor(value => value.Name).MustAsync(async (name, ct) => { await Task.Yield(); ct.ThrowIfCancellationRequested(); return !string.IsNullOrEmpty(name); }); }
    }
}
