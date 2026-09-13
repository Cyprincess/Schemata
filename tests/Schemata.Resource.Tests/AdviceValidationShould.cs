using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Validation.FluentValidation.Advisors;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Tests;

[Trait("Category", "Integration")]
public class AdviceValidationShould
{
    [Fact]
    public async Task Custom_AddFailure_Without_Error_Code_Produces_Validation_Failed_Violation_And_Blocks() {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Name)
                 .Custom((_, ctx) => ctx.AddFailure("Name is bad"));

        using var provider = Provider(validator);
        var errors = new List<ErrorFieldViolation>();

        var collected = await new AdviceValidation<Request>(provider)
                       .AdviseAsync(new AdviceContext(provider), Operations.Create, new Request(), errors);
        Assert.Equal(AdviseResult.Continue, collected);

        var violation = Assert.Single(errors);
        Assert.Equal("name", violation.Field);
        Assert.Equal(ErrorReasons.ValidationFailed, violation.Reason);
        Assert.Equal("Name is bad", violation.Description);

        var blocked = await new AdviceValidationErrors<Request>()
                     .AdviseAsync(new AdviceContext(provider), Operations.Create, new Request(), errors);
        Assert.Equal(AdviseResult.Block, blocked);
    }

    [Fact]
    public async Task Named_Error_Code_Is_Normalized_To_Upper_Snake_Case_Reason() {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Age)
                 .InclusiveBetween(1, 150)
                 .WithErrorCode("orderLimitExceeded");

        var errors = await Collect(validator, new Request { Age = 500 });

        Assert.Equal("ORDER_LIMIT_EXCEEDED", Assert.Single(errors).Reason);
    }

    [Fact]
    public async Task Built_In_Validator_Suffix_Is_Stripped_From_Reason() {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Name)
                 .NotEmpty()
                 .WithMessage("Name is required");

        var errors = await Collect(validator, new Request());

        var violation = Assert.Single(errors);
        Assert.Equal("name", violation.Field);
        Assert.Equal("NOT_EMPTY", violation.Reason);
        Assert.Equal("Name is required", violation.Description);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("Validator")]
    public async Task Blank_Or_Bare_Validator_Error_Code_Falls_Back_To_Validation_Failed_Reason(string errorCode) {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Age)
                 .InclusiveBetween(1, 150)
                 .WithErrorCode(errorCode);

        var errors = await Collect(validator, new Request { Age = 500 });

        Assert.Equal(ErrorReasons.ValidationFailed, Assert.Single(errors).Reason);
    }

    [Fact]
    public async Task Empty_Validation_Failure_Error_Code_Falls_Back_To_Validation_Failed_Reason() {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Name)
                 .Custom((_, ctx) => ctx.AddFailure(new ValidationFailure("Name", "Name is bad") { ErrorCode = "" }));

        var errors = await Collect(validator, new Request());

        Assert.Equal(ErrorReasons.ValidationFailed, Assert.Single(errors).Reason);
    }

    private static ServiceProvider Provider(IValidator<Request> validator) {
        return new ServiceCollection()
              .AddSingleton(validator)
              .BuildServiceProvider();
    }

    private static async Task<List<ErrorFieldViolation>> Collect(IValidator<Request> validator, Request request) {
        using var provider = Provider(validator);
        var errors = new List<ErrorFieldViolation>();

        var result = await new AdviceValidation<Request>(provider)
                    .AdviseAsync(new AdviceContext(provider), Operations.Create, request, errors);

        Assert.Equal(AdviseResult.Continue, result);
        return errors;
    }

    private sealed class Request
    {
        public string? Name { get; set; }
        public int     Age  { get; set; }
    }
}
