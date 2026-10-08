using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

public abstract class ScriptTask : ProcedureTaskBase
{
    private string? _language;
    private string? _script;
    private ExpressionCompileOptions? _options;
    private IReadOnlyDictionary<string, string> _outputs = FrozenDictionary<string, string>.Empty;

    public string? Language {
        get => _language;
        set { EnsureMutable(); _language = value; }
    }

    public string? Script {
        get => _script;
        set { EnsureMutable(); _script = value; }
    }

    public ExpressionCompileOptions? Options {
        get => _options;
        set { EnsureMutable(); _options = value; }
    }

    public IReadOnlyDictionary<string, string> Outputs {
        get => _outputs;
        set { EnsureMutable(); _outputs = value; }
    }

    protected internal override void FreezeCore() {
        base.FreezeCore();
        _outputs = FlowTaskBinding.FreezeOutputs(_outputs);
        _options = _options?.Snapshot();
    }
}

public sealed class ScriptTask<TInput, TResult> : ScriptTask
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
        if (string.IsNullOrEmpty(Language)) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_LANGUAGE_REQUIRED, "script", "Language");
        }
        if (string.IsNullOrEmpty(Script)) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_BINDING_REQUIRED, "script", "Script");
        }

        var compiler = context.GetService<IExpressionCompiler>(Language);
        if (compiler is null) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_COMPILER_NOT_FOUND, "script", "Language", Language);
        }

        Func<TInput, TResult> evaluate;
        try {
            var key = ExpressionCacheKey.Create(Language, Script, typeof(TInput), typeof(TResult), ExpressionCompileOptions.Fingerprint(Options));
            var expression = ExpressionCache.GetOrAddExpression(key, () => compiler.Compile<TInput, TResult>(compiler.Parse(Script), Options));
            evaluate = ExpressionCache.GetOrAddDelegate(expression);
        } catch (Exception exception) when (exception is ExpressionException or ArgumentException or InvalidOperationException or InvalidCastException) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_EXPRESSION_INVALID, "script", "Script", Language, exception);
        }

        var input = await FlowTaskBinding.ReadInputAsync(this, Input!, context, ct);
        ct.ThrowIfCancellationRequested();
        TResult result;
        try {
            result = evaluate(input);
        } catch (Exception exception) when (exception is ExpressionException or InvalidCastException or FormatException or OverflowException) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "script", typeof(TResult).FullName, Language, exception);
        }

        if (result is IExpressionError error) {
            throw FlowTaskBinding.Error(this, SchemataResources.FLOW_TASK_EXPRESSION_INVALID, "script", "Script", Language, expressionError: error);
        }

        await FlowTaskBinding.ApplyOutputAsync(this, Outputs, result, Output!, context, ct);
    }
}
