using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

public abstract class BusinessRuleTask : ProcedureTaskBase
{
    private string? _key;
    private string _version = "1";
    private RuleNoMatchPolicy _noMatch;
    private IReadOnlyDictionary<string, string> _outputs = FrozenDictionary<string, string>.Empty;

    public string? Key {
        get => _key;
        set { EnsureMutable(); _key = value; }
    }

    public string Version {
        get => _version;
        set { EnsureMutable(); _version = value; }
    }

    public RuleNoMatchPolicy NoMatch {
        get => _noMatch;
        set { EnsureMutable(); _noMatch = value; }
    }

    public IReadOnlyDictionary<string, string> Outputs {
        get => _outputs;
        set { EnsureMutable(); _outputs = value; }
    }

    protected internal override void FreezeCore() {
        base.FreezeCore();
        _outputs = FlowTaskBinding.FreezeOutputs(_outputs);
    }
}

public sealed class BusinessRuleTask<TInput, TResult> : BusinessRuleTask
{
    private Func<FlowTaskContext, CancellationToken, ValueTask<TInput>>? _input;
    private Func<FlowTaskContext, TResult, CancellationToken, ValueTask>? _output;

    public Func<FlowTaskContext, CancellationToken, ValueTask<TInput>>? Input {
        get => _input;
        set { EnsureMutable(); _input = value; }
    }

    public Func<FlowTaskContext, TResult, CancellationToken, ValueTask>? Output {
        get => _output;
        set { EnsureMutable(); _output = value; }
    }

    protected internal override async ValueTask InvokeAsync(FlowTaskContext context, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        FlowTaskBinding.RequireBindings(this, Input, Output);
        if (string.IsNullOrEmpty(Key) || string.IsNullOrEmpty(Version)) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_BINDING_REQUIRED, "rule", "Key/Version");
        }

        IFlowRuleHandler<TInput, TResult> handler;
        try {
            handler = context.GetRequiredService<IFlowRuleHandler<TInput, TResult>>((Key, Version));
        } catch (InvalidOperationException exception) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_HANDLER_NOT_FOUND, "rule", Key, Version, exception);
        }

        var input = await FlowTaskBinding.ReadInputAsync(this, Input!, context, ct);
        ct.ThrowIfCancellationRequested();
        BusinessRuleResult<TResult> evaluation;
        try {
            evaluation = await handler.EvaluateAsync(input, context, ct);
        } catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "rule", typeof(TResult).FullName, innerException: exception);
        }
        ct.ThrowIfCancellationRequested();
        if (!evaluation.Matched) {
            if (NoMatch == RuleNoMatchPolicy.Fail) {
                throw FlowTaskBinding.Error(this, SchemataResources.FLOW_RULE_NO_MATCH, "rule", Key, Version);
            }
            return;
        }

        await FlowTaskBinding.ApplyOutputAsync(this, Outputs, evaluation.Output!, Output!, context, ct);
    }
}
