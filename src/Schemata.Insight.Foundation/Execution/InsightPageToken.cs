using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Schemata.Abstractions;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Queries;

namespace Schemata.Insight.Foundation.Execution;

internal sealed record InsightPageToken(string Binding, Guid? Tenant, ProtectedContinuationCaller? Caller, int Skip)
{
    internal const string ProtectionPurpose = "Schemata.Insight.Foundation.PageToken";

    internal static InsightPageToken Bind(QueryInsightRequest request, ClaimsPrincipal? principal,
        int skip, int pageSize, SchemataInsightOptions options) {
        var binding = JsonSerializer.Serialize(new {
            request.Sources,
            request.Joins,
            request.Transformations,
            request.Selections,
            request.Language,
            Skip = skip,
            PageSize = pageSize,
            options.TotalSize,
            options.MaxResidualScanRows,
        }, SchemataJson.Default);
        return new(binding, TenantContext.Current.Uid, ProtectedContinuationCaller.Bind(principal), 0);
    }

    internal string Encode(IDataProtector protector, int skip) {
        ValidateCaller();
        ValidateOffset(skip);
        return ProtectedContinuation.Encode(protector, this with { Skip = skip });
    }

    internal int Decode(IDataProtector protector, string token) {
        ValidateCaller();
        try {
            var payload = ProtectedContinuation.Decode<InsightPageToken>(protector, token);
            if (payload.Binding != Binding || payload.Tenant != Tenant || payload.Caller != Caller) {
                throw Invalid();
            }

            ValidateOffset(payload.Skip);
            return payload.Skip;
        } catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or IOException or InvalidDataException) {
            throw new InsightValidationException(InsightReasons.InvalidArgument,
                SchemataResources.INVALID_PAGE_TOKEN, innerException: ex);
        }
    }

    internal static void ValidateOffset(int skip) {
        if (skip < 0) throw Invalid();
    }

    internal static int Advance(int skip, int pageSize) {
        try {
            return checked(skip + pageSize);
        } catch (OverflowException ex) {
            throw new InsightValidationException(InsightReasons.InvalidArgument,
                SchemataResources.INVALID_PAGE_TOKEN, innerException: ex);
        }
    }

    private void ValidateCaller() {
        if (Caller is not { } caller || caller.IsAuthenticated && string.IsNullOrWhiteSpace(caller.Subject)) {
            throw Invalid();
        }
    }

    private static InsightValidationException Invalid() => new(InsightReasons.InvalidArgument,
        SchemataResources.INVALID_PAGE_TOKEN);
}
