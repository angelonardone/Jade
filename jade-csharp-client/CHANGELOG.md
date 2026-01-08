# Changelog

All notable changes to the Jade C# Client Library.

## [0.2.0] - 2026-01-08

### Added - Phase 2A: Remote PIN Server Authentication

- **PIN Server Integration**
  - `IPinServerHandler` interface for pluggable PIN server implementations
  - `RemotePinServerHandler` - HTTP proxy to Blockstream's PIN server (https://j8d.io)
  - Support for custom remote PIN server URLs
  - Server public key management for custom servers

- **Authentication Methods**
  - `AuthUserAsync()` - Full PIN authentication flow with http_request loop handling
  - `LogoutAsync()` - Lock the device wallet
  - `UpdatePinServerAsync()` - Configure custom PIN server on device
  - `ResetPinServerAsync()` - Reset to Blockstream's default PIN server

- **Configuration**
  - `PinServerMode` enum (Remote, Local)
  - `LocalPinServerOptions` class for future local mode configuration
  - Updated `JadeClientOptions` with PIN server settings

- **Protocol Enhancements**
  - `HttpRequestProxy` model for Jade's http_request messages
  - `ExtractHttpRequest()` in CborSerializer for parsing proxy requests
  - JSON serialization for dictionary data in http_request params

### Fixed

- Correct data format for PIN server communication (JSON body, not wrapped)
- Proper parsing of PIN server responses back to Jade RPC

## [0.1.0] - 2026-01-08

### Added - Phase 1: Core Infrastructure

- **Transport Layer**
  - `IJadeTransport` interface for device communication
  - `SerialTransport` - USB serial port implementation
  - `GetAvailablePorts()` to list available serial ports
  - Automatic read/write buffering with configurable timeouts
  - `Drain()` method to clear pending data

- **RPC Protocol**
  - `JadeRpc` - Low-level RPC communication layer
  - `RpcRequest` / `RpcResponse` models
  - Request ID generation and response matching
  - Timeout handling with cancellation token support

- **CBOR Serialization**
  - `CborSerializer` - Full CBOR encoding/decoding
  - Support for all Jade data types (bool, int, string, bytes, arrays, maps)
  - BIP32 path serialization (uint arrays with hardened flags)
  - Response deserialization with error handling

- **Device Operations**
  - `GetVersionInfoAsync()` - Get firmware version, board type, state
  - `AddEntropyAsync()` - Add entropy to device RNG
  - `ConnectAsync()` / `DisconnectAsync()` - Connection management

- **Models**
  - `VersionInfo` - Device information (version, board, state, features)
  - `JadeState` enum (Uninit, Locked, Ready, Temp)

- **Exception Handling**
  - `JadeException` - Base exception class
  - `JadeConnectionException` - Transport/connection errors
  - `JadeRpcException` - RPC errors with error codes
  - Helper properties: `IsDeviceLocked`, `IsUserCancelled`

- **Testing**
  - 41 unit tests for CBOR serialization and RPC layer
  - Mock transport for protocol testing
  - Real device integration verified

### Dependencies

- PeterO.Cbor 4.5.2
- System.IO.Ports 8.0.0
- .NET 8.0

## Roadmap

### Phase 2B: Local PIN Server (Planned)

- `LocalPinServerHandler` - In-process Blind Oracle
- secp256k1 ECDH key exchange
- BIP341 key tweaking for protocol v2
- AES-256-CBC encryption for PIN records
- File-based PIN record storage
- Server key generation and management

### Phase 3: Wallet Operations (Planned)

- XPub derivation (`get_xpub`)
- PSBT signing (`sign_psbt`)
- Legacy transaction signing (`sign_tx`)
- Message signing (`sign_message`)
- Multisig wallet registration
- Descriptor wallet support

### Phase 4: Advanced Features (Planned)

- Bluetooth LE transport
- OTP (TOTP/HOTP) functionality
- Firmware updates
- High-level `JadeClient` wrapper class
