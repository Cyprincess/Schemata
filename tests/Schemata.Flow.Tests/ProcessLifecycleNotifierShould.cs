using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessLifecycleNotifierShould
{
    [Fact]
    public async Task Log_Error_And_Swallow_When_Process_Observer_Throws() {
        var observer = new Mock<IProcessLifecycleObserver>();
        observer.Setup(o => o.OnStartedAsync(It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
        var logger = new Mock<ILogger<ProcessLifecycleNotifier>>();
        var notifier = Notifier(logger, [observer.Object]);
        var process = Process();

        await notifier.NotifyStartedAsync(Snapshot(process), CancellationToken.None);

        VerifyLog(logger, LogLevel.Error, Times.Once());
        logger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Continue_Notifying_Remaining_Observers_After_A_Throw() {
        var failing = new Mock<IProcessLifecycleObserver>();
        failing.Setup(o => o.OnTerminatedAsync(It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("boom"));
        var succeeding = new Mock<IProcessLifecycleObserver>();
        var logger = new Mock<ILogger<ProcessLifecycleNotifier>>();
        var notifier = Notifier(logger, [failing.Object, succeeding.Object]);
        var process = Process();

        await notifier.NotifyTerminatedAsync(process, CancellationToken.None);

        succeeding.Verify(o => o.OnTerminatedAsync(process, It.IsAny<CancellationToken>()), Times.Once);
        VerifyLog(logger, LogLevel.Error, Times.Once());
    }

    private static ProcessLifecycleNotifier Notifier(
        Mock<ILogger<ProcessLifecycleNotifier>> logger,
        IProcessLifecycleObserver[]             processObservers
    ) {
        return new(processObservers, logger.Object);
    }

    private static SchemataProcess Process() {
        return new() { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "definition" };
    }

    private static ProcessSnapshot Snapshot(
        SchemataProcess              process,
        SchemataProcessToken[]?      tokens      = null,
        SchemataProcessTransition[]? transitions = null
    ) {
        return new() { Process = process, Tokens = tokens ?? [], Transitions = transitions ?? [] };
    }

    private static void VerifyLog(Mock<ILogger<ProcessLifecycleNotifier>> logger, LogLevel level, Times times) {
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
    }
}
