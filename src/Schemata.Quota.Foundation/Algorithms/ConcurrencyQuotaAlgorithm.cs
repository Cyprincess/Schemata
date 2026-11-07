using System;
using Schemata.Quota.Skeleton;

namespace Schemata.Quota.Foundation.Algorithms;

public sealed class ConcurrencyQuotaAlgorithm : IQuotaAlgorithm
{
    public QuotaDecision Evaluate(QuotaAlgorithmContext context) {
        var account = context.Account;
        if (!QuotaArithmetic.TryTotal(account.Committed, account.Reserved, context.Requested, context.Policy.Limit, out var total)) {
            return new(false, account.Committed, account.Reserved,
                QuotaArithmetic.Available(context.Policy.Limit, account.Committed, account.Reserved),
                account.WindowStart, TimeSpan.Zero, context.Segments);
        }

        return new(true, account.Committed, checked(account.Reserved + context.Requested),
            context.Policy.Limit - total, account.WindowStart, null, context.Segments);
    }

    public QuotaAccountTransition Settle(QuotaSettlementContext context) {
        var reserved = context.Account.Reserved - Math.Min(context.Amount, context.Account.Reserved);
        return new(context.Account.Committed, reserved,
            QuotaArithmetic.Available(context.Policy.Limit, context.Account.Committed, reserved),
            context.Account.WindowStart, context.Segments);
    }
}
