using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Xunit;
using ValidateOne = Schemata.Advice.Generator.Tests.Namespaces.NamespaceOne.IValidate;
using ValidateTwo = Schemata.Advice.Generator.Tests.Namespaces.NamespaceTwo.IValidate;

namespace Schemata.Advice.Generator.Tests;

public sealed class ValidateRouteShould
{
    [Fact]
    public async Task Route_RunAsync_For_Partial_Interface_In_NamespaceOne() {
        var advisor = new Mock<ValidateOne>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Handle);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();

        var result = await Advisor.For<ValidateOne>().RunAsync(new(provider), "payload");

        Assert.Equal(AdviseResult.Handle, result);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Route_RunAsync_For_Partial_Interface_In_NamespaceTwo() {
        var advisor = new Mock<ValidateTwo>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Block);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();

        var result = await Advisor.For<ValidateTwo>().RunAsync(new(provider), "payload");

        Assert.Equal(AdviseResult.Block, result);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Route_Containing_Type_And_Interface_Generic_Arguments() {
        var advisor = new Mock<Namespaces.Outer<string>.IValidate<int>>();
        advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), "outer", 7, It.IsAny<CancellationToken>())).ReturnsAsync(AdviseResult.Handle);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();
        Assert.Equal(AdviseResult.Handle, await Advisor.For<Namespaces.Outer<string>.IValidate<int>>().RunAsync(new(provider), "outer", 7));
    }
}