using System;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Skeleton;
using System.Linq;
using Schemata.Report.Grpc;
using Schemata.Insight.Skeleton.Models;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton.Models;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton.Entities;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public class ReportGrpcCustomMethodShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ReportGrpcCustomMethodShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Generate_Uses_Custom_Grpc_Method() {
        var operation = await Call<SchemataReport, GenerateReportRequest, Operation>(
            "generate", new() { Name = "dsl-records", Persist = true, Sync = true });

        Assert.True(operation.Done);
        var response = operation.Response;
        Assert.NotNull(response);
        var payload = response.Output;
        Assert.NotNull(payload);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(payload, SchemataJson.Default);
        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output.Snapshot));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task ReadSnapshot_Pages_Persisted_Rows_Through_Grpc() {
        var operation = await Call<SchemataReport, GenerateReportRequest, Operation>(
            "generate", new() { Name = "dsl-records", Persist = true, Sync = true });
        var operationResponse = operation.Response;
        Assert.NotNull(operationResponse);
        var snapshotPayload = operationResponse.Output;
        Assert.NotNull(snapshotPayload);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(snapshotPayload, SchemataJson.Default);
        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output.Snapshot));
        var request = new ReadSnapshotRequest { CanonicalName = output.Snapshot, PageSize = 2 };

        var first = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read", request);
        Assert.Equal(new long?[] { 1, 2 }, first.Rows.Select(row => row.Fields["value"].IntValue));
        Assert.Equal(FieldType.Int64, Assert.Single(first.Schema).Type);
        Assert.NotNull(first.NextPageToken);
        var last = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = output.Snapshot, PageSize = 2, PageToken = first.NextPageToken });
        Assert.Equal(3L, Assert.Single(last.Rows).Fields["value"].IntValue);
        Assert.Null(last.NextPageToken);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task ReadSnapshot_Uses_Persisted_Schema_To_Preserve_Typed_Values() {
        var operation = await Call<SchemataReport, GenerateReportRequest, Operation>("generate",
            new() { Name = "typed-values", Persist = true, Sync = true });
        Assert.True(operation.Error is null, operation.Error?.Message);
        Assert.NotNull(operation.Response);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(operation.Response!.Output!, SchemataJson.Default)!;
        using (var scope = _factory.Services.CreateScope()) {
            var header = await scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>().GetAsync(output.Snapshot!);
            var schema = JsonSerializer.Deserialize<FieldDescriptor[]>(header!.Schema!, SchemataJson.Default)!;
            Assert.Equal(FieldType.UInt64, schema.Single(field => field.Name == "unsigned").Type);
            Assert.Equal(FieldType.Decimal, schema.Single(field => field.Name == "precise").Type);
        }
        var page = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = output.Snapshot, PageSize = 1 });
        var values = Assert.Single(page.Rows).Fields;
        Assert.Equal(ulong.MaxValue, ulong.Parse(values["unsigned"].StringValue!, CultureInfo.InvariantCulture));
        Assert.Equal(7922816251426433759354395033.5m, decimal.Parse(values["precise"].StringValue!, CultureInfo.InvariantCulture));
        Assert.True(values["missing"].NullValue);
        Assert.True(values["sequence"].ListValue!.Values[0].NullValue);
        Assert.Equal(values["precise"].StringValue, values["sequence"].ListValue!.Values[1].StringValue);
        Assert.Equal(values["precise"].StringValue, values["map"].StructValue!.Fields["ExactKey"].StringValue);
        Assert.Empty(values["empty_list"].ListValue!.Values);
        Assert.Empty(values["empty_map"].StructValue!.Fields);
        Assert.Equal(TimeSpan.FromHours(5.5), DateTimeOffset.ParseExact(values["offset"].StringValue!, "O", CultureInfo.InvariantCulture).Offset);
        Assert.Equal(FieldType.DateTimeOffset, page.Schema.Single(field => field.Name == "offset").Type);
        Assert.NotNull(page.NextPageToken);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task ReadSnapshot_Completes_Computed_Expression_Types_Like_Direct_Insight() {
        QueryInsightResponse direct;
        using (var directScope = _factory.Services.CreateScope()) {
            direct = await directScope.ServiceProvider.GetRequiredService<Schemata.Insight.Skeleton.IInsightService>().QueryAsync(new() {
                Sources = [new("record", "source-records")],
                Selections = [
                    new() { Alias = "number", Expression = new("1.25", "cel") },
                    new() { Alias = "unsigned", Expression = new("18446744073709551615u", "cel") },
                    new() { Alias = "bytes", Expression = new("b'\\000\\xff'", "cel") },
                ],
            }, null);
        }
        var expected = Schemata.Transport.Grpc.DynamicValueMapper.ToValue(direct.Rows[0]["number"], InsightValueModel.Unsupported);
        var operation = await Call<SchemataReport, GenerateReportRequest, Operation>("generate",
            new() { Name = "computed-values", Persist = true, Sync = true });
        Assert.True(operation.Error is null, operation.Error?.Message);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(operation.Response!.Output!, SchemataJson.Default)!;
        var page = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = output.Snapshot, PageSize = 1 });
        Assert.Equal(FieldType.Dynamic, page.Schema.Single(field => field.Name == "number").Type);
        var number = Assert.Single(page.Rows).Fields["number"];
        Assert.Equal(1.25, number.NumberValue);
        Assert.Equal(expected.NumberValue, number.NumberValue);
        Assert.Null(number.StringValue);
        var values = Assert.Single(page.Rows).Fields;
        Assert.Equal(FieldType.Dynamic, page.Schema.Single(field => field.Name == "unsigned").Type);
        Assert.Equal("18446744073709551615", values["unsigned"].StringValue);
        Assert.Equal(FieldType.Dynamic, page.Schema.Single(field => field.Name == "bytes").Type);
        Assert.Equal(new byte[] { 0, 255 }, Convert.FromBase64String(values["bytes"].StringValue!));
        Assert.Equal(Schemata.Common.ScalarKind.Double, number.TypeLabel);
        Assert.Equal(Schemata.Common.ScalarKind.UInt64, values["unsigned"].TypeLabel);
        Assert.Equal(Schemata.Common.ScalarKind.Bytes, values["bytes"].TypeLabel);
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Schemata.Report.Skeleton.IReportService>();
        var persisted = await service.RunAsync(new() { Persist = true, Query = new() {
            Sources = [new("record", "source-records")],
            Transformations = [
                new() { Compute = new([new(new("record.value == 1 ? 1 : '1'", "cel"), "key")]) },
                new() { GroupBy = new(["key"], [new("*", AggregationFunction.Count, "count")]) },
            ], Selections = [new() { Field = "key" }, new() { Field = "count" }],
        } });
        var mixed = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = persisted.Snapshot, PageSize = 10 });
        Assert.Equal(2, mixed.Rows.Count);
        Assert.Equal(FieldType.Dynamic, mixed.Schema.Single(field => field.Name == "key").Type);
        var decoded = ReadSnapshotGrpcResponse.ToResponse(mixed);
        Assert.Contains(decoded.Rows, row => Equals(1L, row["key"]) && Equals(1L, row["count"]));
        Assert.Contains(decoded.Rows, row => Equals("1", row["key"]) && Equals(2L, row["count"]));
        var precision = await service.RunAsync(new() { Persist = true, Query = new() {
            Sources = [new("v", "typed-values")], Selections = [new() { Alias = "value", Expression = new(
                "[v.id == 1 ? v.precise : '1', v.unsigned, v.blob, v.timestamp, v.offset, v.identifier, v.duration, v.character, v.flag, v.number, null, [], {}, {'exact':v.precise}]", "cel") }],
        } });
        var precisionPage = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = precision.Snapshot, PageSize = 10 });
        var restored = ReadSnapshotGrpcResponse.ToResponse(precisionPage);
        Assert.Equal(3, restored.Rows.Count);
        Assert.Contains(restored.Rows, row => Assert.IsAssignableFrom<System.Collections.Generic.IList<object?>>(row["value"])[0] is decimal);
        Assert.Contains(restored.Rows, row => Equals("1", Assert.IsAssignableFrom<System.Collections.Generic.IList<object?>>(row["value"])[0]));
        Assert.All(restored.Rows, row => {
            var cells = Assert.IsAssignableFrom<System.Collections.Generic.IList<object?>>(row["value"]);
            Assert.Equal(ulong.MaxValue, cells[1]);
            Assert.Equal(new byte[] { 0, 255 }, Assert.IsType<byte[]>(cells[2]));
            Assert.Equal(new DateTime(638999999999999999, DateTimeKind.Utc), cells[3]);
            Assert.Equal(TimeSpan.FromHours(5.5), Assert.IsType<DateTimeOffset>(cells[4]).Offset);
            Assert.Equal(Guid.Parse("3f0bafc4-298d-4521-a302-8d646c81a515"), cells[5]);
            Assert.Equal(TimeSpan.FromTicks(-1234567890123), cells[6]);
            Assert.Equal('λ', cells[7]); Assert.Equal(false, cells[8]); Assert.Equal(1.25d, cells[9]);
            Assert.Null(cells[10]);
            Assert.Empty(Assert.IsAssignableFrom<System.Collections.Generic.IList<object?>>(cells[11]));
            Assert.Empty(Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyDictionary<string, object?>>(cells[12]));
            Assert.Equal(7922816251426433759354395033.5m,
                Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyDictionary<string, object?>>(cells[13])["exact"]);
        });
        using var client = _factory.CreateClient();
        using var http = await client.GetAsync("/v1/" + precision.Snapshot + ":read?page_size=10");
        Assert.Equal(System.Net.HttpStatusCode.OK, http.StatusCode);
        using var body = JsonDocument.Parse(await http.Content.ReadAsStringAsync());
        var httpValues = body.RootElement.GetProperty("rows").EnumerateArray().Select(row =>
            Schemata.Common.ScalarPayloadConverter.ReadValue(row.GetProperty("value"))).ToArray();
        Assert.Equal(3, httpValues.Length);
        Assert.Contains(httpValues, value => Assert.IsAssignableFrom<System.Collections.Generic.IList<object?>>(value)[0] is decimal);
        Assert.All(httpValues, value => Assert.Equal(ulong.MaxValue, Assert.IsAssignableFrom<System.Collections.Generic.IList<object?>>(value)[1]));
        var childSnapshot = await service.RunAsync(new() { Persist = true, Query = new() {
            Sources = [new("v", "typed-values")], Selections = [new() { Field = "v.children", Alias = "children",
                Selections = [new() { Field = "children.number" }, new() { Alias = "computed", Expression = new("children.amount", "cel") }],
            }],
        } });
        var childPage = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = childSnapshot.Snapshot, PageSize = 10 });
        Assert.All(childPage.Rows, row => {
            var leaf = Assert.Single(row.Fields["children"].ListValue!.Values).StructValue!.Fields["computed"];
            Assert.Equal(Schemata.Common.ScalarKind.Decimal, leaf.TypeLabel);
            Assert.Equal(7922816251426433759354395033.5m, DynamicValueMapper.FromDynamic(leaf));
            var known = Assert.Single(row.Fields["children"].ListValue!.Values).StructValue!.Fields["number"];
            Assert.Equal(7L, known.IntValue);
            Assert.Null(known.TypeLabel);
        });
        using var childHttp = await client.GetAsync("/v1/" + childSnapshot.Snapshot + ":read?page_size=10");
        Assert.Equal(System.Net.HttpStatusCode.OK, childHttp.StatusCode);
        using var childBody = JsonDocument.Parse(await childHttp.Content.ReadAsStringAsync());
        Assert.All(childBody.RootElement.GetProperty("rows").EnumerateArray(), row => {
            var child = row.GetProperty("children")[0];
            Assert.Equal(JsonValueKind.Number, child.GetProperty("number").ValueKind);
            Assert.Equal(7, child.GetProperty("number").GetInt32());
            Assert.Equal(7922816251426433759354395033.5m, ScalarPayloadConverter.ReadValue(child.GetProperty("computed")));
        });
        var localPage = childBody.RootElement.Deserialize<ReadSnapshotResponse>(SchemataJson.Default)!;
        using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(localPage, SchemataJson.Default));
        Assert.All(roundTrip.RootElement.GetProperty("rows").EnumerateArray(), row => {
            Assert.Equal(7, row.GetProperty("children")[0].GetProperty("number").GetInt32());
            Assert.Equal(7922816251426433759354395033.5m,
                ScalarPayloadConverter.ReadValue(row.GetProperty("children")[0].GetProperty("computed")));
        });
        var transformed = await service.RunAsync(new() { Persist = true, Query = new() {
            Sources = [new("v", "typed-values")], Selections = [new() { Field = "v.children", Alias = "children",
                Transformations = [
                    new() { GroupBy = new(["children.number"], [new("children.number", AggregationFunction.Sum, "number")]) },
                    new() { Filter = new(new("children.number > 0.0", "cel")) },
                    new() { Compute = new([new(new("children.number * 2.0", "cel"), "doubled")]) },
                ], Selections = [new() { Field = "children.number" }, new() { Field = "doubled" },
                    new() { Alias = "observed", Expression = new("children.number + 1.0", "cel") }],
            }],
        } });
        var transformedPage = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = transformed.Snapshot, PageSize = 10 });
        Assert.Equal(FieldType.Double, transformedPage.Schema.Single().Children.Single(field => field.Name == "number").Type);
        Assert.All(transformedPage.Rows, row => {
            var fields = Assert.Single(row.Fields["children"].ListValue!.Values).StructValue!.Fields;
            Assert.Equal(7d, fields["number"].NumberValue);
            Assert.Equal(14d, DynamicValueMapper.FromDynamic(fields["doubled"]));
            Assert.Equal(8d, DynamicValueMapper.FromDynamic(fields["observed"]));
        });
        var collision = await service.RunAsync(new() { Persist = true, Query = new() {
            Sources = [new("v", "typed-values")], Selections = [new() { Field = "v.children", Alias = "children",
                Transformations = [new() { GroupBy = new(["o.number"], [new("o.number", AggregationFunction.Sum, "o")]) },
                    new() { Filter = new(new("o.o > 0.0 && o > 0.0", "cel")) }],
                Selections = [new() { Field = "o.o", Alias = "qualified" }, new() { Field = "o", Alias = "bare" },
                    new() { Alias = "expression", Expression = new("o", "cel") }],
            }],
        } });
        var collisionPage = await Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>("read",
            new() { CanonicalName = collision.Snapshot, PageSize = 10 });
        Assert.All(collisionPage.Rows, row => {
            var fields = Assert.Single(row.Fields["children"].ListValue!.Values).StructValue!.Fields;
            Assert.Equal(7d, fields["qualified"].NumberValue);
            Assert.Equal(7d, fields["bare"].NumberValue);
            Assert.Equal(7d, DynamicValueMapper.FromDynamic(fields["expression"]));
        });
        using var collisionHttp = await client.GetAsync("/v1/" + collision.Snapshot + ":read?page_size=10");
        Assert.Equal(System.Net.HttpStatusCode.OK, collisionHttp.StatusCode);
        using var collisionJson = JsonDocument.Parse(await collisionHttp.Content.ReadAsStringAsync());
        Assert.All(collisionJson.RootElement.GetProperty("rows").EnumerateArray(), row => {
            var fields = row.GetProperty("children")[0];
            Assert.Equal(7d, fields.GetProperty("qualified").GetDouble());
            Assert.Equal(7d, fields.GetProperty("bare").GetDouble());
            Assert.Equal(7d, ScalarPayloadConverter.ReadValue(fields.GetProperty("expression")));
        });
    }


    [Fact]
    public async Task Generate_With_Name_And_Query_Returns_InvalidArgument() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataReport, GenerateReportRequest, Operation>(
                "generate", new() { Name = "dsl-records", Query = new(), Sync = true }));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    private async Task<TResponse> Call<TEntity, TRequest, TResponse>(string verb, TRequest request)
        where TEntity : class
        where TRequest : class
        where TResponse : class {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(TRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(TResponse));
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var method = new Method<TRequest, TResponse>(
            MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(TEntity)),
            GrpcResourceNaming.CustomMethodName(descriptor, verb),
            GrpcMarshallers.Create<TRequest>(model),
            GrpcMarshallers.Create<TResponse>(model));
        using var channel = _factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), request);
        return await call.ResponseAsync;
    }
}
