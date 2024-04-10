using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute : Attribute
{
    public ResourceAttribute(
        Type  entity,
        Type? request = null,
        Type? detail  = null,
        Type? summary = null) {
        Entity  = entity;
        Request = request ?? entity;
        Detail  = detail ?? entity;
        Summary = summary ?? detail ?? entity;

        var policies = Entity.GetCustomAttributes<ResourcePolicyAttribute>();
        foreach (var policy in policies) {
            var methods = policy.Methods?.Split(',').ToList();

            if (methods?.Contains(nameof(Browse)) == true) {
                Browse ??= policy;
            }

            if (methods?.Contains(nameof(Read)) == true) {
                Read ??= policy;
            }

            if (methods?.Contains(nameof(Edit)) == true) {
                Edit ??= policy;
            }

            if (methods?.Contains(nameof(Add)) == true) {
                Add ??= policy;
            }

            if (methods?.Contains(nameof(Delete)) == true) {
                Delete ??= policy;
            }
        }

        Endpoints.AddRange(Entity.GetCustomAttributes<ResourceAttributeBase>());
    }

    public Type Entity { get; }

    public Type? Request { get; }

    public Type? Detail { get; }

    public Type? Summary { get; }

    public ResourcePolicyAttribute? Browse { get; set; }

    public ResourcePolicyAttribute? Read { get; set; }

    public ResourcePolicyAttribute? Edit { get; set; }

    public ResourcePolicyAttribute? Add { get; set; }

    public ResourcePolicyAttribute? Delete { get; set; }

    public List<ResourceAttributeBase> Endpoints { get; } = [];
}
