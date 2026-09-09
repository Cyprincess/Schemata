using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>Validates and projects optional client metadata before an application is persisted.</summary>
public interface IRegistrationRequestAdvisor<TApplication> : IAdvisor<RegisterRequest, TApplication>
    where TApplication : SchemataApplication;
