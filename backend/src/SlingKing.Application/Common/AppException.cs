namespace SlingKing.Application.Common;

/// <summary>
/// A failure the caller caused and is allowed to read about. Every message on this type is
/// written by us and carries nothing from an inner exception, a SQL fragment or a type name,
/// which is what makes it safe for <c>ProblemDetailsExceptionHandler</c> to return verbatim.
/// Anything else thrown still becomes a generic 500.
/// </summary>
public sealed class AppException : Exception
{
    public int StatusCode { get; }

    private AppException(int statusCode, string message, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;

    public static AppException NotFound(string message) => new(404, message);

    /// <summary>
    /// <paramref name="inner"/> is for a rejection we inferred from a database failure rather
    /// than checked ourselves. It never reaches the client — the handler logs it so an
    /// unexpected failure is not silently dressed up as an ordinary conflict.
    /// </summary>
    public static AppException Conflict(string message, Exception? inner = null) =>
        new(409, message, inner);

    public static AppException Invalid(string message) => new(400, message);
}
