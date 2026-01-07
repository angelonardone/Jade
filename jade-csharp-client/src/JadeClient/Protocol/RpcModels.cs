namespace JadeClient.Protocol;

/// <summary>
/// Represents an RPC request to be sent to Jade.
/// </summary>
public class RpcRequest
{
    /// <summary>
    /// Unique request identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// RPC method name.
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Method parameters (optional).
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
}

/// <summary>
/// Represents an RPC response received from Jade.
/// </summary>
public class RpcResponse
{
    /// <summary>
    /// Request identifier (echoed from request).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Result data if successful.
    /// </summary>
    public object? Result { get; set; }

    /// <summary>
    /// Error information if failed.
    /// </summary>
    public RpcError? Error { get; set; }

    /// <summary>
    /// Check if response indicates success.
    /// </summary>
    public bool IsSuccess => Error == null;

    /// <summary>
    /// Check if result contains an HTTP request proxy instruction.
    /// </summary>
    public bool HasHttpRequest =>
        Result is IDictionary<string, object> dict &&
        dict.ContainsKey("http_request");
}

/// <summary>
/// Represents an RPC error response.
/// </summary>
public class RpcError
{
    /// <summary>
    /// Error code.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional error data (optional).
    /// </summary>
    public object? Data { get; set; }
}

/// <summary>
/// Represents an HTTP request that Jade wants the host to make.
/// Used for pinserver communication.
/// </summary>
public class HttpRequestProxy
{
    /// <summary>
    /// List of URLs to try (first non-onion preferred).
    /// </summary>
    public List<string> Urls { get; set; } = new();

    /// <summary>
    /// HTTP method (GET or POST).
    /// </summary>
    public string Method { get; set; } = "POST";

    /// <summary>
    /// Expected response type.
    /// </summary>
    public string Accept { get; set; } = "json";

    /// <summary>
    /// Request body data.
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// RPC method to call with the HTTP response.
    /// </summary>
    public string OnReply { get; set; } = string.Empty;

    /// <summary>
    /// Optional TLS certificate for pinserver validation.
    /// </summary>
    public string? Certificate { get; set; }
}
