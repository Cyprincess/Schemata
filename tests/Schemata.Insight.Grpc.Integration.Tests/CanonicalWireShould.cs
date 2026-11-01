using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Schemata.Transport.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;
using Schemata.Insight.Grpc.Wire;
using Schemata.Insight.Skeleton.Models;
using Xunit;
using Schemata.Common;

namespace Schemata.Insight.Grpc.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class CanonicalWireShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Grouped_Map_Keys_Keep_Ordinary_Dotted_Traversal_And_Sorted_Values() {
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "b", Name = "buyers" } }, Selections = { new() {
                Field = "b.orders", Alias = "orders", Transformations = [
                    new() { IsGroupBy = true, GroupByKeys = ["o.meta"], GroupByAggregations = [] },
                    new() { OrderBy = "meta.code asc" },
                ], Selections = [new() { Field = "o.meta", Alias = "meta" }, new() { Field = "meta.code", Alias = "code" }],
            } },
        });
        var response = await call.ResponseAsync;
        Assert.Equal(2, response.Rows.Count);
        Assert.All(response.Rows, row => {
            var children = row.Fields["orders"].ListValue!.Values;
            Assert.Equal(new[] { "A", "Z" }, children.Select(child => child.StructValue!.Fields["code"].StringValue));
            Assert.Equal(new[] { "A", "Z" }, children.Select(child => child.StructValue!.Fields["meta"].StructValue!.Fields["code"].StringValue));
        });
        using var client = factory.CreateClient();
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "b", name = "buyers" } }, selections = new[] { new { field = "b.orders", alias = "orders",
                transformations = new object[] { new { group_by = new { keys = new[] { "o.meta" }, aggregations = new object[] { } } },
                    new { order_by = new { order_by = "meta.code asc" } } },
                selections = new[] { new { field = "o.meta", alias = "meta" }, new { field = "meta.code", alias = "code" } } } },
        });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(json.GetProperty("rows").EnumerateArray(), row => {
            Assert.Equal(new object?[] { "A", "Z" }, row.GetProperty("orders").EnumerateArray().Select(child => ScalarPayloadConverter.ReadValue(child.GetProperty("code"))));
            Assert.Equal(new[] { "A", "Z" }, row.GetProperty("orders").EnumerateArray().Select(child => child.GetProperty("meta").GetProperty("code").GetString()));
        });
    }

    [Fact]
    public async Task Group_Output_Named_As_Qualifier_Remains_A_Bare_Scalar_And_Qualified_Field() {
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "b", Name = "buyers" } }, Selections = { new() {
                Field = "b.orders", Alias = "orders", Transformations = [
                    new() { IsGroupBy = true, GroupByKeys = ["o.status"], GroupByAggregations = [new() { Field = "o.amount", Function = AggregationFunction.Sum, Alias = "o" }] },
                    new() { Filter = new() { Source = "o.o > 0.0 && o > 0.0", Language = "cel" } },
                    new() { OrderBy = "o.o desc" },
                    new() { Compute = [new() { Alias = "sum", Expression = new() { Source = "o.o + o", Language = "cel" } }] },
                ], Selections = [new() { Field = "o.o", Alias = "qualified" }, new() { Field = "o", Alias = "bare" },
                    new() { Alias = "expression", Expression = new() { Source = "o", Language = "cel" } }, new() { Field = "sum" },
                    new() { Alias = "local", Expression = new() { Source = "[{'o':7}].map(o, o.o)[0]", Language = "cel" } }],
            } },
        });
        var response = await call.ResponseAsync;
        Assert.Equal(2, response.Rows.Count);
        Assert.All(response.Rows, row => {
            var fields = Assert.Single(row.Fields["orders"].ListValue!.Values).StructValue!.Fields;
            Assert.Equal(300d, fields["qualified"].NumberValue);
            Assert.Equal(300d, fields["bare"].NumberValue);
            Assert.Equal(300d, DynamicValueMapper.FromDynamic(fields["expression"]));
            Assert.Equal(600d, DynamicValueMapper.FromDynamic(fields["sum"]));
            Assert.Equal(7L, DynamicValueMapper.FromDynamic(fields["local"]));
        });
        using var client = factory.CreateClient();
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "b", name = "buyers" } }, selections = new[] { new { field = "b.orders", alias = "orders",
                transformations = new object[] {
                    new { group_by = new { keys = new[] { "o.status" }, aggregations = new[] { new { field = "o.amount", function = "sum", alias = "o" } } } },
                    new { filter = new { predicate = new { source = "o.o > 0.0 && o > 0.0", language = "cel" } } },
                }, selections = new object[] { new { field = "o.o", alias = "qualified" }, new { field = "o", alias = "bare" },
                    new { alias = "expression", expression = new { source = "o", language = "cel" } } } } },
        });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(json.GetProperty("rows").EnumerateArray(), row => {
            var child = row.GetProperty("orders")[0];
            Assert.Equal(300d, child.GetProperty("qualified").GetDouble());
            Assert.Equal(300d, child.GetProperty("bare").GetDouble());
            Assert.Equal(300d, ScalarPayloadConverter.ReadValue(child.GetProperty("expression")));
        });
    }

    [Fact]
    public async Task Declared_Source_Alias_And_Grouped_Expressions_Keep_Their_Public_Bindings() {
        using var channel = factory.CreateGrpcChannel();
        using var collision = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "full_name", Name = "buyers" } },
            Selections = { new() { Field = "full_name.id", Alias = "id" },
                new() { Alias = "computed", Expression = new() { Source = "full_name.id + 1", Language = "cel" } } },
        });
        var projected = await collision.ResponseAsync;
        Assert.Equal(FieldType.Int64, projected.Schema.Single(field => field.Name == "id").Type);
        Assert.Equal(new long?[] { 1, 2 }, projected.Rows.Select(row => row.Fields["id"].IntValue).OrderBy(value => value));
        Assert.All(projected.Rows, row => Assert.Null(row.Fields["id"].TypeLabel));
        using var groupedCall = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "b", Name = "buyers" } }, Selections = { new() {
                Field = "b.orders", Alias = "orders", Transformations = [
                    new() { IsGroupBy = true, GroupByKeys = ["o.status"], GroupByAggregations = [new() { Field = "o.amount", Function = AggregationFunction.Sum, Alias = "amount" }] },
                    new() { Filter = new() { Source = "o.amount > 0.0", Language = "cel" } },
                    new() { OrderBy = "o.amount desc" },
                    new() { Compute = [new() { Alias = "doubled", Expression = new() { Source = "o.amount * 2.0", Language = "cel" } }] },
                ], Selections = [new() { Field = "o.amount" }, new() { Field = "doubled" },
                    new() { Alias = "observed", Expression = new() { Source = "o.amount + 1.0", Language = "cel" } }],
            } },
        });
        var grouped = await groupedCall.ResponseAsync;
        Assert.Equal(2, grouped.Rows.Count);
        Assert.Equal(FieldType.Double, grouped.Schema.Single().Children.Single(field => field.Name == "amount").Type);
        Assert.All(grouped.Rows, row => {
            var values = Assert.Single(row.Fields["orders"].ListValue!.Values).StructValue!.Fields;
            Assert.Equal(300d, values["amount"].NumberValue);
            Assert.Equal(600d, DynamicValueMapper.FromDynamic(values["doubled"]));
            Assert.Equal(301d, DynamicValueMapper.FromDynamic(values["observed"]));
        });
        using var client = factory.CreateClient();
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "full_name", name = "buyers" } }, selections = new object[] {
                new { field = "full_name.id", alias = "id" }, new { alias = "computed", expression = new { source = "full_name.id + 1", language = "cel" } },
            },
        });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(json.GetProperty("rows").EnumerateArray(), row => Assert.Equal(JsonValueKind.Number, row.GetProperty("id").ValueKind));
    }

    [Fact]
    public async Task Grpc_Alias_Collision_And_Nested_Group_Return_Selected_Output_Types() {
        using var channel = factory.CreateGrpcChannel();
        using var aliasCall = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "b", Name = "buyers" } },
            Selections = { new() { Field = "b.id", Alias = "full_name" },
                new() { Alias = "computed", Expression = new() { Source = "b.id + 1", Language = "cel" } } },
        });
        var aliased = await aliasCall.ResponseAsync;
        Assert.Equal(FieldType.Int64, aliased.Schema.Single(field => field.Name == "full_name").Type);
        Assert.Equal(new long?[] { 1, 2 }, aliased.Rows.Select(row => row.Fields["full_name"].IntValue).OrderBy(value => value));
        using var nestedCall = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "b", Name = "buyers" } },
            Selections = { new() { Field = "b.orders", Alias = "orders",
                Transformations = [new() { IsGroupBy = true, GroupByKeys = ["o.status"],
                    GroupByAggregations = [new() { Field = "o.amount", Function = AggregationFunction.Sum, Alias = "amount" }] }],
                Selections = [new() { Field = "o.amount" }],
            } },
        });
        var nested = await nestedCall.ResponseAsync;
        Assert.Equal(FieldType.Double, nested.Schema.Single().Children.Single().Type);
        Assert.All(nested.Rows, row => Assert.Equal(300d, Assert.Single(row.Fields["orders"].ListValue!.Values).StructValue!.Fields["amount"].NumberValue));
    }

    [Fact]
    public async Task Public_Group_And_Dynamic_Precision_Leaves_Retain_Each_Value_Kind() {
        using var channel = factory.CreateGrpcChannel();
        var request = new QueryInsightGrpcRequest { Sources = { new() { Alias = "v", Name = "values" } },
            Transformations = {
                new() { Compute = [new() { Alias = "key", Expression = new() { Source = "v.id == 1 ? 1 : '1'", Language = "cel" } }] },
                new() { IsGroupBy = true, GroupByKeys = ["key"], GroupByAggregations = [new() { Field = "*", Function = AggregationFunction.Count, Alias = "count" }] },
            }, Selections = { new() { Field = "key" }, new() { Field = "count" } } };
        using var groupedCall = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        var grouped = await groupedCall.ResponseAsync;
        Assert.Equal(FieldType.Dynamic, grouped.Schema.Single(field => field.Name == "key").Type);
        Assert.Equal(2, grouped.Rows.Count);
        Assert.Contains(grouped.Rows, row => Equals(1L, DynamicValueMapper.FromDynamic(row.Fields["key"])));
        Assert.Contains(grouped.Rows, row => Equals("1", DynamicValueMapper.FromDynamic(row.Fields["key"])));
        Assert.All(grouped.Rows, row => Assert.Equal(1L, row.Fields["count"].IntValue));
        using var http = factory.CreateClient();
        using var result = await http.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "v", name = "values" } },
            transformations = new object[] {
                new { compute = new { fields = new[] { new { alias = "key", expression = new { source = "v.id == 1 ? 1 : '1'", language = "cel" } } } } },
                new { group_by = new { keys = new[] { "key" }, aggregations = new[] { new { field = "*", function = "count", alias = "count" } } } },
            }, selections = new[] { new { field = "key" }, new { field = "count" } },
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        var keys = json.GetProperty("rows").EnumerateArray().Select(row => ScalarPayloadConverter.ReadValue(row.GetProperty("key"))).ToArray();
        Assert.Contains(1L, keys);
        Assert.Contains("1", keys);
        var expected = ValueQuery.From(1);
        foreach (var (name, value) in new (string, object)[] { ("precise", expected.Precise), ("unsigned", expected.Unsigned),
                     ("blob", expected.Blob), ("timestamp", expected.Timestamp), ("offset", expected.Offset), ("identifier", expected.Identifier),
                     ("duration", expected.Duration), ("character", expected.Character), ("flag", expected.Flag), ("number", expected.Number) }) {
            var projected = new QueryInsightGrpcRequest { Sources = { new() { Alias = "v", Name = "values" } },
                Selections = { new() { Field = "v.id", Alias = "id" }, new() { Alias = "value", Expression = new() { Source = "v." + name, Language = "cel" } } } };
            using var call = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), projected);
            var response = await call.ResponseAsync;
            var leaf = response.Rows.Single(row => row.Fields["id"].IntValue == 1).Fields["value"];
            Assert.Equal(ScalarValue.Kind(value.GetType()), leaf.TypeLabel);
            var restored = DynamicValueMapper.FromDynamic(leaf);
            if (value is byte[] bytes) Assert.Equal(bytes, Assert.IsType<byte[]>(restored));
            else Assert.Equal(value, restored);
            using var projectedHttp = await http.PostAsJsonAsync("/v1/insight:query", new {
                sources = new[] { new { alias = "v", name = "values" } },
                selections = new object[] { new { field = "v.id", alias = "id" }, new { alias = "value", expression = new { source = "v." + name, language = "cel" } } },
            });
            Assert.Equal(HttpStatusCode.OK, projectedHttp.StatusCode);
            var body = await projectedHttp.Content.ReadFromJsonAsync<JsonElement>();
            var restoredHttp = ScalarPayloadConverter.ReadValue(body.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == 1).GetProperty("value"));
            if (value is byte[] binary) Assert.Equal(binary, Assert.IsType<byte[]>(restoredHttp));
            else Assert.Equal(value, restoredHttp);
        }
    }

    [Fact]
    public async Task Query_RepositoryValues_RoundTripsThroughGrpcAndHttpWithoutPrecisionLoss() {
        using var channel = factory.CreateGrpcChannel();
        var descriptor = factory.Services.GetServices<IGrpcServiceDescriptorContributor>()
            .SelectMany(contributor => contributor.GetServiceDescriptors(factory.Services))
            .Single(service => service.FullName == InsightGrpcMethods.ServiceName);
        var query = Assert.Single(descriptor.Methods);
        var method = new Method<QueryInsightGrpcRequest, QueryInsightGrpcResponse>(MethodType.Unary,
            query.Service.FullName, query.Name,
            GrpcMarshallers.Create<QueryInsightGrpcRequest>(InsightGrpcMethods.Model),
            GrpcMarshallers.Create<QueryInsightGrpcResponse>(InsightGrpcMethods.Model));
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(),
            new() { Sources = { new() { Alias = "v", Name = "values" } } });
        var rowsType = query.OutputType.FindFieldByName("Rows").MessageType;
        Assert.True(rowsType.FindFieldByName("Fields").IsMap);
        var valueType = rowsType.FindFieldByName("Fields").MessageType.FindFieldByName("value").MessageType;
        Assert.True(valueType.FindFieldByName("IntValue").HasPresence);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.Message, valueType.FindFieldByName("ListValue").FieldType);
        var grpc = await call.ResponseAsync;
        using var client = factory.CreateClient();
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new { sources = new[] { new { alias = "v", name = "values" } } });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        var row = grpc.Rows.Single(r => r.Fields["id"].IntValue == 1);
        var wire = row.Fields;
        var expected = ValueQuery.From(1);
        var httpRow = json.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == 1);
        var schema = grpc.Schema.ToDictionary(f => f.Name);
        Assert.Equal(FieldType.UInt64, schema["unsigned"].Type);
        Assert.Equal("18446744073709551615", wire["unsigned"].StringValue);
        Assert.Equal(expected.Unsigned, ulong.Parse(wire["unsigned"].StringValue!, CultureInfo.InvariantCulture));
        Assert.Equal(expected.Unsigned, httpRow.GetProperty("unsigned").GetUInt64());
        Assert.Equal((long?)long.MaxValue, grpc.Rows.Single(r => r.Fields["id"].IntValue == 2).Fields["unsigned"].IntValue);
        Assert.Equal(FieldType.Decimal, schema["precise"].Type);
        Assert.Equal(expected.Precise, decimal.Parse(wire["precise"].StringValue!, CultureInfo.InvariantCulture));
        Assert.Equal(expected.Precise, httpRow.GetProperty("precise").GetDecimal());
        Assert.Equal(FieldType.Bytes, schema["blob"].Type);
        Assert.Equal(expected.Blob, Convert.FromBase64String(wire["blob"].StringValue!));
        Assert.Equal(expected.Blob, httpRow.GetProperty("blob").GetBytesFromBase64());
        Assert.Equal(FieldType.Guid, schema["identifier"].Type);
        Assert.Equal(expected.Identifier, Guid.ParseExact(wire["identifier"].StringValue!, "D"));
        Assert.Equal(expected.Identifier, httpRow.GetProperty("identifier").GetGuid());
        Assert.Equal(FieldType.Timestamp, schema["timestamp"].Type);
        Assert.Equal(expected.Timestamp, DateTime.ParseExact(wire["timestamp"].StringValue!, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        Assert.Equal(expected.Timestamp, httpRow.GetProperty("timestamp").GetDateTime());
        Assert.Equal(FieldType.DateTimeOffset, schema["offset"].Type);
        var offset = DateTimeOffset.ParseExact(wire["offset"].StringValue!, "O", CultureInfo.InvariantCulture);
        Assert.Equal(expected.Offset.Ticks, offset.Ticks);
        Assert.Equal(expected.Offset.Offset, offset.Offset);
        Assert.Equal(expected.Offset, httpRow.GetProperty("offset").GetDateTimeOffset());
        Assert.Equal(FieldType.Duration, schema["duration"].Type);
        Assert.Equal(expected.Duration, TimeSpan.ParseExact(wire["duration"].StringValue!, "c", CultureInfo.InvariantCulture));
        Assert.Equal(expected.Duration, TimeSpan.Parse(httpRow.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(FieldType.Enum, schema["kind"].Type);
        Assert.Equal("Ready", wire["kind"].StringValue);
        Assert.Equal("Ready", httpRow.GetProperty("kind").GetString());
        Assert.Equal(FieldType.Char, schema["character"].Type);
        Assert.Equal("λ", wire["character"].StringValue);
        Assert.Equal(true, wire["flag"].BoolValue);
        Assert.Equal(1.25, wire["number"].NumberValue);
        Assert.Equal((long?)long.MinValue, wire["signed"].IntValue);
        Assert.Equal((long?)uint.MaxValue, wire["positive"].IntValue);
        Assert.Equal((long?)sbyte.MinValue, wire["small_signed"].IntValue);
        Assert.Equal((long?)ushort.MaxValue, wire["small_unsigned"].IntValue);
        Assert.Equal((long?)short.MinValue, wire["short"].IntValue);
        Assert.Equal((long?)byte.MaxValue, wire["byte"].IntValue);
        Assert.Equal(-1.5, wire["float"].NumberValue);
        Assert.Equal(long.MinValue, long.Parse(httpRow.GetProperty("signed").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(uint.MaxValue, httpRow.GetProperty("positive").GetUInt32());
        Assert.Equal(-1.5f, httpRow.GetProperty("float").GetSingle());
        Assert.True(wire["missing"].NullValue);
        Assert.Equal(JsonValueKind.Null, httpRow.GetProperty("missing").ValueKind);
        Assert.Equal(FieldType.Map, schema["amounts"].Type);
        Assert.Equal(FieldType.Decimal, schema["amounts"].Children.Single().Element!.Type);
        var amounts = wire["amounts"].StructValue!.Fields["exact"].ListValue!.Values;
        Assert.True(amounts[0].NullValue);
        Assert.Equal(expected.Precise, decimal.Parse(amounts[1].StringValue!, CultureInfo.InvariantCulture));
        Assert.Equal(expected.Precise, httpRow.GetProperty("amounts").GetProperty("exact")[1].GetDecimal());
        Assert.True(schema["sequences"].IsList);
        Assert.True(schema["sequences"].Element!.IsList);
        Assert.Equal(FieldType.UInt64, schema["sequences"].Element!.Element!.Type);
        var sequence = wire["sequences"].ListValue!.Values[0].ListValue!.Values;
        Assert.True(sequence[0].NullValue);
        Assert.Equal(expected.Unsigned, ulong.Parse(sequence[1].StringValue!, CultureInfo.InvariantCulture));
        Assert.Equal(expected.Unsigned, httpRow.GetProperty("sequences")[0][1].GetUInt64());
        Assert.NotNull(wire["empty_list"].ListValue);
        Assert.Empty(wire["empty_list"].ListValue!.Values);
        Assert.False(wire["empty_list"].NullValue);
        Assert.NotNull(wire["empty_map"].StructValue);
        Assert.Empty(wire["empty_map"].StructValue!.Fields);
        Assert.Equal(JsonValueKind.Array, httpRow.GetProperty("empty_list").ValueKind);
        Assert.Equal(JsonValueKind.Object, httpRow.GetProperty("empty_map").ValueKind);
    }

    [Theory]
    [InlineData("en-US", "Unknown resource 'missing'.")]
    [InlineData("fr", "Ressource 'missing' inconnue.")]
    public async Task Query_UnknownSource_PreservesCanonicalReasonMetadataAndLocalizationAcrossClients(string locale, string localized) {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(locale);
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new { sources = new[] { new { alias = "x", name = "missing" } } });
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal("NOT_FOUND", error.GetProperty("status").GetString());
        Assert.Equal("Unknown resource 'missing'.", error.GetProperty("message").GetString());
        var details = error.GetProperty("details").EnumerateArray().ToArray();
        var info = details.Single(d => d.TryGetProperty("reason", out _));
        Assert.Equal("UNKNOWN_SOURCE_NAME", info.GetProperty("reason").GetString());
        Assert.Equal("schemata.insight", info.GetProperty("domain").GetString());
        Assert.Equal("missing", info.GetProperty("metadata").GetProperty("name").GetString());
        Assert.Equal(localized, details.Single(d => d.TryGetProperty("locale", out _)).GetProperty("message").GetString());
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null,
            new(headers: new() { { "accept-language", locale } }), new() { Sources = { new() { Alias = "x", Name = "missing" } } });
        var failure = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.Equal(StatusCode.NotFound, failure.StatusCode);
        var status = Google.Rpc.Status.Parser.ParseFrom(failure.Trailers.GetValueBytes("grpc-status-details-bin"));
        Assert.Equal(error.GetProperty("message").GetString(), status.Message);
        var rpcInfo = status.Details.Single(d => d.Is(ErrorInfo.Descriptor)).Unpack<ErrorInfo>();
        Assert.Equal(info.GetProperty("reason").GetString(), rpcInfo.Reason);
        Assert.Equal(info.GetProperty("domain").GetString(), rpcInfo.Domain);
        Assert.Equal("missing", rpcInfo.Metadata["name"]);
        Assert.Equal(localized, status.Details.Single(d => d.Is(LocalizedMessage.Descriptor)).Unpack<LocalizedMessage>().Message);
    }

    [Fact]
    public async Task Query_DuplicateAlias_PreservesInvalidArgumentReasonAndArgumentsAcrossClients() {
        using var client = factory.CreateClient();
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new { sources = new[] {
            new { alias = "v", name = "values" }, new { alias = "v", name = "buyers" },
        } });
        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        var body = json.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENT", body.GetProperty("status").GetString());
        var info = body.GetProperty("details").EnumerateArray().Single(d => d.TryGetProperty("reason", out _));
        Assert.Equal("INVALID_ARGUMENT", info.GetProperty("reason").GetString());
        Assert.Equal("v", info.GetProperty("metadata").GetProperty("alias").GetString());
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), new() {
            Sources = { new() { Alias = "v", Name = "values" }, new() { Alias = "v", Name = "buyers" } },
        });
        var failure = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
        var status = Google.Rpc.Status.Parser.ParseFrom(failure.Trailers.GetValueBytes("grpc-status-details-bin"));
        var rpcInfo = status.Details.Single(d => d.Is(ErrorInfo.Descriptor)).Unpack<ErrorInfo>();
        Assert.Equal(info.GetProperty("reason").GetString(), rpcInfo.Reason);
        Assert.Equal("v", rpcInfo.Metadata["alias"]);
        Assert.Equal(body.GetProperty("message").GetString(), status.Message);
    }


    [Fact]
    public async Task Query_UnsupportedTopLevelTransform_ReturnsSameUnimplementedClassification() {
        using var client = factory.CreateClient();
        using var http = await client.PostAsJsonAsync("/v1/insight:query", new { sources = new[] { new { alias = "v", name = "values" } }, transformations = new[] { new { top = new { count = 1 } } } });
        Assert.Equal(HttpStatusCode.NotImplemented, http.StatusCode);
        var json = await http.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNIMPLEMENTED", json.GetProperty("error").GetProperty("status").GetString());
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(InsightGrpcMethods.Query, null, new(),
            new() { Sources = { new() { Alias = "v", Name = "values" } }, Transformations = { new() { Top = 1 } } });
        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        var status = Google.Rpc.Status.Parser.ParseFrom(error.Trailers.GetValueBytes("grpc-status-details-bin"));
        Assert.Equal("UNIMPLEMENTED", status.Details.Single(d => d.Is(ErrorInfo.Descriptor)).Unpack<ErrorInfo>().Reason);
    }
}
