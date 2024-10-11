// ReSharper disable once CheckNamespace

namespace System.Reflection;

public static class CustomAttributeExtensions
{
    public static bool HasCustomAttribute<T>(this MemberInfo element, bool inherit) where T : Attribute {
        return Attribute.IsDefined(element, typeof(T), inherit);
    }

    public static bool HasCustomAttribute<T>(this TypeInfo type, bool inherit) where T : Attribute {
        return Attribute.IsDefined(type, typeof(T), inherit);
    }
}
