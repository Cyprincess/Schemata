// ReSharper disable once CheckNamespace

namespace System.Reflection;

/// <summary>
///     Extension methods for checking custom attribute presence on reflection members.
/// </summary>
public static class CustomAttributeExtensions
{
    /// <summary>
    ///     Determines whether the specified member has a custom attribute of type <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The attribute type to check for.</typeparam>
    /// <param name="element">The member to inspect.</param>
    /// <param name="inherit">Whether to search the inheritance chain.</param>
    /// <returns><see langword="true" /> if the attribute is defined.</returns>
    public static bool HasCustomAttribute<T>(this MemberInfo element, bool inherit)
        where T : Attribute {
        return Attribute.IsDefined(element, typeof(T), inherit);
    }

    /// <summary>
    ///     Determines whether the specified type has a custom attribute of type <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The attribute type to check for.</typeparam>
    /// <param name="type">The type to inspect.</param>
    /// <param name="inherit">Whether to search the inheritance chain.</param>
    /// <returns><see langword="true" /> if the attribute is defined.</returns>
    public static bool HasCustomAttribute<T>(this TypeInfo type, bool inherit)
        where T : Attribute {
        return Attribute.IsDefined(type, typeof(T), inherit);
    }
}
