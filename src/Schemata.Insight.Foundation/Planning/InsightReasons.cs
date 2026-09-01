namespace Schemata.Insight.Foundation.Planning;

/// <summary>The well-known reason codes Insight reports for rejected requests.</summary>
public static class InsightReasons
{
    public const string UnknownSourceName                 = "UNKNOWN_SOURCE_NAME";
    public const string UnknownExpressionLanguage         = "UNKNOWN_EXPRESSION_LANGUAGE";
    public const string InvalidExpression                 = "INVALID_EXPRESSION";
    public const string InvalidArgument                   = "INVALID_ARGUMENT";
    public const string Unimplemented                     = "UNIMPLEMENTED";
    public const string ExpressionLanguageNotValueCapable = "EXPRESSION_LANGUAGE_NOT_VALUE_CAPABLE";
}