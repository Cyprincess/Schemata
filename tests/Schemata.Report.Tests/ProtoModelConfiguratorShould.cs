using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Insight.Skeleton;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Report.Tests;

public class ProtoModelConfiguratorShould
{
    [Fact]
    public void Configures_Cyclic_Wire_Types_Without_Freezing_Model() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;

        SchemataProtoModelConfigurator.ConfigureType(model, typeof(QueryInsightRequest));

        Assert.True(model.IsDefined(typeof(QueryInsightRequest)));
        Assert.True(model.IsDefined(typeof(SelectionSpec)));
    }

    [Fact]
    public void Configures_Same_Wire_Type_Graph_Twice() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;

        SchemataProtoModelConfigurator.ConfigureType(model, typeof(QueryInsightRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(QueryInsightRequest));

        Assert.True(model.IsDefined(typeof(QueryInsightRequest)));
        Assert.True(model.IsDefined(typeof(SelectionSpec)));
    }

    [Fact]
    public async Task Configures_Concurrent_Wire_Type_Graphs_Without_Duplicate_Type() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();

        Task Configure() {
            return Task.Run(() => {
                ready.Signal();
                start.Wait();
                SchemataProtoModelConfigurator.ConfigureType(model, typeof(QueryInsightRequest));
            });
        }

        var first  = Configure();
        var second = Configure();
        ready.Wait();
        start.Set();
        await Task.WhenAll(first, second);

        Assert.True(model.IsDefined(typeof(QueryInsightRequest)));
        Assert.True(model.IsDefined(typeof(SelectionSpec)));
    }
}
