namespace GxJadeLib.Models;

/// <summary>
/// Standard result wrapper for all GxJadeWrapper operations.
/// Follows the pattern established for GeneXus External Object compatibility.
/// </summary>
public class JadeOperationResult
{
    /// <summary>
    /// Whether the operation completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the operation failed, empty string otherwise.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// The connection ID associated with this operation.
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Optional response message (JSON or simple value).
    /// </summary>
    public string ResponseMessage { get; set; } = string.Empty;

    /// <summary>
    /// RPC error code if applicable, -1 otherwise.
    /// </summary>
    public int ErrorCode { get; set; } = -1;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="response">Optional response message.</param>
    /// <returns>A successful JadeOperationResult.</returns>
    public static JadeOperationResult Ok(Guid connectionId, string response = "")
    {
        return new JadeOperationResult
        {
            Success = true,
            ConnectionId = connectionId,
            ResponseMessage = response,
            ErrorMessage = string.Empty,
            ErrorCode = 0
        };
    }

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="error">The error message.</param>
    /// <param name="code">The error code (default -1).</param>
    /// <returns>A failed JadeOperationResult.</returns>
    public static JadeOperationResult Fail(Guid connectionId, string error, int code = -1)
    {
        return new JadeOperationResult
        {
            Success = false,
            ConnectionId = connectionId,
            ErrorMessage = error,
            ErrorCode = code,
            ResponseMessage = string.Empty
        };
    }

    /// <summary>
    /// Creates a failure result with no connection.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="code">The error code (default -1).</param>
    /// <returns>A failed JadeOperationResult.</returns>
    public static JadeOperationResult Fail(string error, int code = -1)
    {
        return Fail(Guid.Empty, error, code);
    }
}
