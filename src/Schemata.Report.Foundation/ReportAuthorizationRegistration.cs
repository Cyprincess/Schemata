using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Report.Foundation.Commands;
using Schemata.Security.Skeleton.Advisors;

using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton.Models;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation;

internal static class ReportAuthorizationRegistration
{
    internal static IServiceCollection AddReportAuthentication<TReport, TSnapshot, TChunk>(this IServiceCollection services)
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        AddAuthentication<ResourceMethodRequest<TReport, RunReportRequest, ReportResult>, ReportResult>(services, static request => (request.Verb, typeof(TReport)));
        AddAuthentication<ResourceMethodRequest<TReport, GenerateReportRequest, Operation>, Operation>(services, static request => (request.Verb, typeof(TReport)));
        AddAuthentication<ReadSnapshotRequest, ReadSnapshotResponse>(services, static _ => (ReportOperations.Read, typeof(TSnapshot)));
        return services;
    }

    internal static IServiceCollection AddReportAuthorization<TReport, TSnapshot, TChunk>(this IServiceCollection services)
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        AddAuthorization<ResourceMethodRequest<TReport, RunReportRequest, ReportResult>, ReportResult>(services, static request => (request.Verb, typeof(TReport)));
        AddAuthorization<ResourceMethodRequest<TReport, GenerateReportRequest, Operation>, Operation>(services, static request => (request.Verb, typeof(TReport)));
        AddAuthorization<ReadSnapshotRequest, ReadSnapshotResponse>(services, static _ => (ReportOperations.Read, typeof(TSnapshot)));
        return services;
    }

    private static void AddAuthentication<TRequest, TResponse>(IServiceCollection services, Func<TRequest, (string Operation, Type? Entity)> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, (string Operation, Type? Entity)>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthenticationPipelineAdvisor<TRequest, TResponse>)));
    }

    private static void AddAuthorization<TRequest, TResponse>(IServiceCollection services, Func<TRequest, (string Operation, Type? Entity)> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, (string Operation, Type? Entity)>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthorizationPipelineAdvisor<TRequest, TResponse>)));
    }
}
