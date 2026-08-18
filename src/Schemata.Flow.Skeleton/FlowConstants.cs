namespace Schemata.Flow.Skeleton;

/// <summary>
///     Well-known constant values for the flow domain.
/// </summary>
public static class FlowConstants
{
    #region Nested type: Engines

    /// <summary>
    ///     Well-known flow engine identifiers. Each names a keyed <c>IFlowRuntime</c> registration.
    /// </summary>
    public static class Engines
    {
        /// <summary>The built-in single-token state machine engine, covering a subset of BPMN 2.0.2.</summary>
        public const string StateMachine = "statemachine";

        /// <summary>The full BPMN 2.0.2 engine shipped by <c>Schemata.Flow.Bpmn</c>.</summary>
        public const string Bpmn = "bpmn";
    }

    #endregion
}
