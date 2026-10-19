using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton;

public interface IMessageExecutionScopeFactory
{
    ValueTask<MessageExecutionScope> CreateAsync(MessageContext? context, CancellationToken ct = default);
}
