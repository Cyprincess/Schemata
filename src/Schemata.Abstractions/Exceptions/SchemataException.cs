using System;
using System.Collections.Generic;
using Schemata.Abstractions.Errors;

namespace Schemata.Abstractions.Exceptions;

public class SchemataException : Exception
{
    public SchemataException(int status, string? code = null, string? message = null) : base(message) {
        Status = status;
        Code   = code;
    }

    public int Status { get; }

    public string? Code { get; }

    public List<IErrorDetail>? Details { get; set; }
}
