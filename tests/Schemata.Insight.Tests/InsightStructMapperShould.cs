using System.Collections.Generic;
using System.Linq;
using Schemata.Insight.Grpc;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class InsightStructMapperShould
{
    [Fact]
    public void Maps_A_Flat_Row_To_Typed_Value_Slots() {
        var row = new Dictionary<string, object?> {
            ["name"]   = "Ada",
            ["age"]    = 36,
            ["score"]  = 1.5,
            ["active"] = true,
            ["note"]   = null,
        };

        var result = InsightStructMapper.ToStruct(row);

        Assert.Equal("Ada", result.Fields["name"].StringValue);
        Assert.Equal(36, result.Fields["age"].IntValue);
        Assert.Equal(1.5, result.Fields["score"].NumberValue);
        Assert.True(result.Fields["active"].BoolValue);
        Assert.True(result.Fields["note"].NullValue);
    }

    [Fact]
    public void Maps_A_Nested_Child_List_To_A_List_Of_Structs() {
        var row = new Dictionary<string, object?> {
            ["full_name"] = "Ada",
            ["orders"] = new List<IReadOnlyDictionary<string, object?>> {
                new Dictionary<string, object?> { ["id"] = 1 },
                new Dictionary<string, object?> { ["id"] = 2 },
            },
        };

        var result = InsightStructMapper.ToStruct(row);

        var orders = result.Fields["orders"].ListValue;
        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.Equal(1, orders[0].StructValue!.Fields["id"].IntValue);
        Assert.Equal(2, orders[1].StructValue!.Fields["id"].IntValue);
    }

    [Fact]
    public void Round_Trips_The_Request_Graph() {
        var message = new QueryInsightGrpcRequest {
            Sources = {
                new() { Alias = "c", Name = "customers" },
                new() { Alias = "o", Name = "orders" },
            },
            Joins = {
                new() { Left = "c", Right = "o", Kind = JoinKind.Inner, On = new() { Source = "c.id == o.customer_id", Language = "cel" } },
            },
            Transformations = {
                new() { Filter = new() { Source = "o.status == 'paid'", Language = "cel" } },
                new() { OrderBy = "o.amount desc" },
            },
            Selections = {
                new() { Field = "c.full_name", Alias = "full_name" },
            },
            PageSize = 10,
            Language = "aip",
        };

        var request = InsightStructMapper.ToRequest(message);

        Assert.Equal(["customers", "orders"], request.Sources.Select(s => s.Name));
        var join = Assert.Single(request.Joins);
        Assert.Equal(JoinKind.Inner, join.Kind);
        Assert.Equal("c.id == o.customer_id", join.On.Source);
        Assert.Equal(2, request.Transformations.Count);
        Assert.Equal("o.amount desc", request.Transformations[1].OrderBy!.OrderBy);
        Assert.Equal("full_name", Assert.Single(request.Selections).Alias);
        Assert.Equal(10, request.PageSize);
    }
}
