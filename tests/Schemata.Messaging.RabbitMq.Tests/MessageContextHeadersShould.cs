using System.Collections.Generic;
using System.Text;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Messaging.RabbitMq.Tests;

public class MessageContextHeadersShould
{
    [Fact]
    public void RoundTrip_ItemsThroughAmqpHeaders() {
        var context = new MessageContext(new Dictionary<string, string?> {
            ["tenancy.tenant"] = "acme",
            ["locale"]         = "zh-CN",
        });

        var restored = MessageContextHeaders.Read(MessageContextHeaders.Write(context));

        Assert.Equal("acme", restored["tenancy.tenant"]);
        Assert.Equal("zh-CN", restored["locale"]);
    }

    [Fact]
    public void Decode_HeaderValuesTheBrokerDeliversAsByteArrays() {
        // AMQP hands string headers back as byte[]; decoding them as ToString() would yield
        // "System.Byte[]" and silently corrupt every propagated value.
        var headers = new Dictionary<string, object?> {
            [MessageContextHeaders.Prefix + "tenancy.tenant"] = Encoding.UTF8.GetBytes("acme"),
        };

        Assert.Equal("acme", MessageContextHeaders.Read(headers)["tenancy.tenant"]);
    }

    [Fact]
    public void Ignore_HeadersThatAreNotPropagatedContext() {
        var headers = new Dictionary<string, object?> {
            [MessageContextHeaders.Prefix + "locale"] = Encoding.UTF8.GetBytes("zh-CN"),
            ["x-death"]                               = Encoding.UTF8.GetBytes("something-else"),
            ["traceparent"]                           = Encoding.UTF8.GetBytes("00-abc-def-01"),
        };

        var restored = MessageContextHeaders.Read(headers);

        Assert.Equal(["locale"], restored.Keys);
    }

    [Fact]
    public void Write_NoHeadersAtAll_ForAnEmptyContext() {
        // An application with no propagator registered must pay nothing on the wire.
        Assert.Null(MessageContextHeaders.Write(new(new Dictionary<string, string?>())));
    }

    [Fact]
    public void Write_NoHeadersAtAll_ForANullContext() {
        Assert.Null(MessageContextHeaders.Write(null));
    }

    [Fact]
    public void Carry_ANullItem_AsAnAbsentHeader() {
        // Absent and empty-string must stay distinguishable on the far side.
        var context = new MessageContext(new Dictionary<string, string?> {
            ["set"]   = string.Empty,
            ["unset"] = null,
        });

        var restored = MessageContextHeaders.Read(MessageContextHeaders.Write(context));

        Assert.Equal(string.Empty, restored["set"]);
        Assert.DoesNotContain("unset", restored.Keys);
    }

    [Fact]
    public void Read_NothingFromAbsentHeaders() {
        Assert.Empty(MessageContextHeaders.Read(null));
    }
}
