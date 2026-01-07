using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.Protocol;
using JadeClient.Transport;
using Moq;
using PeterO.Cbor;
using Xunit;

namespace JadeClient.Tests;

public class JadeRpcTests
{
    private readonly Mock<IJadeTransport> _mockTransport;

    public JadeRpcTests()
    {
        _mockTransport = new Mock<IJadeTransport>();
    }

    [Fact]
    public async Task CallAsync_SuccessResponse_ReturnsResponse()
    {
        // Arrange
        var responseData = CreateSuccessResponse("1", true);
        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseData);

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        var response = await rpc.CallAsync("test_method");

        // Assert
        Assert.True(response.IsSuccess);
        Assert.Equal("1", response.Id);
        Assert.Equal(true, response.Result);
    }

    [Fact]
    public async Task CallAsync_ErrorResponse_ReturnsErrorResponse()
    {
        // Arrange
        var responseData = CreateErrorResponse("1", -32602, "Bad parameters");
        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseData);

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        var response = await rpc.CallAsync("test_method");

        // Assert
        Assert.False(response.IsSuccess);
        Assert.NotNull(response.Error);
        Assert.Equal(-32602, response.Error.Code);
        Assert.Equal("Bad parameters", response.Error.Message);
    }

    [Fact]
    public async Task CallAsync_Generic_ThrowsOnError()
    {
        // Arrange
        var responseData = CreateErrorResponse("1", -32002, "Device locked");
        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseData);

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<JadeRpcException>(() => rpc.CallAsync<bool>("test_method"));
        Assert.Equal(-32002, ex.ErrorCode);
        Assert.True(ex.IsDeviceLocked);
    }

    [Fact]
    public async Task CallAsync_NotConnected_ThrowsConnectionException()
    {
        // Arrange
        _mockTransport.Setup(t => t.IsConnected).Returns(false);

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act & Assert
        await Assert.ThrowsAsync<JadeConnectionException>(() => rpc.CallAsync("test_method"));
    }

    [Fact]
    public async Task CallAsync_WithParameters_SendsParameters()
    {
        // Arrange
        byte[]? sentData = null;
        var responseData = CreateSuccessResponse("1", "xpub...");
        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<byte[], CancellationToken>((data, _) => sentData = data)
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseData);

        using var rpc = new JadeRpc(_mockTransport.Object);
        var parameters = new Dictionary<string, object>
        {
            ["network"] = "mainnet",
            ["path"] = new uint[] { 0x80000054, 0x80000000, 0x80000000 }
        };

        // Act
        await rpc.CallAsync("get_xpub", parameters);

        // Assert
        Assert.NotNull(sentData);
        var decoded = CBORObject.DecodeFromBytes(sentData);
        Assert.Equal("get_xpub", decoded["method"].AsString());
        Assert.True(decoded.ContainsKey("params"));
        Assert.Equal("mainnet", decoded["params"]["network"].AsString());
    }

    [Fact]
    public async Task GetVersionInfoAsync_ParsesVersionInfo()
    {
        // Arrange
        var versionResult = CBORObject.NewMap();
        versionResult.Add("JADE_VERSION", "1.0.38");
        versionResult.Add("JADE_OTA_MAX_CHUNK", 4096);
        versionResult.Add("JADE_CONFIG", "BLE");
        versionResult.Add("BOARD_TYPE", "JADE");
        versionResult.Add("JADE_FEATURES", "SB");
        versionResult.Add("EFUSEMAC", "2CBCBB972FE4");
        versionResult.Add("JADE_STATE", "READY");
        versionResult.Add("JADE_NETWORKS", "ALL");
        versionResult.Add("JADE_HAS_PIN", true);

        var responseCbor = CBORObject.NewMap();
        responseCbor.Add("id", "1");
        responseCbor.Add("result", versionResult);

        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseCbor.EncodeToBytes());

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        var info = await rpc.GetVersionInfoAsync();

        // Assert
        Assert.Equal("1.0.38", info.JadeVersion);
        Assert.Equal(4096, info.OtaMaxChunk);
        Assert.Equal("BLE", info.Config);
        Assert.Equal("JADE", info.BoardType);
        Assert.Equal("SB", info.Features);
        Assert.Equal("2CBCBB972FE4", info.EfuseMac);
        Assert.Equal(JadeState.Ready, info.State);
        Assert.Equal("ALL", info.Networks);
        Assert.True(info.HasPin);
        Assert.True(info.IsUnlocked);
        Assert.True(info.HasWallet);
    }

    [Fact]
    public async Task GetVersionInfoAsync_LockedState_ParsesCorrectly()
    {
        // Arrange
        var versionResult = CBORObject.NewMap();
        versionResult.Add("JADE_VERSION", "1.0.38");
        versionResult.Add("JADE_STATE", "LOCKED");
        versionResult.Add("JADE_HAS_PIN", true);

        var responseCbor = CBORObject.NewMap();
        responseCbor.Add("id", "1");
        responseCbor.Add("result", versionResult);

        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseCbor.EncodeToBytes());

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        var info = await rpc.GetVersionInfoAsync();

        // Assert
        Assert.Equal(JadeState.Locked, info.State);
        Assert.False(info.IsUnlocked);
        Assert.True(info.HasWallet);
    }

    [Fact]
    public async Task AddEntropyAsync_SendsEntropyBytes()
    {
        // Arrange
        byte[]? sentData = null;
        var responseData = CreateSuccessResponse("1", true);
        _mockTransport.Setup(t => t.IsConnected).Returns(true);
        _mockTransport.Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<byte[], CancellationToken>((data, _) => sentData = data)
            .Returns(Task.CompletedTask);
        _mockTransport.Setup(t => t.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseData);

        using var rpc = new JadeRpc(_mockTransport.Object);
        var entropy = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        // Act
        var result = await rpc.AddEntropyAsync(entropy);

        // Assert
        Assert.True(result);
        Assert.NotNull(sentData);
        var decoded = CBORObject.DecodeFromBytes(sentData);
        Assert.Equal("add_entropy", decoded["method"].AsString());
        Assert.True(decoded.ContainsKey("params"));
        Assert.Equal(entropy, decoded["params"]["entropy"].GetByteString());
    }

    [Fact]
    public async Task ConnectAsync_CallsTransport()
    {
        // Arrange
        _mockTransport.Setup(t => t.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        await rpc.ConnectAsync();

        // Assert
        _mockTransport.Verify(t => t.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_CallsTransport()
    {
        // Arrange
        _mockTransport.Setup(t => t.DisconnectAsync())
            .Returns(Task.CompletedTask);

        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        await rpc.DisconnectAsync();

        // Assert
        _mockTransport.Verify(t => t.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public void Drain_CallsTransport()
    {
        // Arrange
        using var rpc = new JadeRpc(_mockTransport.Object);

        // Act
        rpc.Drain();

        // Assert
        _mockTransport.Verify(t => t.Drain(), Times.Once);
    }

    [Fact]
    public void Dispose_WithOwnedTransport_DisposesTransport()
    {
        // Arrange
        var rpc = new JadeRpc(_mockTransport.Object, ownsTransport: true);

        // Act
        rpc.Dispose();

        // Assert
        _mockTransport.Verify(t => t.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_WithNonOwnedTransport_DoesNotDisposeTransport()
    {
        // Arrange
        var rpc = new JadeRpc(_mockTransport.Object, ownsTransport: false);

        // Act
        rpc.Dispose();

        // Assert
        _mockTransport.Verify(t => t.Dispose(), Times.Never);
    }

    private static byte[] CreateSuccessResponse(string id, object result)
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", id);
        cbor.Add("result", CborSerializer.ConvertToCbor(result));
        return cbor.EncodeToBytes();
    }

    private static byte[] CreateErrorResponse(string id, int code, string message)
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("id", id);
        var error = CBORObject.NewMap();
        error.Add("code", code);
        error.Add("message", message);
        cbor.Add("error", error);
        return cbor.EncodeToBytes();
    }
}
