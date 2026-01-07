using JadeClient.Protocol;
using PeterO.Cbor;
using Xunit;

namespace JadeClient.Tests;

public class CborSerializerTests
{
    [Fact]
    public void SerializeRequest_BasicRequest_ProducesValidCbor()
    {
        var request = new RpcRequest
        {
            Id = "1",
            Method = "get_version_info"
        };

        var bytes = CborSerializer.SerializeRequest(request);
        var decoded = CBORObject.DecodeFromBytes(bytes);

        Assert.Equal("1", decoded["id"].AsString());
        Assert.Equal("get_version_info", decoded["method"].AsString());
        Assert.False(decoded.ContainsKey("params"));
    }

    [Fact]
    public void SerializeRequest_WithParams_IncludesParams()
    {
        var request = new RpcRequest
        {
            Id = "2",
            Method = "auth_user",
            Params = new Dictionary<string, object>
            {
                ["network"] = "mainnet",
                ["epoch"] = 1704672000
            }
        };

        var bytes = CborSerializer.SerializeRequest(request);
        var decoded = CBORObject.DecodeFromBytes(bytes);

        Assert.Equal("2", decoded["id"].AsString());
        Assert.Equal("auth_user", decoded["method"].AsString());
        Assert.True(decoded.ContainsKey("params"));
        Assert.Equal("mainnet", decoded["params"]["network"].AsString());
        Assert.Equal(1704672000, decoded["params"]["epoch"].AsInt32());
    }

    [Fact]
    public void SerializeRequest_WithPath_SerializesUintArray()
    {
        var request = new RpcRequest
        {
            Id = "3",
            Method = "get_xpub",
            Params = new Dictionary<string, object>
            {
                ["network"] = "mainnet",
                ["path"] = new uint[] { 0x80000054, 0x80000000, 0x80000000 }
            }
        };

        var bytes = CborSerializer.SerializeRequest(request);
        var decoded = CBORObject.DecodeFromBytes(bytes);

        var path = decoded["params"]["path"];
        Assert.Equal(3, path.Count);
        Assert.Equal(0x80000054u, (uint)path[0].AsNumber().ToInt64Checked());
        Assert.Equal(0x80000000u, (uint)path[1].AsNumber().ToInt64Checked());
        Assert.Equal(0x80000000u, (uint)path[2].AsNumber().ToInt64Checked());
    }

    [Fact]
    public void DeserializeResponse_SuccessResponse_ParsesCorrectly()
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", "1");
        cbor.Add("result", true);
        var bytes = cbor.EncodeToBytes();

        var response = CborSerializer.DeserializeResponse(bytes);

        Assert.Equal("1", response.Id);
        Assert.True(response.IsSuccess);
        Assert.Equal(true, response.Result);
        Assert.Null(response.Error);
    }

    [Fact]
    public void DeserializeResponse_ErrorResponse_ParsesCorrectly()
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", "1");
        var error = CBORObject.NewMap();
        error.Add("code", -32602);
        error.Add("message", "Invalid parameters");
        cbor.Add("error", error);
        var bytes = cbor.EncodeToBytes();

        var response = CborSerializer.DeserializeResponse(bytes);

        Assert.Equal("1", response.Id);
        Assert.False(response.IsSuccess);
        Assert.NotNull(response.Error);
        Assert.Equal(-32602, response.Error.Code);
        Assert.Equal("Invalid parameters", response.Error.Message);
    }

    [Fact]
    public void DeserializeResponse_VersionInfo_ParsesMap()
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", "1");
        var result = CBORObject.NewMap();
        result.Add("JADE_VERSION", "1.0.38");
        result.Add("JADE_STATE", "READY");
        result.Add("JADE_HAS_PIN", true);
        cbor.Add("result", result);
        var bytes = cbor.EncodeToBytes();

        var response = CborSerializer.DeserializeResponse(bytes);

        Assert.True(response.IsSuccess);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Equal("1.0.38", resultDict["JADE_VERSION"]);
        Assert.Equal("READY", resultDict["JADE_STATE"]);
        Assert.Equal(true, resultDict["JADE_HAS_PIN"]);
    }

    [Fact]
    public void DeserializeResponse_ByteString_ParsesCorrectly()
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", "1");
        cbor.Add("result", CBORObject.FromObject(new byte[] { 0x01, 0x02, 0x03 }));
        var bytes = cbor.EncodeToBytes();

        var response = CborSerializer.DeserializeResponse(bytes);

        var resultBytes = response.Result as byte[];
        Assert.NotNull(resultBytes);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, resultBytes);
    }

    [Fact]
    public void ConvertToCbor_Dictionary_ConvertsCorrectly()
    {
        var dict = new Dictionary<string, object>
        {
            ["string"] = "value",
            ["int"] = 42,
            ["bool"] = true
        };

        var cbor = CborSerializer.ConvertToCbor(dict);

        Assert.Equal("value", cbor["string"].AsString());
        Assert.Equal(42, cbor["int"].AsInt32());
        Assert.True(cbor["bool"].AsBoolean());
    }

    [Fact]
    public void ConvertFromCbor_Array_ConvertsCorrectly()
    {
        var cbor = CBORObject.NewArray();
        cbor.Add(1);
        cbor.Add(2);
        cbor.Add(3);

        var result = CborSerializer.ConvertFromCbor(cbor) as List<object?>;

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
        Assert.Equal(3, result[2]);
    }

    [Fact]
    public void ExtractHttpRequest_ValidHttpRequest_ExtractsCorrectly()
    {
        var result = new Dictionary<string, object?>
        {
            ["http_request"] = new Dictionary<string, object?>
            {
                ["params"] = new Dictionary<string, object?>
                {
                    ["urls"] = new List<object?> { "https://j8d.io/get_pin", "http://xxx.onion/get_pin" },
                    ["method"] = "POST",
                    ["accept"] = "json",
                    ["data"] = "base64data"
                },
                ["on-reply"] = "pin"
            }
        };

        var httpRequest = CborSerializer.ExtractHttpRequest(result);

        Assert.NotNull(httpRequest);
        Assert.Equal(2, httpRequest.Urls.Count);
        Assert.Equal("https://j8d.io/get_pin", httpRequest.Urls[0]);
        Assert.Equal("POST", httpRequest.Method);
        Assert.Equal("json", httpRequest.Accept);
        Assert.Equal("base64data", httpRequest.Data);
        Assert.Equal("pin", httpRequest.OnReply);
    }

    [Fact]
    public void ExtractHttpRequest_NoHttpRequest_ReturnsNull()
    {
        var result = new Dictionary<string, object?>
        {
            ["some_key"] = "some_value"
        };

        var httpRequest = CborSerializer.ExtractHttpRequest(result);

        Assert.Null(httpRequest);
    }

    [Fact]
    public void ExtractHttpRequest_NullResult_ReturnsNull()
    {
        var httpRequest = CborSerializer.ExtractHttpRequest(null);
        Assert.Null(httpRequest);
    }
}
