using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Insight.Skeleton.Models;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Handlers;

public sealed class ReadSnapshotHandler<TSnapshot>(
    IReportSnapshotStore snapshots,
    IOptions<SchemataReportOptions> options,
    IDataProtectionProvider protection
) : IRequestHandler<ReadSnapshotRequest, ReadSnapshotResponse>
    where TSnapshot : SchemataReportSnapshot, new()
{
    private const int DefaultPageSize = 1_000;
    private readonly IDataProtector _protector = protection.CreateProtector(ReportReadPageToken.ProtectionPurpose);

    public async Task<ReadSnapshotResponse> HandleAsync(ReadSnapshotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshotName = request.CanonicalName ?? request.Name;
        if (string.IsNullOrWhiteSpace(snapshotName)) {
            throw new InvalidArgumentException(SchemataResources.REPORT_SNAPSHOT_NAME_REQUIRED);
        }

        var requested = request.PageSize ?? DefaultPageSize;
        if (requested <= 0) {
            throw new InvalidArgumentException(SchemataResources.REPORT_PAGE_SIZE_INVALID);
        }

        var maxPageSize = options.Value.MaxReadPageSize > 0 ? options.Value.MaxReadPageSize : DefaultPageSize;
        var pageSize    = requested > maxPageSize ? maxPageSize : requested;

        var binding = ReportReadPageToken.Bind(snapshotName, pageSize, request.Principal);
        var token = string.IsNullOrWhiteSpace(request.PageToken)
            ? binding
            : binding.Decode(_protector, request.PageToken);
        var header = await snapshots.GetAsync(snapshotName, ct)
                     ?? throw new InvalidArgumentException(SchemataResources.REPORT_SNAPSHOT_NOT_FOUND);
        var snapshotUid = header.Uid;
        var response = new ReadSnapshotResponse();
        var chunkIndex = token.ChunkIndex;
        var offset     = token.Offset;

        while (response.Rows.Count < pageSize) {
            var chunk = await snapshots.GetChunkAsync(snapshotName, chunkIndex, ct);
            if (chunk is null) {
                if (offset != 0) {
                    throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
                }

                break;
            }

            var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(chunk.Rows ?? "[]", SchemataJson.Default)
                       ?? [];
            if (offset > rows.Count) {
                throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
            }

            for (var position = offset; position < rows.Count && response.Rows.Count < pageSize; position++) {
                response.Rows.Add(rows[position]);
                offset = checked(position + 1);
            }

            if (response.Rows.Count == pageSize) {
                if (offset == rows.Count) {
                    chunkIndex = ReportReadPageToken.Advance(chunkIndex);
                    offset = 0;
                }

                break;
            }

            chunkIndex = ReportReadPageToken.Advance(chunkIndex);
            offset = 0;
        }

        var current = await snapshots.GetAsync(snapshotName, ct)
                      ?? throw new InvalidArgumentException(SchemataResources.REPORT_SNAPSHOT_NOT_FOUND);
        if (current.Uid != snapshotUid) {
            throw new InvalidArgumentException(SchemataResources.REPORT_SNAPSHOT_NOT_FOUND);
        }
        response.Schema = JsonSerializer.Deserialize<FieldDescriptor[]>(current.Schema ?? "[]", SchemataJson.Default) ?? [];
        for (var i = 0; i < response.Rows.Count; i++) {
            response.Rows[i] = response.Rows[i].ToDictionary(pair => pair.Key, pair => {
                var field = response.Schema.FirstOrDefault(field => field.Name == pair.Key);
                return field is { Type: FieldType.Dynamic, IsList: false } && pair.Value is JsonElement value
                    ? InsightValueModel.Decode(value, field) : pair.Value;
            });
        }
        if (response.Rows.Count == pageSize && (current.ChunkCount is not int count || chunkIndex < count)) {
            response.NextPageToken = binding.Encode(_protector, chunkIndex, offset);
        }

        return response;
    }
}
