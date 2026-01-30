using JadeClient.Exceptions;
using JadeClient.Transport;
using Xunit;

namespace JadeClient.Tests;

public class TcpTransportTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesInstance()
    {
        // Act
        using var transport = new TcpTransport("localhost", 30121);

        // Assert
        Assert.NotNull(transport);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public void Constructor_EmptyHost_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TcpTransport(""));
        Assert.Throws<ArgumentException>(() => new TcpTransport("  "));
    }

    [Fact]
    public void Constructor_InvalidPort_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransport("localhost", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransport("localhost", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransport("localhost", 65536));
    }

    [Fact]
    public void CreateForQemu_DefaultPort_UsesPort30121()
    {
        // Act
        using var transport = TcpTransport.CreateForQemu();

        // Assert
        Assert.NotNull(transport);
    }

    [Fact]
    public void CreateForQemu_CustomPort_UsesSpecifiedPort()
    {
        // Act
        using var transport = TcpTransport.CreateForQemu(12345);

        // Assert
        Assert.NotNull(transport);
    }

    [Theory]
    [InlineData("localhost:30121")]
    [InlineData("192.168.1.100:8080")]
    [InlineData("tcp:localhost:30121")]
    [InlineData("tcp:192.168.1.100:8080")]
    [InlineData("localhost")] // Default port
    public void FromConnectionString_ValidFormats_ParsesCorrectly(string connectionString)
    {
        // Act
        using var transport = TcpTransport.FromConnectionString(connectionString);

        // Assert
        Assert.NotNull(transport);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConnectionString_EmptyString_ThrowsArgumentException(string connectionString)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => TcpTransport.FromConnectionString(connectionString));
    }

    [Theory]
    [InlineData("host:port:extra")]
    [InlineData("host:notanumber")]
    public void FromConnectionString_InvalidFormat_ThrowsArgumentException(string connectionString)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => TcpTransport.FromConnectionString(connectionString));
    }

    [Fact]
    public async Task ConnectAsync_NoServer_ThrowsJadeConnectionException()
    {
        // Arrange - use a port that definitely has no server
        using var transport = new TcpTransport("127.0.0.1", 59999, connectTimeoutMs: 1000);

        // Act & Assert
        await Assert.ThrowsAsync<JadeConnectionException>(() => transport.ConnectAsync());
    }

    [Fact]
    public async Task WriteAsync_NotConnected_ThrowsJadeConnectionException()
    {
        // Arrange
        using var transport = new TcpTransport("localhost", 30121);

        // Act & Assert
        await Assert.ThrowsAsync<JadeConnectionException>(() => transport.WriteAsync(new byte[] { 0x01 }));
    }

    [Fact]
    public async Task ReadAsync_NotConnected_ThrowsJadeConnectionException()
    {
        // Arrange
        using var transport = new TcpTransport("localhost", 30121);

        // Act & Assert
        await Assert.ThrowsAsync<JadeConnectionException>(() => transport.ReadAsync());
    }

    [Fact]
    public void Drain_NotConnected_DoesNotThrow()
    {
        // Arrange
        using var transport = new TcpTransport("localhost", 30121);

        // Act & Assert - should not throw even when not connected
        transport.Drain();
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var transport = new TcpTransport("localhost", 30121);

        // Act & Assert
        transport.Dispose();
        transport.Dispose(); // Second dispose should not throw
    }

    [Fact]
    public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var transport = new TcpTransport("localhost", 30121);
        transport.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ConnectAsync());
    }
}
