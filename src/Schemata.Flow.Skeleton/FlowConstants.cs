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

    #region Nested type: Handlers

    /// <summary>Well-known keys for Flow request-handler registrations.</summary>
    public static class Handlers
    {
        /// <summary>The Foundation handler used when no optional wrapper replaces the unkeyed alias.</summary>
        public const string Default = "default";
    }

    #endregion

    #region Nested type: Effects

    /// <summary>Well-known names for external-effect intent recording.</summary>
    public static class Effects
    {
        /// <summary>Token bookkeeping key holding the per-token in-transition effect sequence.</summary>
        public const string SequenceKey = "effectSequence";

        /// <summary>Request id path segment separating the token canonical name from the effect ordinal.</summary>
        public const string RequestIdSegment = "effects";
    }

    #endregion
}
