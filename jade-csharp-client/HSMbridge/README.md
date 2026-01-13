# HSMbridge

HSMbridge is a REST API server that exposes Jade HSM functionality over HTTP. It connects to a Jade hardware device via USB and transforms REST API calls into USB RPC HSM commands.

## Architecture

```
┌─────────────────┐     REST API      ┌─────────────────┐     USB/Serial     ┌─────────────────┐
│   Client App    │ ◄───────────────► │   HSMbridge     │ ◄─────────────────► │   Jade Device   │
│  (any language) │    HTTP/JSON      │   (C# Server)   │     CBOR-RPC       │   (HSM Mode)    │
└─────────────────┘                   └─────────────────┘                    └─────────────────┘
```

## Features

- REST API exposing all HSM operations (signing, encryption, key derivation)
- Automatic Jade device detection on USB
- PIN authentication via remote PIN server
- HSM mode activation with configurable timeout
- Swagger UI for interactive API documentation
- Thread-safe request serialization for safe device communication
- Configurable via `appsettings.json`

## Requirements

- Jade hardware device with firmware supporting HSM mode
- USB connection to the Jade device
- For development: .NET 8.0 SDK

## Installation

### Option 1: Pre-built Executables (Recommended)

Download the pre-built executable for your platform from the [Releases](../../releases) page:

| Platform | File |
|----------|------|
| Windows (64-bit) | `HSMbridge-x.x.x-windows-x64.zip` |
| Windows ARM64 | `HSMbridge-x.x.x-windows-arm64.zip` |
| macOS Intel | `HSMbridge-x.x.x-macos-x64.zip` |
| macOS Apple Silicon | `HSMbridge-x.x.x-macos-arm64.zip` |
| Linux (64-bit) | `HSMbridge-x.x.x-linux-x64.zip` |
| Linux ARM64 | `HSMbridge-x.x.x-linux-arm64.zip` |

Extract and run:
```bash
# macOS/Linux
./HSMbridge

# Windows
HSMbridge.exe
```

No .NET installation required - the executable is self-contained.

### Option 2: Build from Source

Requires .NET 8.0 SDK.

```bash
cd jade-csharp-client/HSMbridge
dotnet build
dotnet run
```

### Building Release Executables

To create self-contained executables for all platforms:

```bash
./build-release.sh 1.0.0
```

This creates zip archives in `releases/` for Windows, macOS, and Linux.

## Quick Start

### 1. Configure (Optional)

Edit `appsettings.json` to customize settings:

```json
{
  "HSMbridge": {
    "Port": 5001,
    "SerialPort": null,
    "Network": "mainnet",
    "PinServer": {
      "Mode": "Remote",
      "Url": null
    },
    "EnableSwagger": true,
    "HsmActivationTimeoutSeconds": 120
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Port` | HTTP port for the REST API | 5000 |
| `SerialPort` | Serial port name (null = auto-detect) | null |
| `Network` | Network for authentication ("mainnet" or "testnet") | "mainnet" |
| `PinServer.Mode` | PIN server mode ("Remote" or "Local") | "Remote" |
| `PinServer.Url` | Custom PIN server URL (null = Blockstream default) | null |
| `EnableSwagger` | Enable Swagger UI at /swagger | true |
| `HsmActivationTimeoutSeconds` | Timeout waiting for HSM mode activation | 120 |

### 2. Run

```bash
dotnet run
```

### 3. Startup Flow

When HSMbridge starts, it will:

1. **Detect Jade device** - Automatically finds Jade on USB (or uses configured port)
2. **Authenticate** - If device is locked, prompts you to enter PIN on the Jade
3. **Wait for HSM mode** - Displays instructions to activate HSM mode on device:
   - Press button on Jade to open menu
   - Select "Session"
   - Select "HSM Mode"
   - Confirm activation
4. **Start REST API** - Once HSM is active, starts the HTTP server

### 4. Test

Open Swagger UI in your browser:
```
http://localhost:5001/swagger
```

Or run the test script:
```bash
./test-api.sh http://localhost:5001
```

## API Reference

All binary data (hashes, public keys, signatures, plaintext, ciphertext) is **hex-encoded** in JSON requests and responses.

### Health Check

```
GET /health
```

**Response:**
```json
{
  "healthy": true,
  "hsmActive": true,
  "deviceVersion": "1.0.38"
}
```

---

### Get HSM Info

```
GET /api/hsm/info
```

Returns HSM status including active state, supported networks, root public keys, and operation count.

**Response:**
```json
{
  "active": true,
  "networks": ["mainnet", "testnet"],
  "mainnetRootPath": "m/86'/0'/0'/6000'",
  "testnetRootPath": "m/86'/1'/0'/6000'",
  "mainnetRootPubkey": "02abc123...",
  "testnetRootPubkey": "03def456...",
  "operationsCount": 42,
  "autoLockTimeout": 0,
  "autoLockRemaining": null
}
```

---

### Get Public Key

```
GET /api/hsm/pubkey/{network}/{index}
```

**Parameters:**
- `network` - "mainnet" or "testnet"
- `index` - Key index (0 to 2^31-1)

**Response:**
```json
{
  "pubkey": "02abc123def456...",
  "path": "m/86'/0'/0'/6000'/0"
}
```

---

### Get Extended Public Key (XPub)

```
GET /api/hsm/xpub/{network}
```

**Parameters:**
- `network` - "mainnet" or "testnet"

**Response:**
```json
{
  "xpub": "xpub6ABC123...",
  "path": "m/86'/0'/0'/6000'"
}
```

---

### Sign Hash

```
POST /api/hsm/sign
```

Signs a 32-byte hash using Schnorr (BIP-340) or ECDSA algorithm.

**Request:**
```json
{
  "network": "mainnet",
  "index": 0,
  "hash": "0000000000000000000000000000000000000000000000000000000000000001",
  "algorithm": "schnorr"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `network` | string | Yes | "mainnet" or "testnet" |
| `index` | uint | Yes | Key index |
| `hash` | string | Yes | 32-byte hash (64 hex chars) |
| `algorithm` | string | No | "schnorr" (default) or "ecdsa" |

**Response:**
```json
{
  "signature": "abc123...",
  "pubkey": "02def456...",
  "algorithm": "schnorr"
}
```

**Signature lengths:**
- Schnorr: 64 bytes (128 hex chars)
- ECDSA: DER-encoded, up to 72 bytes

---

### ECDH Shared Secret

```
POST /api/hsm/ecdh
```

Computes an ECDH shared secret with another party's public key.

**Request:**
```json
{
  "network": "mainnet",
  "index": 0,
  "theirPubkey": "02abc123..."
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `network` | string | Yes | "mainnet" or "testnet" |
| `index` | uint | Yes | Key index |
| `theirPubkey` | string | Yes | 33-byte compressed or 65-byte uncompressed pubkey |

**Response:**
```json
{
  "sharedSecret": "abc123..."
}
```

The shared secret is 32 bytes (64 hex chars).

---

### ECIES Encrypt

```
POST /api/hsm/encrypt
```

Encrypts data using ECIES (Elliptic Curve Integrated Encryption Scheme) with AES-256-GCM.

**Request:**
```json
{
  "network": "mainnet",
  "index": 0,
  "plaintext": "48656c6c6f",
  "theirPubkey": null,
  "aad": null
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `network` | string | Yes | "mainnet" or "testnet" |
| `index` | uint | Yes | Key index for encryption |
| `plaintext` | string | Yes | Data to encrypt (max 1024 bytes, hex-encoded) |
| `theirPubkey` | string | No | Recipient's pubkey (null = encrypt to self) |
| `aad` | string | No | Additional authenticated data (hex-encoded) |

**Response:**
```json
{
  "ciphertext": "abc123...",
  "nonce": "def456...",
  "tag": "789abc...",
  "ephemeralPubkey": "02xyz..."
}
```

| Field | Size | Description |
|-------|------|-------------|
| `ciphertext` | varies | Encrypted data |
| `nonce` | 12 bytes | AES-GCM nonce |
| `tag` | 16 bytes | AES-GCM authentication tag |
| `ephemeralPubkey` | 33 bytes | Ephemeral public key for ECDH |

---

### ECIES Decrypt

```
POST /api/hsm/decrypt
```

Decrypts ECIES-encrypted data.

**Request:**
```json
{
  "network": "mainnet",
  "index": 0,
  "ciphertext": "abc123...",
  "nonce": "def456...",
  "tag": "789abc...",
  "ephemeralPubkey": "02xyz...",
  "aad": null
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `network` | string | Yes | "mainnet" or "testnet" |
| `index` | uint | Yes | Key index for decryption |
| `ciphertext` | string | Yes | Encrypted data (hex) |
| `nonce` | string | Yes | 12-byte nonce (hex) |
| `tag` | string | Yes | 16-byte auth tag (hex) |
| `ephemeralPubkey` | string | Yes | 33-byte ephemeral pubkey (hex) |
| `aad` | string | No | Additional authenticated data (must match encryption) |

**Response:**
```json
{
  "plaintext": "48656c6c6f"
}
```

---

### Lock HSM

```
POST /api/hsm/lock
```

Deactivates HSM mode on the device. After locking, the device returns to normal wallet mode.

**Response:**
```json
{
  "success": true
}
```

---

## Error Responses

All endpoints return errors in this format:

```json
{
  "error": "Error message",
  "details": "Additional details (optional)"
}
```

**HTTP Status Codes:**
- `200` - Success
- `400` - Bad request (invalid parameters)
- `500` - Internal server error (device communication failure)

---

## Example Usage

### curl

```bash
# Get HSM info
curl http://localhost:5001/api/hsm/info

# Get public key
curl http://localhost:5001/api/hsm/pubkey/mainnet/0

# Sign a hash
curl -X POST http://localhost:5001/api/hsm/sign \
  -H "Content-Type: application/json" \
  -d '{"network":"mainnet","index":0,"hash":"0000000000000000000000000000000000000000000000000000000000000001","algorithm":"schnorr"}'

# Encrypt data ("Hello" = 48656c6c6f in hex)
curl -X POST http://localhost:5001/api/hsm/encrypt \
  -H "Content-Type: application/json" \
  -d '{"network":"mainnet","index":0,"plaintext":"48656c6c6f"}'
```

### Python

```python
import requests

BASE_URL = "http://localhost:5001"

# Get HSM info
response = requests.get(f"{BASE_URL}/api/hsm/info")
print(response.json())

# Sign a hash
response = requests.post(f"{BASE_URL}/api/hsm/sign", json={
    "network": "mainnet",
    "index": 0,
    "hash": "00" * 32,
    "algorithm": "schnorr"
})
print(response.json())

# Encrypt
plaintext_hex = "Hello".encode().hex()
response = requests.post(f"{BASE_URL}/api/hsm/encrypt", json={
    "network": "mainnet",
    "index": 0,
    "plaintext": plaintext_hex
})
encrypted = response.json()
print(encrypted)

# Decrypt
response = requests.post(f"{BASE_URL}/api/hsm/decrypt", json={
    "network": "mainnet",
    "index": 0,
    "ciphertext": encrypted["ciphertext"],
    "nonce": encrypted["nonce"],
    "tag": encrypted["tag"],
    "ephemeralPubkey": encrypted["ephemeralPubkey"]
})
decrypted_hex = response.json()["plaintext"]
print(bytes.fromhex(decrypted_hex).decode())  # "Hello"
```

### JavaScript/Node.js

```javascript
const BASE_URL = "http://localhost:5001";

// Get HSM info
const info = await fetch(`${BASE_URL}/api/hsm/info`).then(r => r.json());
console.log(info);

// Sign a hash
const signResult = await fetch(`${BASE_URL}/api/hsm/sign`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    network: "mainnet",
    index: 0,
    hash: "00".repeat(32),
    algorithm: "schnorr"
  })
}).then(r => r.json());
console.log(signResult);
```

---

## Security Considerations

1. **No Authentication** - The REST API has no authentication. It is designed for trusted network/local use only. Do not expose to the internet.

2. **HTTP Only** - The API uses HTTP (not HTTPS). For production use over untrusted networks, place behind a reverse proxy with TLS.

3. **Seed Isolation** - When HSM mode is active, the wallet seed is cleared from memory. Only the HSM-specific keys are available.

4. **Physical Confirmation** - HSM mode must be activated manually on the device, providing physical security.

---

## Troubleshooting

### "No Jade device found"
- Ensure Jade is connected via USB
- Check that the device is powered on
- Try specifying the serial port in `appsettings.json`

### "Authentication failed"
- Make sure you entered the correct PIN on the device
- Check that the PIN server is reachable (requires internet for remote mode)

### "Timeout waiting for HSM mode activation"
- Activate HSM mode on the device: Menu → Session → HSM Mode
- Increase `HsmActivationTimeoutSeconds` in config if needed

### API returns errors after device disconnect
- Restart HSMbridge to reconnect to the device

---

## License

MIT License - See the main repository for details.
