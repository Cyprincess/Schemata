using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Xunit;

namespace Schemata.Advice.Generator.Tests;

public sealed class AdviceRunnerArityShould
{
    [Fact]
    public async Task Run_Arity1_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity1TestAdvisor>(
            new Arity1Advisor(recorder, 30, AdviseResult.Block),
            new Arity1Advisor(recorder, 20, AdviseResult.Handle),
            new Arity1Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity1TestAdvisor>().RunAsync(new(provider), 1);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 1 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity2_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity2TestAdvisor>(
            new Arity2Advisor(recorder, 30, AdviseResult.Block),
            new Arity2Advisor(recorder, 20, AdviseResult.Handle),
            new Arity2Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity2TestAdvisor>().RunAsync(new(provider), 1, 2);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 2, 1, 2 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity3_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity3TestAdvisor>(
            new Arity3Advisor(recorder, 30, AdviseResult.Block),
            new Arity3Advisor(recorder, 20, AdviseResult.Handle),
            new Arity3Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity3TestAdvisor>().RunAsync(new(provider), 1, 2, 3);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 2, 3, 1, 2, 3 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity4_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity4TestAdvisor>(
            new Arity4Advisor(recorder, 30, AdviseResult.Block),
            new Arity4Advisor(recorder, 20, AdviseResult.Handle),
            new Arity4Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity4TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 2, 3, 4, 1, 2, 3, 4 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity5_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity5TestAdvisor>(
            new Arity5Advisor(recorder, 30, AdviseResult.Block),
            new Arity5Advisor(recorder, 20, AdviseResult.Handle),
            new Arity5Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity5TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 1, 2, 3, 4, 5 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity6_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity6TestAdvisor>(
            new Arity6Advisor(recorder, 30, AdviseResult.Block),
            new Arity6Advisor(recorder, 20, AdviseResult.Handle),
            new Arity6Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity6TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 1, 2, 3, 4, 5, 6 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity7_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity7TestAdvisor>(
            new Arity7Advisor(recorder, 30, AdviseResult.Block),
            new Arity7Advisor(recorder, 20, AdviseResult.Handle),
            new Arity7Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity7TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 1, 2, 3, 4, 5, 6, 7 }, recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity8_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity8TestAdvisor>(
            new Arity8Advisor(recorder, 30, AdviseResult.Block),
            new Arity8Advisor(recorder, 20, AdviseResult.Handle),
            new Arity8Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity8TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 8).Concat(Enumerable.Range(1, 8)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity9_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity9TestAdvisor>(
            new Arity9Advisor(recorder, 30, AdviseResult.Block),
            new Arity9Advisor(recorder, 20, AdviseResult.Handle),
            new Arity9Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity9TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 9).Concat(Enumerable.Range(1, 9)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity10_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity10TestAdvisor>(
            new Arity10Advisor(recorder, 30, AdviseResult.Block),
            new Arity10Advisor(recorder, 20, AdviseResult.Handle),
            new Arity10Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity10TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 10).Concat(Enumerable.Range(1, 10)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity11_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity11TestAdvisor>(
            new Arity11Advisor(recorder, 30, AdviseResult.Block),
            new Arity11Advisor(recorder, 20, AdviseResult.Handle),
            new Arity11Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity11TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 11).Concat(Enumerable.Range(1, 11)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity12_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity12TestAdvisor>(
            new Arity12Advisor(recorder, 30, AdviseResult.Block),
            new Arity12Advisor(recorder, 20, AdviseResult.Handle),
            new Arity12Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity12TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 12).Concat(Enumerable.Range(1, 12)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity13_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity13TestAdvisor>(
            new Arity13Advisor(recorder, 30, AdviseResult.Block),
            new Arity13Advisor(recorder, 20, AdviseResult.Handle),
            new Arity13Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity13TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 13).Concat(Enumerable.Range(1, 13)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity14_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity14TestAdvisor>(
            new Arity14Advisor(recorder, 30, AdviseResult.Block),
            new Arity14Advisor(recorder, 20, AdviseResult.Handle),
            new Arity14Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity14TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 14).Concat(Enumerable.Range(1, 14)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity15_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity15TestAdvisor>(
            new Arity15Advisor(recorder, 30, AdviseResult.Block),
            new Arity15Advisor(recorder, 20, AdviseResult.Handle),
            new Arity15Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity15TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 15).Concat(Enumerable.Range(1, 15)), recorder.Arguments);
    }

    [Fact]
    public async Task Run_Arity16_Advisors_In_Order_And_Short_Circuit() {
        var recorder = new AdvisorRecorder();
        await using var provider = Provider<IArity16TestAdvisor>(
            new Arity16Advisor(recorder, 30, AdviseResult.Block),
            new Arity16Advisor(recorder, 20, AdviseResult.Handle),
            new Arity16Advisor(recorder, 10, AdviseResult.Continue));

        var result = await Advisor.For<IArity16TestAdvisor>().RunAsync(new(provider), 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(new[] { 10, 20 }, recorder.Calls);
        Assert.Equal(Enumerable.Range(1, 16).Concat(Enumerable.Range(1, 16)), recorder.Arguments);
    }

    private static ServiceProvider Provider<TAdvisor>(params TAdvisor[] advisors) where TAdvisor : class, IAdvisor {
        var services = new ServiceCollection();
        foreach (var advisor in advisors) {
            services.AddSingleton(advisor);
        }

        return services.BuildServiceProvider();
    }
}
