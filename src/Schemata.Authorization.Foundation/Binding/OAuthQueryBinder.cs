using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Schemata.Authorization.Foundation.Binding;

/// <summary>
///     Model binder that reads OAuth request parameters from the query string, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.2: Authorization Response
///     </seealso>
///     .
/// </summary>
/// <typeparam name="T">The OAuth request model type.</typeparam>
/// <remarks>
///     Maps query parameter names from <c>snake_case</c> to PascalCase properties via <see cref="OAuthBinderHelpers" />.
/// </remarks>
/// <seealso cref="OAuthFormBinder{T}" />
/// <seealso cref="OAuthRequestBinderProvider" />
public sealed class OAuthQueryBinder<T> : IModelBinder
    where T : new()
{
    private static readonly (PropertyInfo Prop, string Param)[] Map = OAuthBinderHelpers.BuildMap(typeof(T));

    #region IModelBinder Members

    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext) {
        var request = bindingContext.HttpContext.Request;
        var model   = new T();

        foreach (var (prop, param) in Map) {
            var value = request.Query[param].ToString();
            if (!string.IsNullOrWhiteSpace(value)) {
                prop.SetValue(model, value);
            }
        }

        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }

    #endregion
}
