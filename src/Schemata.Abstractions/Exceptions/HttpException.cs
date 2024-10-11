using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Exceptions;

public class HttpException : Exception
{
    public HttpException(int status, string? message = "") : base(message) {
        StatusCode = status;
    }

    public int StatusCode { get; }

    public Dictionary<string, string>? Errors { get; set; }

    public string? Error { get; set; }
}
