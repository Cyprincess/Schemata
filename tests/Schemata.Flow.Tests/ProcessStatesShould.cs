using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessStatesShould
{
    [Theory]
    [InlineData("Completed", true)]
    [InlineData("Failed", true)]
    [InlineData("Terminated", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Running", false)]
    [InlineData("Waiting", false)]
    [InlineData(null, false)]
    [InlineData("Paid", false)]
    public void Classify_Process_State_Terminality(string? state, bool terminal) {
        Assert.Equal(terminal, ProcessStates.IsTerminal(state));
    }
}
