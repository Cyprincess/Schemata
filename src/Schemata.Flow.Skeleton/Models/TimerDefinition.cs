using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Timer event definition. The <see cref="TimeExpression" /> is interpreted
///     according to <see cref="TimerType" />.
/// </summary>
public sealed class TimerDefinition : FlowGraphNode, IEventDefinition
{
    private TimerType _timerType;
    private string _timeExpression = null!;
    private string _name = null!;

    /// <summary>
    ///     Whether the time expression is a fixed date, duration, or cycle.
    /// </summary>
    public TimerType TimerType {
        get => _timerType;
        set {
            EnsureMutable();
            _timerType = value;
        }
    }

    /// <summary>
    ///     The time expression string - ISO 8601 datetime/duration, or cron.
    /// </summary>
    public string TimeExpression {
        get => _timeExpression;
        set {
            EnsureMutable();
            _timeExpression = value;
        }
    }

    #region IEventDefinition Members

    public string Name {
        get => _name;
        set {
            EnsureMutable();
            _name = value;
        }
    }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
