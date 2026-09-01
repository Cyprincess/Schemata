using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Handlers;

public sealed class ReadSnapshotHandler<TSnapshot>(
    IReportSnapshotStore            snapshots,
    IOptions<SchemataReportOptions> options
) : IRequestHandler<ReadSnapshotRequest, ReadSnapshotResponse>
    where TSnapshot : SchemataReportSnapshot
{
    private const int DefaultPageSize = 1_000;

    public async Task<ReadSnapshotResponse> HandleAsync(ReadSnapshotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshotName = request.CanonicalName ?? request.Name;
        if (string.IsNullOrWhiteSpace(snapshotName)) {
            throw new InvalidArgumentException(message: "Snapshot name is required.");
        }

        var requested = request.PageSize ?? DefaultPageSize;
        if (requested <= 0) {
            throw new InvalidArgumentException(message: "Page size must be greater than zero.");
        }

        var maxPageSize = options.Value.MaxReadPageSize > 0 ? options.Value.MaxReadPageSize : DefaultPageSize;
        var pageSize    = requested > maxPageSize ? maxPageSize : requested;

        var header = await snapshots.GetAsync(snapshotName, ct)
                     ?? throw new InvalidArgumentException(message: "Report snapshot was not found.");
        var token = string.IsNullOrWhiteSpace(request.PageToken)
            ? new ReportReadPageToken(0, 0)
            : ReportReadPageToken.Decode(request.PageToken);
        var response   = new ReadSnapshotResponse();
        var chunkIndex = token.ChunkIndex;
        var offset     = token.Offset;

        while (response.Rows.Count < pageSize) {
            var chunk = await snapshots.GetChunkAsync(snapshotName, chunkIndex, ct);
            if (chunk is null) {
                if (offset != 0) {
                    throw new InvalidArgumentException(message: "Invalid page token.");
                }

                return response;
            }

            var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(chunk.Rows ?? "[]", SchemataJson.Default)
                       ?? [];
            if (offset > rows.Count) {
                throw new InvalidArgumentException(message: "Invalid page token.");
            }

            for (var position = offset; position < rows.Count && response.Rows.Count < pageSize; position++) {
                response.Rows.Add(rows[position]);
                offset = position + 1;
            }

            if (response.Rows.Count == pageSize) {
                if (offset == rows.Count) {
                    chunkIndex++;
                    offset = 0;
                }

                if (header.ChunkCount is not int count || chunkIndex < count) {
                    response.NextPageToken = ReportReadPageToken.Encode(chunkIndex, offset);
                }

                return response;
            }

            chunkIndex++;
            offset = 0;
        }

        return response;
    }
}
