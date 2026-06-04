using Humanizer;

namespace Schemata.Common;

public static class SchemataNaming
{
    public static string ToWireName(string clrName) { return clrName.Underscore(); }

    public static string ToClrMemberName(string wireName) { return wireName.Pascalize(); }
}
