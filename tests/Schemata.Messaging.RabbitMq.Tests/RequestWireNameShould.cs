using System;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Messaging.RabbitMq.Tests;

public class RequestWireNameShould
{
    [Fact]
    public void Resolve_ARegisteredRequest_ToItsWireNameAndResponseType() {
        var options = new RabbitMqRequestOptions().Register<PriceQuery, decimal>("pricing.quote");

        var binding = options.Require(typeof(PriceQuery));

        Assert.Equal("pricing.quote", binding.Name);
        Assert.Equal(typeof(PriceQuery), binding.Request);
        Assert.Equal(typeof(decimal), binding.Response);
    }

    [Fact]
    public void Resolve_AWireName_BackToItsBinding() {
        var options = new RabbitMqRequestOptions().Register<PriceQuery, decimal>("pricing.quote");

        Assert.Equal(typeof(PriceQuery), options.Resolve("pricing.quote")!.Request);
    }

    [Fact]
    public void Resolve_AnUnknownWireName_ToNothing() {
        // The consumer must be able to ignore traffic it was never configured for rather than throw
        // on someone else's routing key.
        Assert.Null(new RabbitMqRequestOptions().Resolve("nobody.knows"));
    }

    [Fact]
    public void Reject_ARequestTypeThatWasNeverRegistered() {
        // A CLR type name never travels on the wire, so an unregistered request is a configuration
        // error the sender must see immediately, not a name silently derived from the class.
        var error = Assert.Throws<InvalidOperationException>(
            () => new RabbitMqRequestOptions().Require(typeof(PriceQuery)));

        Assert.Contains(typeof(PriceQuery).FullName!, error.Message);
        Assert.Contains("Register", error.Message);
    }

    [Fact]
    public void Expose_EveryRegisteredBinding_SoTheConsumerCanBindItsQueue() {
        var options = new RabbitMqRequestOptions()
                     .Register<PriceQuery, decimal>("pricing.quote")
                     .Register<StockQuery, int>("stock.level");

        Assert.Equal(2, options.Bindings.Count);
        Assert.Contains("pricing.quote", options.Bindings.Keys);
        Assert.Contains("stock.level", options.Bindings.Keys);
    }

    [Fact]
    public void Let_ALaterRegistrationReplaceAnEarlierOneForTheSameRequest() {
        var options = new RabbitMqRequestOptions()
                     .Register<PriceQuery, decimal>("pricing.quote")
                     .Register<PriceQuery, decimal>("pricing.quote.v2");

        Assert.Equal("pricing.quote.v2", options.Require(typeof(PriceQuery)).Name);
    }

    private sealed record PriceQuery(string Product) : IRequest<decimal>;

    private sealed record StockQuery(string Sku) : IRequest<int>;
}
