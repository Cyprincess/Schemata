using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Schemata.Authorization.Foundation.Binding;

/// <summary>
///     Model binder that reads OAuth request parameters from an HTTP form body, per
///     <seealso href="https://openid.net/specs/oauth-v2-form-post-response-mode-1_0.html">
///         OAuth 2.0 Form Post Response
///         Mode 1.0
///     </seealso>
///     .
/// </summary>
/// <typeparam name="T">The OAuth request model type.</typeparam>
/// <remarks>
///     Maps form field names from <c>snake_case</c> to PascalCase properties via <see cref="OAuthBinderHelpers" />.
/// </remarks>
/// <seealso cref="OAuthQueryBinder{T}" />
/// <seealso cref="OAuthRequestBinderProvider" />
public sealed class OAuthFormBinder<T> : IModelBinder
    where T : new()
{
    private static readonly (PropertyInfo Prop, string Param)[] Map = OAuthBinderHelpers.BuildMap(typeof(T));

    #region IModelBinder Members

    public async Task BindModelAsync(ModelBindingContext bindingContext) {
        var request = bindingContext.HttpContext.Request;
        var model   = new T();

        if (request.HasFormContentType) {
            var form = await request.ReadFormAsync();

            foreach (var (prop, param) in Map) {
                var value = form[param].ToString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    prop.SetValue(model, value);
                }
            }
        }

        bindingContext.Result = ModelBindingResult.Success(model);
    }

    #endregion
}
