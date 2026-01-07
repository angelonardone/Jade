namespace JadeClient.Exceptions;

/// <summary>
/// Base exception for all Jade-related errors.
/// </summary>
public class JadeException : Exception
{
    public JadeException(string message) : base(message) { }
    public JadeException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when connection to Jade device fails.
/// </summary>
public class JadeConnectionException : JadeException
{
    public JadeConnectionException(string message) : base(message) { }
    public JadeConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when Jade RPC call returns an error.
/// </summary>
public class JadeRpcException : JadeException
{
    /// <summary>
    /// RPC error code from Jade device.
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Additional error data if provided.
    /// </summary>
    public object? ErrorData { get; }

    public JadeRpcException(int errorCode, string message, object? errorData = null)
        : base($"RPC Error {errorCode}: {message}")
    {
        ErrorCode = errorCode;
        ErrorData = errorData;
    }

    /// <summary>
    /// Check if this error indicates the device is locked.
    /// </summary>
    public bool IsDeviceLocked => ErrorCode == RpcErrorCodes.HW_LOCKED;

    /// <summary>
    /// Check if this error indicates user cancelled the operation.
    /// </summary>
    public bool IsUserCancelled => ErrorCode == RpcErrorCodes.USER_CANCELLED;
}

/// <summary>
/// Standard Jade RPC error codes.
/// </summary>
public static class RpcErrorCodes
{
    public const int INVALID_REQUEST = -32600;
    public const int UNKNOWN_METHOD = -32601;
    public const int BAD_PARAMETERS = -32602;
    public const int INTERNAL_ERROR = -32603;
    public const int USER_CANCELLED = -32000;
    public const int PROTOCOL_ERROR = -32001;
    public const int HW_LOCKED = -32002;
    public const int NETWORK_MISMATCH = -32003;
}
