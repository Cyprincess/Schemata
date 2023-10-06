namespace Schemata.DSL;

public static class CharExtensions
{
    public static bool IsStopWord(this char c) {
        return c is ',' or '(' or ')' or '[' or ']' or '{' or '}';
    }
}
