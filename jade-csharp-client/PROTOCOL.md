# Jade RPC Protocol Specification

This document describes the CBOR-RPC protocol used to communicate with Jade hardware wallet devices.

## Message Format

All messages are encoded using [CBOR (Concise Binary Object Representation)](https://www.rfc-editor.org/rfc/rfc8949.html).

### Request Format

```
{
    "id": <string>,        // Unique request identifier (required)
    "method": <string>,    // RPC method name (required)
    "params": <map>        // Method parameters (optional, method-specific)
}
```

### Response Format - Success

```
{
    "id": <string>,        // Echoes request id
    "result": <any>        // Method-specific result
}
```

### Response Format - Error

```
{
    "id": <string>,        // Echoes request id
    "error": {
        "code": <integer>,     // Error code
        "message": <string>,   // Human-readable message
        "data": <any>          // Additional error data (optional)
    }
}
```

## Error Codes

| Code | Name | Description |
|------|------|-------------|
| -32600 | INVALID_REQUEST | Malformed request |
| -32601 | UNKNOWN_METHOD | Method not found |
| -32602 | BAD_PARAMETERS | Invalid parameters |
| -32603 | INTERNAL_ERROR | Internal device error |
| -32000 | USER_CANCELLED | User cancelled operation on device |
| -32001 | PROTOCOL_ERROR | Protocol violation |
| -32002 | HW_LOCKED | Device locked or uninitialized |
| -32003 | NETWORK_MISMATCH | Network mismatch (e.g., mainnet vs testnet) |

## Transport Layer

### Serial (USB)

- Baud rate: 115200
- Data bits: 8
- Stop bits: 1
- Parity: None
- Flow control: None
- CBOR messages are sent/received as raw bytes

### Bluetooth LE

- Service UUID: `6e400001-b5a3-f393-e0a9-e50e24dcca9e` (Nordic UART)
- TX Characteristic: `6e400002-b5a3-f393-e0a9-e50e24dcca9e`
- RX Characteristic: `6e400003-b5a3-f393-e0a9-e50e24dcca9e`
- MTU: 517 bytes preferred

## RPC Methods

### Device Information

#### get_version_info

Get device version and status information.

**Request:**
```
{
    "id": "1",
    "method": "get_version_info"
}
```

**Response:**
```
{
    "id": "1",
    "result": {
        "JADE_VERSION": "1.0.38",
        "JADE_OTA_MAX_CHUNK": 4096,
        "JADE_CONFIG": "BLE",
        "BOARD_TYPE": "JADE",
        "JADE_FEATURES": "SB",
        "EFUSEMAC": "2CBCBB972FE4",
        "JADE_STATE": "READY",         // or "LOCKED", "TEMP", "UNINIT"
        "JADE_NETWORKS": "ALL",        // or "MAIN", "TEST"
        "JADE_HAS_PIN": true
    }
}
```

#### add_entropy

Add external entropy to the device RNG.

**Request:**
```
{
    "id": "2",
    "method": "add_entropy",
    "params": {
        "entropy": <bytes>    // Random bytes to add
    }
}
```

**Response:**
```
{
    "id": "2",
    "result": true
}
```

### Authentication

#### auth_user

Authenticate the user with PIN. This initiates a multi-step process:
1. Jade returns an `http_request` for the pinserver
2. Host makes HTTP request to pinserver
3. Host sends response back to Jade via `pin` method
4. Jade verifies and unlocks

**Request:**
```
{
    "id": "3",
    "method": "auth_user",
    "params": {
        "network": "mainnet",    // "mainnet", "testnet", "liquid", etc.
        "epoch": 1704672000       // Current unix timestamp
    }
}
```

**Response (intermediate - http_request):**
```
{
    "id": "3",
    "result": {
        "http_request": {
            "params": {
                "urls": ["https://j8d.io/get_pin", "http://xxx.onion/get_pin"],
                "method": "POST",
                "accept": "json",
                "data": "<base64-encoded-payload>"
            },
            "on-reply": "pin"
        }
    }
}
```

**Host action:** Make HTTP POST to URL, then send response:

```
{
    "id": "4",
    "method": "pin",
    "params": {
        "data": "<base64-encoded-pinserver-response>"
    }
}
```

**Final Response:**
```
{
    "id": "4",
    "result": true    // or false if PIN incorrect
}
```

#### logout

Log out the current session.

**Request:**
```
{
    "id": "5",
    "method": "logout"
}
```

### Key Derivation

#### get_xpub

Get an extended public key for a derivation path.

**Request:**
```
{
    "id": "10",
    "method": "get_xpub",
    "params": {
        "network": "mainnet",
        "path": [2147483732, 2147483648, 2147483648]   // m/84'/0'/0' with hardened flags
    }
}
```

**Note:** Path elements use BIP32 convention: add 0x80000000 (2147483648) for hardened derivation.

**Response:**
```
{
    "id": "10",
    "result": "xpub6D4BDPcP2GT577..."
}
```

#### get_receive_address

Get a receive address for display/verification.

**Request:**
```
{
    "id": "11",
    "method": "get_receive_address",
    "params": {
        "network": "mainnet",
        "path": [2147483732, 2147483648, 2147483648, 0, 0],  // m/84'/0'/0'/0/0
        "variant": "pkh(k)"    // Address type: "pkh(k)", "wpkh(k)", "sh(wpkh(k))"
    }
}
```

**Response:**
```
{
    "id": "11",
    "result": "bc1q..."
}
```

### Transaction Signing

#### sign_psbt

Sign a PSBT (Partially Signed Bitcoin Transaction).

**Request:**
```
{
    "id": "20",
    "method": "sign_psbt",
    "params": {
        "network": "mainnet",
        "psbt": <bytes>       // Raw PSBT bytes
    }
}
```

**Response:**
```
{
    "id": "20",
    "result": <bytes>         // Signed PSBT bytes
}
```

#### sign_tx (Legacy)

Sign a legacy transaction (non-PSBT).

**Request:**
```
{
    "id": "21",
    "method": "sign_tx",
    "params": {
        "network": "mainnet",
        "txn": <bytes>,                    // Raw transaction bytes
        "num_inputs": 2,
        "trusted_commitments": [],
        "use_ae_signatures": false,
        "change": [                        // Optional change output info
            {"path": [...], "variant": "wpkh(k)"}
        ]
    }
}
```

Then for each input, Jade will request input data via separate messages.

### Message Signing

#### sign_message

Sign a message with a specific key path.

**Request:**
```
{
    "id": "30",
    "method": "sign_message",
    "params": {
        "path": [2147483732, 2147483648, 2147483648, 0, 0],
        "message": "Hello, Bitcoin!",
        "ae_host_commitment": <bytes>      // Optional: for anti-exfil
    }
}
```

**Response:**
```
{
    "id": "30",
    "result": {
        "signature": "<base64-signature>"
    }
}
```

### Pinserver Configuration

#### update_pinserver

Update custom pinserver details.

**Request (set custom):**
```
{
    "id": "40",
    "method": "update_pinserver",
    "params": {
        "urlA": "https://my-pinserver.com",
        "urlB": "http://my-onion.onion",    // Optional
        "pubkey": <33-byte-ec-pubkey>,
        "certificate": "-----BEGIN..."       // Optional TLS cert
    }
}
```

**Request (reset to default):**
```
{
    "id": "41",
    "method": "update_pinserver",
    "params": {
        "reset_details": true,
        "reset_certificate": true
    }
}
```

### Multisig & Descriptors

#### register_multisig

Register a multisig wallet configuration.

**Request:**
```
{
    "id": "50",
    "method": "register_multisig",
    "params": {
        "network": "mainnet",
        "multisig_name": "family-vault",
        "descriptor": {
            "variant": "wsh(sortedmulti(k))",
            "threshold": 2,
            "signers": [
                {"fingerprint": <4-bytes>, "derivation": [...], "xpub": "xpub..."},
                {"fingerprint": <4-bytes>, "derivation": [...], "xpub": "xpub..."},
                {"fingerprint": <4-bytes>, "derivation": [...], "xpub": "xpub..."}
            ]
        }
    }
}
```

#### get_registered_multisigs

List registered multisig wallets.

**Request:**
```
{
    "id": "51",
    "method": "get_registered_multisigs"
}
```

**Response:**
```
{
    "id": "51",
    "result": ["family-vault", "business-wallet"]
}
```

## HTTP Request Handling

When Jade needs to communicate with an external server (e.g., pinserver), it returns an `http_request` in the result. The host application must:

1. Parse the `http_request` structure
2. Make the HTTP request to the specified URL(s)
3. Send the response back using the method specified in `on-reply`

```csharp
// Pseudocode for handling http_request
if (result.ContainsKey("http_request"))
{
    var httpReq = result["http_request"]["params"];
    var urls = httpReq["urls"];
    var method = httpReq["method"];  // "GET" or "POST"
    var data = httpReq["data"];

    // Make HTTP request
    var response = await HttpClient.PostAsync(urls[0], data);
    var body = await response.Content.ReadAsStringAsync();

    // Send response back to Jade
    var onReply = result["http_request"]["on-reply"];  // e.g., "pin"
    await jade.SendAsync(onReply, new { data = body });
}
```

## Implementation Notes

### Message Framing

CBOR messages are self-delimiting, but the transport may deliver partial data. Implementations should:

1. Buffer incoming bytes
2. Attempt to parse CBOR from buffer
3. If parsing fails with "unexpected end of input", wait for more data
4. If parsing succeeds, process message and remove from buffer

### Timeouts

- Normal operations: 30 seconds
- Operations requiring user interaction (PIN entry, tx approval): No timeout / indefinite

### Thread Safety

The protocol is strictly request-response. Only one request should be in-flight at a time.

### BIP32 Path Encoding

Paths are encoded as arrays of uint32 values:
- Normal derivation: raw index (e.g., `0`, `1`, `5`)
- Hardened derivation: index + 0x80000000 (e.g., `84'` = `2147483732`)

```csharp
// Convert "m/84'/0'/0'" to path array
uint[] path = new uint[] {
    84 | 0x80000000,   // 84' (hardened)
    0 | 0x80000000,    // 0'  (hardened)
    0 | 0x80000000     // 0'  (hardened)
};
```
