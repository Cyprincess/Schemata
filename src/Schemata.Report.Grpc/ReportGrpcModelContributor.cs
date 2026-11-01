using ProtoBuf.Meta;
using Schemata.Report.Foundation.Queries;
using Schemata.Transport.Grpc;

namespace Schemata.Report.Grpc;

internal sealed class ReportGrpcModelContributor : IGrpcRuntimeModelContributor
{
    public void Configure(RuntimeTypeModel model) {
        model.Add(typeof(ReadSnapshotGrpcResponse), true);
        model.SetSurrogate<ReadSnapshotResponse, ReadSnapshotGrpcResponse>(
            ReadSnapshotGrpcResponse.FromResponse, ReadSnapshotGrpcResponse.ToResponse);
    }
}
