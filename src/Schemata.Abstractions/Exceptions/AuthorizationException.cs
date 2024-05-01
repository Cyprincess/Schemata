namespace Schemata.Abstractions.Exceptions;

public class AuthorizationException(int status = 401, string? message = "") : HttpException(status, message);
