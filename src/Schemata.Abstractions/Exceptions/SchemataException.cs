using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Exceptions;

public class SchemataException : Exception
{
    public SchemataException(int status, string? code = null, string? message = null) : base(message) {
        StatusCode = status;
        Code       = code;
    }

    public int StatusCode { get; }

    public string? Code { get; }

    public Dictionary<string, string>? Errors { get; set; }

    public string? Error { get; set; }
}
