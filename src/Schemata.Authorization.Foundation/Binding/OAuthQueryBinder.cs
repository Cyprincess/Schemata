using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Schemata.Authorization.Foundation.Binding;

public sealed class OAuthQueryBinder<T> : IModelBinder
    where T : new()
{
    private static readonly (PropertyInfo Prop, string Param)[] Map = OAuthBinderHelpers.BuildMap(typeof(T));

    #region IModelBinder Members

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
