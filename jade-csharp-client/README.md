# Jade C# Client Library

A C# library for communicating with Blockstream Jade hardware wallet devices using the CBOR-RPC protocol.

## Features

### Implemented

- **Serial Transport** - USB connection to Jade devices
- **TCP Transport** - Network connection to QEMU emulator or remote devices
- **CBOR-RPC Protocol** - Full request/response serialization
- **Device Info** - Get firmware version, board type, state, etc.
- **Entropy** - Add entropy to device RNG
- **PIN Authentication (Remote)** - Authenticate via Blockstream's PIN server (https://j8d.io)
- **Logout** - Lock the device wallet
- **PIN Server Configuration** - Update/reset PIN server settings
- **HSM Mode** - Hardware Security Module operations:
  - Get HSM status and key info
  - Public key and XPub derivation
  - Schnorr (BIP-340) and ECDSA signing
  - ECDH shared secret computation
  - ECIES encryption/decryption (AES-256-GCM)

### Planned

- **Local PIN Server** - In-process Blind Oracle for self-hosted PIN verification
- **Transaction Signing** - PSBT and legacy transaction signing
- **Message Signing** - Sign arbitrary messages
- **XPub Derivation** - Get extended public keys for wallet paths
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

### Testing with QEMU Emulator (No Hardware Required)

You can test the C# client without a physical Jade device using the QEMU emulator:

```csharp
using JadeClient.Transport;
using JadeClient.Protocol;

// Connect to QEMU emulator via TCP
using var transport = new TcpTransport("localhost", 30121);
using var rpc = new JadeRpc(transport);

await rpc.ConnectAsync();
var version = await rpc.GetVersionInfoAsync();
Console.WriteLine($"QEMU Jade: {version.JadeVersion}");
```

See [EMULATOR_TESTING.md](EMULATOR_TESTING.md) for complete setup instructions.

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

## HSM Mode

HSM (Hardware Security Module) Mode provides isolated cryptographic operations without exposing Bitcoin wallet keys. Keys are derived from a dedicated branch (`m/86'/coin'/0'/6000'/*`) and support both mainnet and testnet simultaneously.

### Activating HSM Mode

HSM mode must be activated from the Jade device menu:
1. Unlock the device with PIN
2. Navigate to: Menu → Session → HSM Mode
3. Confirm activation on device

### HSM Usage Example

```csharp
using JadeClient.Protocol;
using JadeClient.Transport;
using JadeClient.PinServer;
using JadeClient.Models;

// Connect and authenticate
using var transport = new SerialTransport(portName);
using var rpc = new JadeRpc(transport);
await rpc.ConnectAsync();

var pinServer = new RemotePinServerHandler();
await rpc.AuthUserAsync(pinServer, "mainnet");

// Check HSM status
var hsmInfo = await rpc.HsmGetInfoAsync();
if (!hsmInfo.Active)
{
    Console.WriteLine("Please activate HSM mode from Jade menu");
    // Wait for user to activate HSM mode on device
}

// Once HSM is active:

// Get public key at index 0
var pubkeyResult = await rpc.HsmGetPubkeyAsync("mainnet", 0);
Console.WriteLine($"Pubkey: {BitConverter.ToString(pubkeyResult.Pubkey)}");
Console.WriteLine($"Path: {pubkeyResult.Path}");

// Get extended public key (for external key derivation)
var xpubResult = await rpc.HsmGetXpubAsync("mainnet");
Console.WriteLine($"XPub: {xpubResult.Xpub}");

// Sign a hash with Schnorr (BIP-340)
byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("message"));
var signResult = await rpc.HsmSignAsync("mainnet", 0, hash, "schnorr");
Console.WriteLine($"Signature ({signResult.Algorithm}): {BitConverter.ToString(signResult.Signature)}");

// Sign with ECDSA
var ecdsaResult = await rpc.HsmSignAsync("mainnet", 0, hash, "ecdsa");
Console.WriteLine($"Signature (DER): {BitConverter.ToString(ecdsaResult.Signature)}");

// Compute ECDH shared secret
var theirPubkey = await rpc.HsmGetPubkeyAsync("mainnet", 1);
var sharedSecret = await rpc.HsmEcdhAsync("mainnet", 0, theirPubkey.Pubkey);
Console.WriteLine($"Shared secret: {BitConverter.ToString(sharedSecret)}");

// ECIES Encryption (encrypt to self)
byte[] plaintext = Encoding.UTF8.GetBytes("Secret message");
var encrypted = await rpc.HsmEncryptAsync("mainnet", 0, plaintext);

// ECIES Decryption
var decrypted = await rpc.HsmDecryptAsync(
    "mainnet", 0,
    encrypted.Ciphertext,
    encrypted.Nonce,
    encrypted.Tag,
    encrypted.EphemeralPubkey);
Console.WriteLine($"Decrypted: {Encoding.UTF8.GetString(decrypted)}");

// Lock HSM mode when done
await rpc.HsmLockAsync();
```

### HSM Key Derivation Paths

| Network | Root Path | Index Path |
|---------|-----------|------------|
| Mainnet | `m/86'/0'/0'/6000'` | `m/86'/0'/0'/6000'/{index}` |
| Testnet | `m/86'/1'/0'/6000'` | `m/86'/1'/0'/6000'/{index}` |

- Index is non-hardened (0 to 2^31-1)
- Both networks are available simultaneously when HSM mode is active
- Keys are deterministic: same index always produces the same key

### HSM Data Models

```csharp
// HSM status information
public class HsmInfo
{
    public bool Active { get; set; }
    public string[] Networks { get; set; }
    public string? MainnetRootPath { get; set; }
    public string? TestnetRootPath { get; set; }
    public byte[]? MainnetRootPubkey { get; set; }
    public byte[]? TestnetRootPubkey { get; set; }
    public ulong OperationsCount { get; set; }
    public uint AutoLockTimeout { get; set; }
}

// Public key result
public class HsmPubkeyResult
{
    public byte[] Pubkey { get; set; }  // 33 bytes, compressed
    public string Path { get; set; }     // e.g., "m/86'/0'/0'/6000'/0"
}

// Extended public key result
public class HsmXpubResult
{
    public string Xpub { get; set; }    // Base58-encoded xpub/tpub
    public string Path { get; set; }
}

// Sign result
public class HsmSignResult
{
    public byte[] Signature { get; set; }  // 64 bytes (Schnorr) or DER (ECDSA)
    public byte[] Pubkey { get; set; }     // Signing public key
    public string Algorithm { get; set; }  // "schnorr" or "ecdsa"
}

// ECIES encryption result
public class HsmEncryptResult
{
    public byte[] Ciphertext { get; set; }
    public byte[] Nonce { get; set; }          // 12 bytes
    public byte[] Tag { get; set; }            // 16 bytes
    public byte[] EphemeralPubkey { get; set; } // 33 bytes
}
```

For detailed HSM design documentation, see [HSM_MODE_DESIGN.md](../docs/HSM_MODE_DESIGN.md).

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

### HSM Mode Methods

| Method | Description |
|--------|-------------|
| `HsmGetInfoAsync()` | Get HSM status, networks, paths, pubkeys, counters |
| `HsmGetPubkeyAsync(network, index)` | Get public key at index (33 bytes, compressed) |
| `HsmGetXpubAsync(network)` | Get extended public key (base58 encoded) |
| `HsmSignAsync(network, index, hash, algo)` | Sign 32-byte hash (Schnorr or ECDSA) |
| `HsmEcdhAsync(network, index, theirPubkey)` | Compute ECDH shared secret |
| `HsmEncryptAsync(network, index, plaintext, ...)` | ECIES encryption |
| `HsmDecryptAsync(network, index, ciphertext, ...)` | ECIES decryption |
| `HsmLockAsync()` | Deactivate HSM mode |

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
│       │   ├── JadeRpc.cs        # RPC implementation (includes HSM methods)
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
│       │   ├── HsmModels.cs      # HSM data models
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
    ├── BasicUsage/
    │   └── Program.cs
    └── HsmTest/                  # HSM mode test sample
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
