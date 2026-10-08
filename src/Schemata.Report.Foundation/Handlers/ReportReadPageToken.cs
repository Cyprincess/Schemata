using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;

namespace Schemata.Report.Foundation.Handlers;

internal readonly record struct ReportReadPageToken(
    string Snapshot, int PageSize, Guid? Tenant, ProtectedContinuationCaller? Caller, int ChunkIndex, int Offset)
{
    internal const string ProtectionPurpose = "Schemata.Report.Foundation.PageToken";

    internal static ReportReadPageToken Bind(string snapshot, int pageSize, ClaimsPrincipal? principal) =>
        new(snapshot, pageSize, TenantContext.Current.Uid, ProtectedContinuationCaller.Bind(principal), 0, 0);

    internal string Encode(IDataProtector protector, int chunkIndex, int offset) {
        ValidateCaller();
        ValidatePosition(chunkIndex, offset);
        return ProtectedContinuation.Encode(protector, this with { ChunkIndex = chunkIndex, Offset = offset });
    }

    internal ReportReadPageToken Decode(IDataProtector protector, string token) {
        ValidateCaller();
        try {
            var payload = ProtectedContinuation.Decode<ReportReadPageToken>(protector, token);
            if (payload.Snapshot != Snapshot || payload.PageSize != PageSize
             || payload.Tenant != Tenant || payload.Caller != Caller) {
                throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
            }

            ValidatePosition(payload.ChunkIndex, payload.Offset);
            return payload;
        } catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or IOException or InvalidDataException) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
        }
    }

    internal static int Advance(int chunkIndex) {
        try {
            return checked(chunkIndex + 1);
        } catch (OverflowException) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
        }
    }

    private void ValidateCaller() {
        if (Caller is not { } caller || caller.IsAuthenticated && string.IsNullOrWhiteSpace(caller.Subject)) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
        }
    }

    private static void ValidatePosition(int chunkIndex, int offset) {
        if (chunkIndex < 0 || offset < 0) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAGE_TOKEN);
        }
    }
}
