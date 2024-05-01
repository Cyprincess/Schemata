using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Exceptions;

public class HttpException(int status, string? message = "") : Exception(message)
{
    public int StatusCode { get; } = status;

    public Dictionary<string, string>? Errors { get; set; }

    public string? Error { get; set; }
}
