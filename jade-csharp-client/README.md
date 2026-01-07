# Jade C# Client Library

A C# library for communicating with Blockstream Jade hardware wallet devices using the CBOR-RPC protocol.

## Project Overview

This library provides a .NET implementation of the Jade hardware wallet communication protocol, enabling C# applications to:

- Connect to Jade devices via Serial (USB) or Bluetooth LE
- Authenticate users with PIN via the blind oracle pinserver
- Sign Bitcoin transactions (PSBT and legacy formats)
- Sign messages
- Get extended public keys (xpubs)
- Manage multisig and descriptor wallets
- OTP (TOTP/HOTP) functionality

## Target Framework

- .NET 6.0+ / .NET Standard 2.1
- Compatible with GeneXus .NET generators

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Your Bitcoin Wallet App                      │
│                    (GeneXus / C# Application)                    │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      JadeClient (High-Level API)                 │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────────┐ │
│  │ AuthManager │ │TxSigner     │ │ XPubManager │ │MessageSigner│ │
│  └─────────────┘ └─────────────┘ └─────────────┘ └────────────┘ │
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
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      CBOR Serialization                          │
│                    (PeterO.Cbor NuGet package)                   │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Transport Layer                             │
│  ┌─────────────────────────┐    ┌─────────────────────────────┐ │
│  │   SerialTransport       │    │      BleTransport           │ │
│  │ (System.IO.Ports)       │    │ (Plugin.BLE / Windows.BLE)  │ │
│  └─────────────────────────┘    └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
                          ┌───────────────┐
                          │  Jade Device  │
                          └───────────────┘
```

## Project Structure

```
jade-csharp-client/
├── README.md                    # This file
├── PROTOCOL.md                  # RPC protocol documentation
├── IMPLEMENTATION_PLAN.md       # Detailed implementation phases
├── src/
│   └── JadeClient/
│       ├── JadeClient.csproj    # Main library project
│       ├── Client/
│       │   ├── JadeClient.cs    # High-level API
│       │   └── JadeClientOptions.cs
│       ├── Protocol/
│       │   ├── JadeRpc.cs       # RPC protocol implementation
│       │   ├── RpcRequest.cs
│       │   ├── RpcResponse.cs
│       │   └── RpcError.cs
│       ├── Transport/
│       │   ├── IJadeTransport.cs
│       │   ├── SerialTransport.cs
│       │   └── BleTransport.cs
│       ├── Models/
│       │   ├── VersionInfo.cs
│       │   ├── SignedTransaction.cs
│       │   ├── XPubInfo.cs
│       │   └── ...
│       └── Exceptions/
│           ├── JadeException.cs
│           ├── JadeConnectionException.cs
│           └── JadeRpcException.cs
├── tests/
│   └── JadeClient.Tests/
│       ├── JadeClient.Tests.csproj
│       ├── RpcTests.cs
│       └── TransportTests.cs
└── samples/
    └── BasicUsage/
        └── Program.cs           # Example usage
```

## Dependencies

| Package | Purpose |
|---------|---------|
| `PeterO.Cbor` | CBOR encoding/decoding |
| `System.IO.Ports` | Serial port communication |
| `Plugin.BLE` or `Windows.Devices.Bluetooth` | Bluetooth LE (optional) |

## Quick Start (Target API)

```csharp
using JadeClient;

// Create client with serial connection
var jade = new JadeClient("/dev/cu.usbserial-XXX");
// or on Windows: new JadeClient("COM3");

await jade.ConnectAsync();

// Get device info
var version = await jade.GetVersionInfoAsync();
Console.WriteLine($"Jade Version: {version.JadeVersion}");

// Authenticate (requires user PIN entry on device)
bool authenticated = await jade.AuthUserAsync("mainnet");

if (authenticated)
{
    // Get xpub for BIP84 (native segwit)
    var xpub = await jade.GetXPubAsync("m/84'/0'/0'");

    // Sign a PSBT
    byte[] signedPsbt = await jade.SignPsbtAsync(psbtBytes);
}

await jade.DisconnectAsync();
```

## License

MIT License - Same as parent Jade project.

## References

- [Jade Firmware Repository](https://github.com/Blockstream/Jade)
- [Jade Python Client (jadepy)](../jadepy/) - Reference implementation
- [CBOR RFC 8949](https://www.rfc-editor.org/rfc/rfc8949.html)
