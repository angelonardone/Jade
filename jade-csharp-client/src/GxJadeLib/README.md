# GxJadeLib - GeneXus External Object for Jade Hardware Wallet

GxJadeLib is a GeneXus External Object wrapper for the Blockstream Jade hardware wallet. It provides synchronous static methods that wrap the async JadeClient API, making it easy to integrate Jade device functionality into GeneXus applications.

## Features

- **Connection Management** - Connect to Jade devices via USB serial port
- **Device Information** - Query firmware version, status, and capabilities
- **Authentication** - PIN-based authentication via remote PIN server
- **Key Derivation** - BIP32/BIP44/BIP84/BIP86 extended public keys and addresses
- **HSM Operations** - Hardware Security Module mode for signing, encryption, and key management

## Installation

### Prerequisites

- .NET 8.0 Runtime
- GeneXus 18 or later (with .NET generator)

### Building the Library

> **Important:** The `System.IO.Ports` package contains platform-specific native code for serial port communication. You must build for your target platform.

#### Building for Windows (most common for GeneXus)

```bash
cd jade-csharp-client
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r win-x64 --self-contained false -o ./publish-win
```

Output DLLs will be in `./publish-win/`:
- `GxJadeLib.dll`
- `JadeClient.dll`
- `PeterO.Cbor.dll`
- `PeterO.Numbers.dll`
- `System.IO.Ports.dll` (Windows native version)

#### Building for Linux

```bash
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r linux-x64 --self-contained false -o ./publish-linux
```

#### Building for macOS

```bash
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r osx-x64 --self-contained false -o ./publish-mac
```

#### Cross-Platform Considerations

| Build Platform | Target Platform | Command |
|---------------|-----------------|---------|
| Windows | Windows | `dotnet publish -c Release -o ./publish` |
| Windows | Linux | `dotnet publish -c Release -r linux-x64 -o ./publish-linux` |
| Linux/WSL | Windows | `dotnet publish -c Release -r win-x64 -o ./publish-win` |
| Linux/WSL | Linux | `dotnet publish -c Release -o ./publish` |
| macOS | macOS | `dotnet publish -c Release -o ./publish` |
| macOS | Windows | `dotnet publish -c Release -r win-x64 -o ./publish-win` |

> **Note:** If you build on Linux (including WSL) without specifying `-r win-x64`, the `System.IO.Ports.dll` will contain Linux native code and will fail on Windows with the error: "System.IO.Ports is currently only supported on Windows."

#### Building for Multiple Platforms at Once

To create builds for all platforms in a single command:

```bash
# Windows
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r win-x64 --self-contained false -o ./publish-win

# Linux
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r linux-x64 --self-contained false -o ./publish-linux

# macOS
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r osx-x64 --self-contained false -o ./publish-mac
```

### Required DLLs

Copy **all** these DLLs from the publish folder to your GeneXus externals directory:

| DLL | Description |
|-----|-------------|
| `GxJadeLib.dll` | GeneXus wrapper (this library) |
| `JadeClient.dll` | Core Jade client library |
| `PeterO.Cbor.dll` | CBOR serialization |
| `PeterO.Numbers.dll` | Dependency of PeterO.Cbor |
| `System.IO.Ports.dll` | Serial port communication (platform-specific) |

### Windows-Specific Notes

**Connection Delay:** On Windows with CH9102 USB-serial chips (common in Jade devices), the `Connect()` method takes approximately 3-4 seconds. This is because opening the serial port triggers a device reset, and the library waits for the Jade to fully boot before sending commands.

**COM Port Selection:** Jade devices may appear as two COM ports on Windows. Use Device Manager to identify the correct port by unplugging/replugging the device and noting which port appears/disappears.

### GeneXus Setup

1. **Copy the DLLs** to your GeneXus environment:
   - `GxJadeLib.dll`
   - `JadeClient.dll`
   - `PeterO.Cbor.dll`
   - `System.IO.Ports.dll` (if not already present)

2. **Create External Object** in GeneXus:
   - Name: `GxJadeWrapper`
   - Assembly: `GxJadeLib`
   - Class: `GxJadeLib.GxJadeWrapper`

3. **Create SDT definitions** for the result types (see Data Types section below)

---

## Data Types

### JadeOperationResult

Standard return type for all operations.

| Property | Type | Description |
|----------|------|-------------|
| Success | Boolean | Whether the operation succeeded |
| ErrorMessage | VarChar(500) | Error description if failed |
| ConnectionId | GUID | Connection identifier |
| ResponseMessage | VarChar(2000) | Optional response data |
| ErrorCode | Numeric(4) | RPC error code (-1 if not applicable) |

### GxVersionInfo

Device version and status information.

| Property | Type | Description |
|----------|------|-------------|
| JadeVersion | VarChar(20) | Firmware version (e.g., "1.0.38") |
| OtaMaxChunk | Numeric(8) | Max OTA update chunk size |
| Config | VarChar(20) | Configuration ("BLE", "NORADIO") |
| BoardType | VarChar(30) | Board type ("JADE", "JADE_V1_1") |
| Features | VarChar(20) | Feature flags ("SB" = Secure Boot) |
| EfuseMac | VarChar(20) | Device MAC address |
| State | VarChar(10) | State ("Uninit", "Locked", "Ready", "Temp") |
| Networks | VarChar(20) | Supported networks ("ALL", "MAIN", "TEST") |
| HasPin | Boolean | Device has PIN configured |
| HasWallet | Boolean | Device has wallet initialized |
| IsUnlocked | Boolean | Device is currently unlocked |

### GxXpubResult

Extended public key result.

| Property | Type | Description |
|----------|------|-------------|
| Xpub | VarChar(120) | Base58-encoded extended public key |
| Path | VarChar(50) | Derivation path (e.g., "m/84'/0'/0'") |

### GxAddressResult

Receive address result.

| Property | Type | Description |
|----------|------|-------------|
| Address | VarChar(100) | Bitcoin address |
| Path | VarChar(50) | Full derivation path |
| Variant | VarChar(20) | Address type variant |

### GxHsmInfo

HSM mode status information.

| Property | Type | Description |
|----------|------|-------------|
| Active | Boolean | HSM mode is active |
| Networks | VarChar(50) | Comma-separated networks |
| MainnetRootPath | VarChar(50) | Mainnet root derivation path |
| TestnetRootPath | VarChar(50) | Testnet root derivation path |
| MainnetRootPubkey | VarChar(70) | Mainnet root pubkey (hex) |
| TestnetRootPubkey | VarChar(70) | Testnet root pubkey (hex) |
| OperationsCount | Numeric(18) | Total operations performed |
| AutoLockTimeout | Numeric(8) | Auto-lock timeout in seconds |
| AutoLockRemaining | Numeric(8) | Time until auto-lock |

### GxHsmPubkeyResult

HSM public key result.

| Property | Type | Description |
|----------|------|-------------|
| Pubkey | VarChar(70) | Public key (hex, 33 bytes) |
| Path | VarChar(50) | Full derivation path |

### GxHsmXpubResult

HSM extended public key result.

| Property | Type | Description |
|----------|------|-------------|
| Xpub | VarChar(120) | Base58-encoded xpub |
| Path | VarChar(50) | Derivation path |

### GxHsmSignResult

HSM signature result.

| Property | Type | Description |
|----------|------|-------------|
| Signature | VarChar(150) | Signature (hex) |
| Pubkey | VarChar(70) | Signing pubkey (hex) |
| Algorithm | VarChar(10) | "schnorr" or "ecdsa" |

### GxHsmEncryptResult

HSM encryption result.

| Property | Type | Description |
|----------|------|-------------|
| Ciphertext | VarChar(2100) | Encrypted data (hex) |
| Nonce | VarChar(30) | 12-byte nonce (hex) |
| Tag | VarChar(35) | 16-byte auth tag (hex) |
| EphemeralPubkey | VarChar(70) | Ephemeral pubkey (hex) |

---

## API Reference

### Connection Management

#### Connect

Connect to a Jade device on a specific serial port.

```genexus
&Result = GxJadeWrapper.Connect(&PortName)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| PortName | VarChar(50) | Serial port (e.g., "COM3", "/dev/ttyACM0") |

**Returns:** `JadeOperationResult` with `ConnectionId` on success.

---

#### ConnectAuto

Auto-detect and connect to the first available Jade device.

```genexus
&Result = GxJadeWrapper.ConnectAuto()
```

**Returns:** `JadeOperationResult` with `ConnectionId` on success.

---

#### Disconnect

Disconnect from a Jade device.

```genexus
&Result = GxJadeWrapper.Disconnect(&ConnectionId)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Connection to disconnect |

---

#### IsConnected

Check if a connection is still active.

```genexus
&Result = GxJadeWrapper.IsConnected(&ConnectionId, &IsConnected)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Connection to check |
| IsConnected | Boolean | Output: connection status |

---

#### ListPorts

List available serial ports that may be Jade devices.

```genexus
&Result = GxJadeWrapper.ListPorts(&Ports)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| Ports | VarChar(500) | Output: comma-separated port names |

---

#### Drain

Clear pending data from the connection buffer.

```genexus
&Result = GxJadeWrapper.Drain(&ConnectionId)
```

---

### Device Information

#### GetVersionInfo

Get device version and status information.

```genexus
&Result = GxJadeWrapper.GetVersionInfo(&ConnectionId, &VersionInfo)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| VersionInfo | GxVersionInfo | Output: device information |

---

### Authentication

#### AddEntropy

Add entropy to the device's random number generator.

```genexus
&Result = GxJadeWrapper.AddEntropy(&ConnectionId, &EntropyHex)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| EntropyHex | VarChar(256) | Random bytes as hex string |

---

#### AuthUser

Authenticate user with PIN via the default Blockstream PIN server.
The user will need to enter their PIN on the Jade device.

```genexus
&Result = GxJadeWrapper.AuthUser(&ConnectionId, &Network)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |

**Note:** This operation has a 5-minute timeout to allow for PIN entry.

---

#### AuthUserWithServer

Authenticate using a custom PIN server.

```genexus
&Result = GxJadeWrapper.AuthUserWithServer(&ConnectionId, &Network, &PinServerUrl)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| PinServerUrl | VarChar(200) | Custom PIN server URL |

---

#### Logout

Lock the device / end the session.

```genexus
&Result = GxJadeWrapper.Logout(&ConnectionId)
```

---

### Key Derivation & Addresses

#### GetXpub

Get an extended public key for a derivation path.

```genexus
&Result = GxJadeWrapper.GetXpub(&ConnectionId, &Network, &Path, &XpubResult)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Path | VarChar(50) | BIP32 path (e.g., "m/84'/0'/0'") |
| XpubResult | GxXpubResult | Output: extended public key |

**Path Formats:**
- With prefix: `"m/84'/0'/0'"`
- Without prefix: `"84'/0'/0'"`
- Hardened notation: `'` or `h` or `H`

---

#### GetReceiveAddress

Get a receive address for verification on the device.

```genexus
&Result = GxJadeWrapper.GetReceiveAddress(&ConnectionId, &Network, &Path, &Variant, &AddressResult)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Path | VarChar(50) | Full path with address index |
| Variant | VarChar(20) | Address type (see below) |
| AddressResult | GxAddressResult | Output: generated address |

**Address Variants:**

| Variant | Description | Example Path |
|---------|-------------|--------------|
| `pkh(k)` | Legacy P2PKH (BIP44) | m/44'/0'/0'/0/0 |
| `sh(wpkh(k))` | Nested SegWit (BIP49) | m/49'/0'/0'/0/0 |
| `wpkh(k)` | Native SegWit (BIP84) | m/84'/0'/0'/0/0 |
| `tr(k)` | Taproot (BIP86) | m/86'/0'/0'/0/0 |

---

### PIN Server Configuration

#### UpdatePinServer

Configure a custom PIN server on the device.

```genexus
&Result = GxJadeWrapper.UpdatePinServer(&ConnectionId, &UrlA, &UrlB, &PubkeyHex)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| UrlA | VarChar(200) | Primary server URL |
| UrlB | VarChar(200) | Secondary URL (Tor) or empty |
| PubkeyHex | VarChar(70) | Server pubkey (hex, 33 bytes) |

---

#### ResetPinServer

Reset PIN server configuration to Blockstream defaults.

```genexus
&Result = GxJadeWrapper.ResetPinServer(&ConnectionId)
```

---

### HSM Operations

HSM (Hardware Security Module) mode provides secure key management and cryptographic operations without exposing the seed phrase.

#### HsmGetInfo

Get HSM mode status and information.

```genexus
&Result = GxJadeWrapper.HsmGetInfo(&ConnectionId, &HsmInfo)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| HsmInfo | GxHsmInfo | Output: HSM status |

---

#### HsmGetPubkey

Get a public key from HSM at a specific index.

```genexus
&Result = GxJadeWrapper.HsmGetPubkey(&ConnectionId, &Network, &Index, &PubkeyResult)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Index | Numeric(8) | Key index (0 to 2^31-1) |
| PubkeyResult | GxHsmPubkeyResult | Output: public key |

---

#### HsmGetXpub

Get the HSM root extended public key.

```genexus
&Result = GxJadeWrapper.HsmGetXpub(&ConnectionId, &Network, &XpubResult)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| XpubResult | GxHsmXpubResult | Output: extended public key |

---

#### HsmSign

Sign a 32-byte hash using an HSM key.

```genexus
&Result = GxJadeWrapper.HsmSign(&ConnectionId, &Network, &Index, &HashHex, &Algorithm, &SignResult)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Index | Numeric(8) | Key index |
| HashHex | VarChar(70) | 32-byte hash (hex) |
| Algorithm | VarChar(10) | "schnorr" or "ecdsa" |
| SignResult | GxHsmSignResult | Output: signature |

---

#### HsmEcdh

Compute an ECDH shared secret.

```genexus
&Result = GxJadeWrapper.HsmEcdh(&ConnectionId, &Network, &Index, &TheirPubkeyHex, &SecretHex)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Index | Numeric(8) | Key index |
| TheirPubkeyHex | VarChar(130) | Other party's pubkey (hex) |
| SecretHex | VarChar(70) | Output: 32-byte shared secret (hex) |

---

#### HsmEncrypt

Encrypt data using ECIES (Elliptic Curve Integrated Encryption Scheme).

```genexus
&Result = GxJadeWrapper.HsmEncrypt(&ConnectionId, &Network, &Index, &PlaintextHex, &TheirPubkeyHex, &AadHex, &EncryptResult)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Index | Numeric(8) | Key index |
| PlaintextHex | VarChar(2100) | Data to encrypt (hex, max 1024 bytes) |
| TheirPubkeyHex | VarChar(130) | Recipient pubkey (hex) or empty for self |
| AadHex | VarChar(500) | Additional authenticated data (hex) or empty |
| EncryptResult | GxHsmEncryptResult | Output: encryption components |

---

#### HsmDecrypt

Decrypt ECIES-encrypted data.

```genexus
&Result = GxJadeWrapper.HsmDecrypt(&ConnectionId, &Network, &Index, &CiphertextHex, &NonceHex, &TagHex, &EphemeralPubkeyHex, &AadHex, &PlaintextHex)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| ConnectionId | GUID | Active connection |
| Network | VarChar(20) | "mainnet" or "testnet" |
| Index | Numeric(8) | Key index |
| CiphertextHex | VarChar(2100) | Encrypted data (hex) |
| NonceHex | VarChar(30) | 12-byte nonce (hex) |
| TagHex | VarChar(35) | 16-byte auth tag (hex) |
| EphemeralPubkeyHex | VarChar(70) | Ephemeral pubkey (hex, 33 bytes) |
| AadHex | VarChar(500) | Additional authenticated data (hex) or empty |
| PlaintextHex | VarChar(2100) | Output: decrypted data (hex) |

---

#### HsmLock

Lock/deactivate HSM mode.

```genexus
&Result = GxJadeWrapper.HsmLock(&ConnectionId)
```

---

## Usage Examples

### Basic Connection and Device Info

```genexus
// Auto-connect to Jade
&Result = GxJadeWrapper.ConnectAuto()
if &Result.Success
    &ConnectionId = &Result.ConnectionId

    // Get device info
    &Result = GxJadeWrapper.GetVersionInfo(&ConnectionId, &VersionInfo)
    if &Result.Success
        msg("Jade Version: " + &VersionInfo.JadeVersion)
        msg("State: " + &VersionInfo.State)
    endif

    // Disconnect
    GxJadeWrapper.Disconnect(&ConnectionId)
else
    msg("Connection failed: " + &Result.ErrorMessage)
endif
```

### Authentication and Wallet Operations

```genexus
// Connect
&Result = GxJadeWrapper.ConnectAuto()
if &Result.Success
    &ConnectionId = &Result.ConnectionId

    // Authenticate (user enters PIN on device)
    &Result = GxJadeWrapper.AuthUser(&ConnectionId, "mainnet")
    if &Result.Success

        // Get account xpub (BIP84 - Native SegWit)
        &Result = GxJadeWrapper.GetXpub(&ConnectionId, "mainnet", "m/84'/0'/0'", &XpubResult)
        if &Result.Success
            msg("Account Xpub: " + &XpubResult.Xpub)
        endif

        // Get first receiving address
        &Result = GxJadeWrapper.GetReceiveAddress(&ConnectionId, "mainnet", "m/84'/0'/0'/0/0", "wpkh(k)", &AddressResult)
        if &Result.Success
            msg("Address: " + &AddressResult.Address)
        endif

        // Logout (lock device)
        GxJadeWrapper.Logout(&ConnectionId)
    else
        msg("Auth failed: " + &Result.ErrorMessage)
    endif

    GxJadeWrapper.Disconnect(&ConnectionId)
endif
```

### HSM Operations

```genexus
// Connect and authenticate
&Result = GxJadeWrapper.ConnectAuto()
&ConnectionId = &Result.ConnectionId
GxJadeWrapper.AuthUser(&ConnectionId, "mainnet")

// Check HSM status
&Result = GxJadeWrapper.HsmGetInfo(&ConnectionId, &HsmInfo)
if &HsmInfo.Active
    msg("HSM is active on: " + &HsmInfo.Networks)
    msg("Operations count: " + &HsmInfo.OperationsCount.ToString())

    // Get HSM public key at index 0
    &Result = GxJadeWrapper.HsmGetPubkey(&ConnectionId, "mainnet", 0, &PubkeyResult)
    msg("Pubkey: " + &PubkeyResult.Pubkey)

    // Sign a hash (example: SHA256 of "Hello")
    &HashHex = "185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969"
    &Result = GxJadeWrapper.HsmSign(&ConnectionId, "mainnet", 0, &HashHex, "schnorr", &SignResult)
    if &Result.Success
        msg("Signature: " + &SignResult.Signature)
        msg("Pubkey: " + &SignResult.Pubkey)
    endif
else
    msg("HSM mode is not active")
endif

GxJadeWrapper.Logout(&ConnectionId)
GxJadeWrapper.Disconnect(&ConnectionId)
```

### HSM Encryption/Decryption

```genexus
// Encrypt data to self
&PlaintextHex = "48656c6c6f20576f726c64"  // "Hello World" in hex
&Result = GxJadeWrapper.HsmEncrypt(&ConnectionId, "mainnet", 0, &PlaintextHex, "", "", &EncryptResult)

if &Result.Success
    msg("Ciphertext: " + &EncryptResult.Ciphertext)

    // Decrypt it back
    &Result = GxJadeWrapper.HsmDecrypt(
        &ConnectionId,
        "mainnet",
        0,
        &EncryptResult.Ciphertext,
        &EncryptResult.Nonce,
        &EncryptResult.Tag,
        &EncryptResult.EphemeralPubkey,
        "",
        &DecryptedHex)

    if &Result.Success
        msg("Decrypted: " + &DecryptedHex)  // Should be "48656c6c6f20576f726c64"
    endif
endif
```

---

## Error Handling

All methods return a `JadeOperationResult`. Always check `Success` before using results:

```genexus
&Result = GxJadeWrapper.SomeMethod(...)
if &Result.Success
    // Use the output parameters
else
    // Handle error
    msg("Error: " + &Result.ErrorMessage)
    if &Result.ErrorCode <> -1
        msg("Error Code: " + &Result.ErrorCode.ToString())
    endif
endif
```

### Common Error Codes

| Code | Description |
|------|-------------|
| -32600 | Invalid request |
| -32601 | Unknown method |
| -32602 | Bad parameters |
| -32603 | Internal error |
| -32000 | User cancelled (on device) |
| -32001 | Protocol error |
| -32002 | Device locked |
| -32003 | Network mismatch |

---

## Hex String Conversion

GxJadeLib uses hexadecimal strings for all binary data (GeneXus cannot handle raw byte arrays).

**Converting text to hex:**
```genexus
// "Hello" = 48 65 6c 6c 6f
&Hex = "48656c6c6f"
```

**Converting hex to text (in your application):**
```genexus
// Parse hex pairs and convert to characters
```

---

## Thread Safety

- `GxJadeWrapper` uses internal locking for thread-safe connection management
- Each connection has its own `ConnectionId` - do not share between threads
- Call `DisconnectAll()` on application shutdown to clean up

---

## Troubleshooting

### "System.IO.Ports is currently only supported on Windows"

**Cause:** You built the library on Linux/WSL without specifying the Windows runtime identifier. The `System.IO.Ports.dll` contains Linux native code that cannot run on Windows.

**Solution:** Rebuild with the Windows runtime identifier:
```bash
dotnet publish src/GxJadeLib/GxJadeLib.csproj -c Release -r win-x64 --self-contained false -o ./publish-win
```

Then copy all DLLs from `./publish-win/` to your GeneXus externals folder.

### Connection takes 3-4 seconds on Windows

**Cause:** This is expected behavior. On Windows with CH9102 USB-serial chips (common in Jade devices), opening the serial port triggers a device reset. The library waits 3.5 seconds for the Jade to fully boot before sending commands.

**Note:** This delay only occurs on Windows. On Linux/macOS, the connection is nearly instant.

### Device resets when connecting on Windows

**Cause:** The CH9102 USB-serial chip triggers a device reset when the serial port is opened on Windows, regardless of DTR/RTS settings. This is a hardware behavior specific to how the chip is wired on ESP32-based devices like Jade.

**Solution:** This is handled automatically by the library - it waits for the device to boot after opening the port. No action needed.

### "Could not load file or assembly 'System.IO.Ports'"

**Cause:** The `System.IO.Ports.dll` is missing from your deployment.

**Solution:** Ensure you copy **all** DLLs from the publish folder, including:
- `System.IO.Ports.dll`
- `PeterO.Cbor.dll`
- `PeterO.Numbers.dll`

### "Could not locate the assembly 'GxJadeLib'"

**Cause:** GeneXus cannot find the GxJadeLib.dll assembly.

**Solution:**
1. Verify `GxJadeLib.dll` is in your GeneXus externals folder
2. Add `GxJadeLib.dll` to the External References in your GeneXus generator properties
3. Ensure the External Object definition points to the correct assembly name

### Device Not Found

1. Ensure Jade is connected via USB
2. Check that no other application is using the serial port
3. On Linux, ensure user has permission: `sudo usermod -a -G dialout $USER`
4. Use `ListPorts()` to see available ports
5. On Windows, check Device Manager for the correct COM port number

### Wrong COM Port on Windows

On Windows, Jade devices with CH9102 chips may create two COM ports. Only one responds to commands.

**To identify the correct port:**
1. Open Device Manager
2. Expand "Ports (COM & LPT)"
3. Unplug the Jade - note which port disappears
4. Plug it back in - that's your Jade port

### Authentication Timeout

- The user has 5 minutes to enter their PIN on the device
- Ensure the device screen is visible to the user
- Check network connectivity (PIN server communication)

### HSM Operations Fail

- Verify HSM mode is enabled on the device
- Check that the correct network is being used
- Ensure the device is unlocked (authenticated)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1.0 | 2025-01 | Initial release |
