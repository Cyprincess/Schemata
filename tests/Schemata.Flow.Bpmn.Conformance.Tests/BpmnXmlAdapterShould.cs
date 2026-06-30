using System;
using System.IO;
using System.Linq;
using Schemata.Flow.Bpmn.Conformance.Tests.Adapters;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Conformance.Tests;

public class BpmnXmlAdapterShould
{
    [Fact]
    public void Parse_SupportedLinearProcess_CreatesProcessDefinition() {
        var path = WriteVector("""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="linear" name="Linear">
                <bpmn:startEvent id="start" name="Start" />
                <bpmn:task id="task" name="Task" />
                <bpmn:endEvent id="end" name="End" />
                <bpmn:sequenceFlow id="flow1" sourceRef="start" targetRef="task" />
                <bpmn:sequenceFlow id="flow2" sourceRef="task" targetRef="end" />
              </bpmn:process>
            </bpmn:definitions>
            """);

        var definition = BpmnXmlAdapter.Parse(path);

        Assert.Equal("linear", definition.Name);
        Assert.Collection(definition.Elements,
                          element => Assert.Equal(EventPosition.Start, Assert.IsType<FlowEvent>(element).Position),
                          element => Assert.IsType<NoneTask>(element),
                          element => Assert.Equal(EventPosition.End, Assert.IsType<FlowEvent>(element).Position));
        Assert.Equal(["start", "task", "end"], definition.Elements.Select(element => element.Name).ToArray());
        Assert.Equal(["start->task", "task->end"],
                     definition.Flows.Select(flow => $"{flow.Source.Name}->{flow.Target.Name}").ToArray());
    }

    [Fact]
    public void Parse_UnsupportedComplexGateway_ThrowsPendingMessage() {
        var path = WriteVector("""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="complex">
                <bpmn:startEvent id="start" />
                <bpmn:complexGateway id="gateway" />
                <bpmn:endEvent id="end" />
              </bpmn:process>
            </bpmn:definitions>
            """);

        var ex = Assert.Throws<NotSupportedException>(() => BpmnXmlAdapter.Parse(path));

        Assert.Contains("complexGateway", ex.Message, StringComparison.Ordinal);
        Assert.Contains("mark vector as Pending", ex.Message, StringComparison.Ordinal);
    }

    private static string WriteVector(string xml) {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bpmn");
        File.WriteAllText(path, xml);
        return path;
    }
}
