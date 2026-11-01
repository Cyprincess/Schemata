using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Xunit;

[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace Schemata.Advice.Generator.Tests;

public sealed class AdvicePipelineGenerationShould
{
    [Fact]
    public async Task Generate_RunAsync_For_Directly_Derived_Advisor_Interface() {
        var advisor = new Mock<IDirectTestAdvisor>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Handle);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();

        var result = await Advisor.For<IDirectTestAdvisor>().RunAsync(new(provider), "payload");

        Assert.Equal(AdviseResult.Handle, result);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_RunAsync_For_Indirectly_Derived_Advisor_Interface() {
        var advisor = new Mock<IIndirectTestAdvisor>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Block);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();

        var result = await Advisor.For<IIndirectTestAdvisor>().RunAsync(new(provider), "payload");

        Assert.Equal(AdviseResult.Block, result);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_An_Accessible_Extension_For_An_Internal_Advisor_Interface() {
        var advisor = new Mock<IInternalTestAdvisor>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Handle);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();

        var result = await Advisor.For<IInternalTestAdvisor>().RunAsync(new(provider), "payload");

        Assert.Equal(AdviseResult.Handle, result);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_Escaped_Keyword_Generic_Parameters_For_An_Advisor_Interface() {
        var advisor = new Mock<IKeywordAdvisor<string>>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Block);
        await using var provider = new ServiceCollection().AddSingleton(advisor.Object).BuildServiceProvider();

        var result = await Advisor.For<IKeywordAdvisor<string>>().RunAsync(new(provider), "payload");

        Assert.Equal(AdviseResult.Block, result);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), "payload", It.IsAny<CancellationToken>()), Times.Once);
    }
}
