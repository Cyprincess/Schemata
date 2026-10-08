using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>Projects optional client metadata into dynamic registration responses.</summary>
public interface IRegistrationResponseAdvisor<TApplication> : IAdvisor<TApplication, RegistrationResponse>
    where TApplication : SchemataApplication;
