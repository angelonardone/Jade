using PeterO.Cbor;
using JadeClient.Exceptions;

namespace JadeClient.Protocol;

/// <summary>
/// Helper class for CBOR serialization/deserialization of Jade RPC messages.
/// </summary>
public static class CborSerializer
{
    /// <summary>
    /// Serialize an RPC request to CBOR bytes.
    /// </summary>
    /// <param name="request">The RPC request to serialize.</param>
    /// <returns>CBOR-encoded bytes.</returns>
    public static byte[] SerializeRequest(RpcRequest request)
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", request.Id);
        cbor.Add("method", request.Method);

        if (request.Params != null && request.Params.Count > 0)
        {
            cbor.Add("params", ConvertToCbor(request.Params));
        }

        return cbor.EncodeToBytes();
    }

    /// <summary>
    /// Deserialize CBOR bytes to an RPC response.
    /// </summary>
    /// <param name="data">CBOR-encoded bytes.</param>
    /// <returns>Parsed RPC response.</returns>
    public static RpcResponse DeserializeResponse(byte[] data)
    {
        try
        {
            var cbor = CBORObject.DecodeFromBytes(data);
            var response = new RpcResponse();

            if (cbor.ContainsKey("id"))
            {
                response.Id = cbor["id"].AsString();
            }

            if (cbor.ContainsKey("error"))
            {
                var errorCbor = cbor["error"];
                response.Error = new RpcError
                {
                    Code = errorCbor.ContainsKey("code") ? errorCbor["code"].AsInt32() : 0,
                    Message = errorCbor.ContainsKey("message") ? errorCbor["message"].AsString() : "Unknown error",
                    Data = errorCbor.ContainsKey("data") ? ConvertFromCbor(errorCbor["data"]) : null
                };
            }
            else if (cbor.ContainsKey("result"))
            {
                response.Result = ConvertFromCbor(cbor["result"]);
            }

            return response;
        }
        catch (CBORException ex)
        {
            throw new JadeException("Failed to deserialize CBOR response", ex);
        }
    }

    /// <summary>
    /// Convert a .NET object to CBOR object.
    /// </summary>
    public static CBORObject ConvertToCbor(object? value)
    {
        return value switch
        {
            null => CBORObject.Null,
            bool b => CBORObject.FromObject(b),
            int i => CBORObject.FromObject(i),
            long l => CBORObject.FromObject(l),
            uint u => CBORObject.FromObject(u),
            ulong ul => CBORObject.FromObject(ul),
            float f => CBORObject.FromObject(f),
            double d => CBORObject.FromObject(d),
            string s => CBORObject.FromObject(s),
            byte[] bytes => CBORObject.FromObject(bytes),
            uint[] uintArray => ConvertUintArrayToCbor(uintArray),
            int[] intArray => ConvertIntArrayToCbor(intArray),
            IDictionary<string, object> dict => ConvertDictionaryToCbor(dict),
            IEnumerable<object> enumerable => ConvertEnumerableToCbor(enumerable),
            CBORObject cbor => cbor,
            _ => CBORObject.FromObject(value.ToString())
        };
    }

    /// <summary>
    /// Convert a CBOR object to a .NET object.
    /// </summary>
    public static object? ConvertFromCbor(CBORObject? cbor)
    {
        if (cbor == null || cbor.IsNull)
            return null;

        if (cbor.IsUndefined)
            return null;

        switch (cbor.Type)
        {
            case CBORType.Boolean:
                return cbor.AsBoolean();

            case CBORType.Integer:
                // Try to return the most appropriate integer type
                if (cbor.CanValueFitInInt32())
                    return cbor.AsInt32();
                if (cbor.CanValueFitInInt64())
                    return cbor.AsNumber().ToInt64Checked();
                return cbor.ToObject<object>();

            case CBORType.FloatingPoint:
                return cbor.AsDouble();

            case CBORType.ByteString:
                return cbor.GetByteString();

            case CBORType.TextString:
                return cbor.AsString();

            case CBORType.Array:
                return ConvertArrayFromCbor(cbor);

            case CBORType.Map:
                return ConvertMapFromCbor(cbor);

            default:
                return cbor.ToObject<object>();
        }
    }

    private static CBORObject ConvertDictionaryToCbor(IDictionary<string, object> dict)
    {
        var cbor = CBORObject.NewMap();
        foreach (var kvp in dict)
        {
            cbor.Add(kvp.Key, ConvertToCbor(kvp.Value));
        }
        return cbor;
    }

    private static CBORObject ConvertEnumerableToCbor(IEnumerable<object> enumerable)
    {
        var cbor = CBORObject.NewArray();
        foreach (var item in enumerable)
        {
            cbor.Add(ConvertToCbor(item));
        }
        return cbor;
    }

    private static CBORObject ConvertUintArrayToCbor(uint[] array)
    {
        var cbor = CBORObject.NewArray();
        foreach (var item in array)
        {
            cbor.Add(CBORObject.FromObject(item));
        }
        return cbor;
    }

    private static CBORObject ConvertIntArrayToCbor(int[] array)
    {
        var cbor = CBORObject.NewArray();
        foreach (var item in array)
        {
            cbor.Add(CBORObject.FromObject(item));
        }
        return cbor;
    }

    private static List<object?> ConvertArrayFromCbor(CBORObject cbor)
    {
        var list = new List<object?>(cbor.Count);
        foreach (var item in cbor.Values)
        {
            list.Add(ConvertFromCbor(item));
        }
        return list;
    }

    private static Dictionary<string, object?> ConvertMapFromCbor(CBORObject cbor)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var key in cbor.Keys)
        {
            var keyStr = key.AsString();
            dict[keyStr] = ConvertFromCbor(cbor[key]);
        }
        return dict;
    }

    /// <summary>
    /// Extracts an HTTP request proxy instruction from a result if present.
    /// </summary>
    public static HttpRequestProxy? ExtractHttpRequest(object? result)
    {
        if (result is not Dictionary<string, object?> dict)
            return null;

        if (!dict.TryGetValue("http_request", out var httpRequestObj))
            return null;

        if (httpRequestObj is not Dictionary<string, object?> httpRequest)
            return null;

        var proxy = new HttpRequestProxy();

        if (httpRequest.TryGetValue("params", out var paramsObj) && paramsObj is Dictionary<string, object?> paramsDict)
        {
            if (paramsDict.TryGetValue("urls", out var urlsObj) && urlsObj is List<object?> urls)
            {
                proxy.Urls = urls.Where(u => u != null).Select(u => u!.ToString()!).ToList();
            }

            if (paramsDict.TryGetValue("method", out var methodObj))
            {
                proxy.Method = methodObj?.ToString() ?? "POST";
            }

            if (paramsDict.TryGetValue("accept", out var acceptObj))
            {
                proxy.Accept = acceptObj?.ToString() ?? "json";
            }

            if (paramsDict.TryGetValue("data", out var dataObj))
            {
                // Data can be a dictionary (for JSON) or a string
                if (dataObj is Dictionary<string, object?> dataDict)
                {
                    // Convert dictionary to JSON string
                    proxy.Data = System.Text.Json.JsonSerializer.Serialize(dataDict);
                }
                else
                {
                    proxy.Data = dataObj?.ToString();
                }
            }
        }

        if (httpRequest.TryGetValue("on-reply", out var onReplyObj))
        {
            proxy.OnReply = onReplyObj?.ToString() ?? string.Empty;
        }

        return proxy;
    }
}
