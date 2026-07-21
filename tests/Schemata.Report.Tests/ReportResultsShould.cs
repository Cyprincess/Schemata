using System.Text.Json;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportResultsShould
{
    [Fact]
    public void Maps_Succeeded_Output_To_Result() {
        var snapshotOperation = new Operation {
            Done = true,
            Response = new() {
                Output = JsonSerializer.Serialize(new ReportOperationOutput {
                    Snapshot = "reports/daily-sales/snapshots/2026-07-18",
                }, SchemataJson.Default),
            },
        };
        var responseOperation = new Operation {
            Done = true,
            Response = new() {
                Output = JsonSerializer.Serialize(new ReportOperationOutput {
                    Response = new() {
                        NextPageToken = "next-page",
                        TotalSize     = 1,
                    },
                }, SchemataJson.Default),
            },
        };

        var snapshot = ReportResults.FromOperation(snapshotOperation);
        var response = ReportResults.FromOperation(responseOperation);

        Assert.Equal("reports/daily-sales/snapshots/2026-07-18", snapshot.Snapshot);
        Assert.Empty(snapshot.Response.Rows);
        Assert.Null(response.Snapshot);
        Assert.Equal("next-page", response.Response.NextPageToken);
        Assert.Equal(1, response.Response.TotalSize);
    }

    [Fact]
    public void Pending_Operation_Throws() {
        var exception = Assert.Throws<ReportException>(() => ReportResults.FromOperation(new Operation()));

        Assert.Equal(ReportReasons.OperationNotComplete, exception.Reason);
    }

    [Fact]
    public void Failed_Operation_Surfaces_Error() {
        var exception = Assert.Throws<ReportException>(() => ReportResults.FromOperation(new Operation {
            Done  = true,
            Error = new() {
                Code    = 13,
                Message = "Generation failed.",
            },
        }));

        Assert.Equal(ReportReasons.OperationFailed, exception.Reason);
        Assert.Equal("Generation failed.", exception.Message);
        Assert.Equal("13", exception.Metadata!["code"]);
    }
}
