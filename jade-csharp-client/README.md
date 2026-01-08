# Jade C# Client Library

A C# library for communicating with Blockstream Jade hardware wallet devices using the CBOR-RPC protocol.

## Features

### Implemented (Phase 1 & 2A)

- **Serial Transport** - USB connection to Jade devices
- **CBOR-RPC Protocol** - Full request/response serialization
- **Device Info** - Get firmware version, board type, state, etc.
- **Entropy** - Add entropy to device RNG
- **PIN Authentication (Remote)** - Authenticate via Blockstream's PIN server (https://j8d.io)
- **Logout** - Lock the device wallet
- **PIN Server Configuration** - Update/reset PIN server settings

### Planned (Phase 2B+)

- **Local PIN Server** - In-process Blind Oracle for self-hosted PIN verification
- **Transaction Signing** - PSBT and legacy transaction signing
- **Message Signing** - Sign arbitrary messages
- **XPub Derivation** - Get extended public keys
- **Multisig Support** - Register and use multisig wallets
- **Bluetooth LE Transport** - Wireless connection to Jade

## Installation

```bash
dotnet add package JadeClient
```

Or add to your `.csproj`:

```xml
<PackageReference Include="JadeClient" Version="1.0.0" />
```

## Quick Start

### Basic Usage

```csharp
using JadeClient.Protocol;
using JadeClient.Transport;
using JadeClient.PinServer;

// Connect to Jade via USB serial
string portName = "/dev/cu.usbserial-XXX";  // macOS/Linux
// string portName = "COM3";                 // Windows

using var transport = new SerialTransport(portName);
using var rpc = new JadeRpc(transport);

await rpc.ConnectAsync();

// Get device info
var version = await rpc.GetVersionInfoAsync();
Console.WriteLine($"Jade Version: {version.JadeVersion}");
Console.WriteLine($"Board Type: {version.BoardType}");
Console.WriteLine($"State: {version.State}");
Console.WriteLine($"Has PIN: {version.HasPin}");

// Authenticate with PIN (user enters PIN on device)
if (version.HasPin)
{
    using var pinServer = new RemotePinServerHandler();
    bool authenticated = await rpc.AuthUserAsync(pinServer, "mainnet");

    if (authenticated)
    {
        Console.WriteLine("Device unlocked!");

        // ... perform wallet operations ...

        // Lock device when done
        await rpc.LogoutAsync();
    }
}

await rpc.DisconnectAsync();
```

### List Available Serial Ports

```csharp
using JadeClient.Transport;

var ports = SerialTransport.GetAvailablePorts();
foreach (var port in ports)
{
    Console.WriteLine(port);
}
```

### Custom PIN Server URL

```csharp
// Use a custom PIN server instead of Blockstream's default
using var pinServer = new RemotePinServerHandler("https://my-pinserver.example.com");
bool authenticated = await rpc.AuthUserAsync(pinServer, "mainnet");
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Your Bitcoin Wallet App                      │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      JadeRpc (Protocol Layer)                    │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │ Request Builder │  │ Response Parser │  │  Error Handler  │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
              ┌───────────────────┼───────────────────┐
              ▼                   ▼                   ▼
┌─────────────────────┐ ┌─────────────────┐ ┌─────────────────────┐
│   CBOR Serializer   │ │  PIN Server     │ │   Transport Layer   │
│  (PeterO.Cbor)      │ │  Handler        │ │  (Serial/BLE)       │
└─────────────────────┘ └────────┬────────┘ └─────────────────────┘
                                 │
              ┌──────────────────┼──────────────────┐
              ▼                                     ▼
┌─────────────────────────┐           ┌─────────────────────────┐
│  RemotePinServerHandler │           │  LocalPinServerHandler  │
│  (Blockstream/Custom)   │           │  (In-Process Oracle)    │
└─────────────────────────┘           └─────────────────────────┘
```

## PIN Server Modes

Jade uses a "Blind Oracle" PIN server to securely verify PINs without the server knowing the actual PIN. The library supports two modes:

### Remote Mode (Default)

Uses Blockstream's PIN server at `https://j8d.io`. This is the default and works with any Jade device initialized through Blockstream Green or other compatible wallets.

```csharp
using var pinServer = new RemotePinServerHandler();  // Uses Blockstream's server
await rpc.AuthUserAsync(pinServer, "mainnet");
```

### Local Mode (Coming Soon)

Self-hosted Blind Oracle running in-process. Requires the device to be configured with your server's public key.

```csharp
// Coming in Phase 2B
using var pinServer = new LocalPinServerHandler(new LocalPinServerOptions
{
    ServerKeyPath = "./pinserver.key",
    StoragePath = "./pins"
});
await rpc.AuthUserAsync(pinServer, "mainnet");
```

**Important:** A wallet initialized with one PIN server cannot be unlocked with a different server. The PIN is cryptographically bound to the server's public key.

## API Reference

### JadeRpc Class

| Method | Description |
|--------|-------------|
| `ConnectAsync()` | Connect to the Jade device |
| `DisconnectAsync()` | Disconnect from the device |
| `GetVersionInfoAsync()` | Get device firmware and state info |
| `AddEntropyAsync(byte[])` | Add entropy to device RNG |
| `AuthUserAsync(IPinServerHandler, string)` | Authenticate with PIN via PIN server |
| `LogoutAsync()` | Lock the device (logout) |
| `UpdatePinServerAsync(string, string?, byte[])` | Configure custom PIN server |
| `ResetPinServerAsync()` | Reset to Blockstream's PIN server |
| `Drain()` | Clear pending data from transport buffer |

### VersionInfo Properties

| Property | Type | Description |
|----------|------|-------------|
| `JadeVersion` | string | Firmware version (e.g., "1.0.38") |
| `BoardType` | string | Hardware type (e.g., "M5BLACKGRAY", "JADE") |
| `Config` | string | Build configuration |
| `Features` | string | Enabled features |
| `EfuseMac` | string | Device MAC address |
| `State` | JadeState | Current state (Uninit, Locked, Ready, Temp) |
| `Networks` | string | Supported networks |
| `HasPin` | bool | Whether a PIN is configured |
| `HasWallet` | bool | Whether a wallet is initialized |
| `IsUnlocked` | bool | Whether the device is currently unlocked |

### JadeState Enum

| Value | Description |
|-------|-------------|
| `Uninit` | Device not initialized (no wallet) |
| `Locked` | Wallet exists but locked (requires PIN) |
| `Ready` | Unlocked and ready for operations |
| `Temp` | Temporary wallet loaded |

## Project Structure

```
jade-csharp-client/
├── README.md                     # This file
├── src/
│   └── JadeClient/
│       ├── JadeClient.csproj     # Main library
│       ├── Client/
│       │   └── JadeClientOptions.cs
│       ├── Protocol/
│       │   ├── JadeRpc.cs        # RPC implementation
│       │   ├── CborSerializer.cs # CBOR encoding/decoding
│       │   ├── RpcRequest.cs
│       │   └── RpcResponse.cs
│       ├── Transport/
│       │   ├── IJadeTransport.cs
│       │   └── SerialTransport.cs
│       ├── PinServer/
│       │   ├── IPinServerHandler.cs
│       │   └── RemotePinServerHandler.cs
│       ├── Models/
│       │   ├── VersionInfo.cs
│       │   └── HttpRequestProxy.cs
│       └── Exceptions/
│           ├── JadeException.cs
│           ├── JadeConnectionException.cs
│           └── JadeRpcException.cs
├── tests/
│   └── JadeClient.Tests/
│       ├── CborSerializerTests.cs
│       └── JadeRpcTests.cs
└── samples/
    └── BasicUsage/
        └── Program.cs
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `PeterO.Cbor` | 4.5.2 | CBOR encoding/decoding |
| `System.IO.Ports` | 8.0.0 | Serial port communication |

## Building

```bash
# Build the library
dotnet build

# Run tests
dotnet test

# Run the sample
cd samples/BasicUsage
dotnet run
```

## Testing with Real Device

1. Connect your Jade device via USB
2. Find the serial port:
   - **macOS/Linux:** `/dev/cu.usbserial-*` or `/dev/ttyUSB*`
   - **Windows:** `COM3`, `COM4`, etc.
3. Update the port name in `samples/BasicUsage/Program.cs`
4. Run `dotnet run` in the samples directory

## Error Handling

```csharp
try
{
    await rpc.AuthUserAsync(pinServer, "mainnet");
}
catch (JadeConnectionException ex)
{
    // Connection failed (device not found, port busy, etc.)
    Console.WriteLine($"Connection error: {ex.Message}");
}
catch (JadeRpcException ex)
{
    // RPC error from device
    Console.WriteLine($"RPC error {ex.ErrorCode}: {ex.Message}");

    if (ex.IsDeviceLocked)
        Console.WriteLine("Device is locked - authentication required");
    if (ex.IsUserCancelled)
        Console.WriteLine("User cancelled the operation on device");
}
catch (TimeoutException ex)
{
    // Operation timed out
    Console.WriteLine($"Timeout: {ex.Message}");
}
```

## License

MIT License - Same as parent Jade project.

## References

- [Jade Firmware Repository](https://github.com/Blockstream/Jade)
- [Jade Python Client (jadepy)](https://github.com/Blockstream/Jade/tree/master/jadepy)
- [Blind Oracle PIN Server Documentation](https://github.com/Blockstream/blind_pin_server)
- [CBOR RFC 8949](https://www.rfc-editor.org/rfc/rfc8949.html)
