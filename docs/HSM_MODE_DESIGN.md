# Jade HSM Mode - Design Document

## Overview

This document describes a proposed **HSM (Hardware Security Module) Mode** for Jade hardware wallets. HSM Mode provides isolated cryptographic operations (encrypt, decrypt, sign, ECDH) without exposing Bitcoin wallet keys.

### Goals

1. **Isolation** - HSM keys are derived from the master seed but the master seed and Bitcoin keys are never accessible in HSM mode
2. **Persistence** - Device stays unlocked while powered (no repeated PIN entry)
3. **Simplicity** - Index-based key derivation for unlimited key slots
4. **Security** - Power cycle required to switch modes or re-lock

### Non-Goals

- Full HSM certification (FIPS, Common Criteria)
- Multi-user/role-based access
- Audit logging (may be added later)
- High-throughput operations

---

## Architecture

### Key Hierarchy

```
                            BIP39 Seed
                                │
                                ▼
                      m/ (Master Key)
                                │
        ┌───────────────────────┼───────────────────────┐
        ▼                       ▼                       ▼
   m/44'/...              m/84'/...               m/86'/...
   (Legacy)               (SegWit)                (Taproot)
                                                       │
                                          ┌────────────┴────────────┐
                                          ▼                         ▼
                                    m/86'/0'/0'               m/86'/0'/0'
                                    (Bitcoin Wallet)          (Coin Type 0)
                                                                    │
                                                                    ▼
                                                            m/86'/0'/0'/8128'
                                                            (HSM Root Key)
                                                                    │
                            ┌───────────┬───────────┬───────────────┼────────────┐
                            ▼           ▼           ▼               ▼            ▼
                      .../8128'/0  .../8128'/1  .../8128'/2   .../8128'/n   ...
                      (Index 0)   (Index 1)    (Index 2)     (Index n)
```

### Path Specification

HSM mode supports **both mainnet and testnet simultaneously** using separate derivation paths:

```
Mainnet:  m / 86' / 0' / 0' / 8128' / index
Testnet:  m / 86' / 1' / 0' / 8128' / index
              │     │    │     │       │
              │     │    │     │       └── Index: 0 to 2^31-1 (non-hardened for flexibility)
              │     │    │     │
              │     │    │     └── HSM Branch: 8128' (0x80001FC0)
              │     │    │         ASCII "HSM" = 0x48534D = decimal 4804941
              │     │    │         Using 8128 (0x1FC0) as shorter alternative
              │     │    │
              │     │    └── Account: 0' (standard first account)
              │     │
              │     └── Coin Type: 0' (mainnet) or 1' (testnet)
              │
              └── Purpose: 86' (BIP-86 Taproot, enables Schnorr signatures)
```

**Why these choices:**
- **86' (Taproot)**: Enables Schnorr signatures which are simpler and more efficient
- **8128'**: Hardened branch ensures HSM keys cannot be derived from xpub
- **Non-hardened index**: Allows public key derivation for key management
- **Dual network support**: Both mainnet and testnet keys available simultaneously

### Memory Model

#### Normal Wallet Mode
```c
struct keychain_t {
    uint8_t seed[64];              // Full BIP39 seed
    size_t seed_len;
    uint8_t master_unblinding_key[64];  // For Liquid
    // ... full access to derive any path
};
```

#### HSM Mode
```c
struct hsm_keychain_t {
    // Mainnet keys: m/86'/0'/0'/8128'
    uint8_t hsm_mainnet_private_key[32];
    uint8_t hsm_mainnet_chain_code[32];
    uint8_t hsm_mainnet_public_key[33];

    // Testnet keys: m/86'/1'/0'/8128'
    uint8_t hsm_testnet_private_key[32];
    uint8_t hsm_testnet_chain_code[32];
    uint8_t hsm_testnet_public_key[33];

    bool is_active;                     // HSM mode flag
    uint32_t auto_lock_timeout;         // 0 = disabled, >0 = seconds
    uint32_t last_activity_timestamp;   // For auto-lock tracking
    uint64_t operations_count;          // Total operations performed
};
// NOTE: seed and master_key are WIPED after HSM key derivation
```

---

## User Interface

### Startup Screen

```
┌─────────────────────────────────┐
│                                 │
│            [Jade Logo]          │
│                                 │
│   ┌─────────────────────────┐   │
│   │    Unlock Wallet    [>] │   │
│   └─────────────────────────┘   │
│                                 │
│   ┌─────────────────────────┐   │
│   │    Unlock HSM       [>] │   │
│   └─────────────────────────┘   │
│                                 │
│   ┌─────────────────────────┐   │
│   │    Options          [>] │   │
│   └─────────────────────────┘   │
│                                 │
└─────────────────────────────────┘
```

### HSM Mode Active Screen

```
┌─────────────────────────────────┐
│  HSM MODE                       │
│  ─────────────────────────────  │
│                                 │
│  Status: Active                 │
│  Networks: Mainnet + Testnet    │
│  Operations: 0                  │
│  Auto-lock: Disabled            │
│                                 │
│  Mainnet: m/86'/0'/0'/8128'/*   │
│  Testnet: m/86'/1'/0'/8128'/*   │
│                                 │
│  [Lock]              [Settings] │
└─────────────────────────────────┘
```

### HSM Settings Screen (Device UI Only)

The auto-lock timeout can **only** be configured through the device UI, not via RPC.
This prevents malicious applications from disabling the timeout remotely.

```
┌─────────────────────────────────┐
│  HSM SETTINGS                   │
│  ─────────────────────────────  │
│                                 │
│  Auto-lock timeout:             │
│  ┌─────────────────────────┐    │
│  │ Disabled            [>] │    │
│  └─────────────────────────┘    │
│                                 │
│  Options:                       │
│   - Disabled (default)          │
│   - 5 minutes                   │
│   - 15 minutes                  │
│   - 30 minutes                  │
│   - 1 hour                      │
│                                 │
│  [Back]                         │
└─────────────────────────────────┘
```

**Security Note:** Auto-lock timeout is intentionally not exposed via RPC to prevent
a compromised host from keeping the device unlocked indefinitely.

---

## RPC Protocol

### New RPC Methods

#### `hsm_get_info`

Get HSM mode status and configuration.

**Request:**
```json
{
    "id": "1",
    "method": "hsm_get_info",
    "params": {}
}
```

**Response:**
```json
{
    "id": "1",
    "result": {
        "active": true,
        "networks": ["mainnet", "testnet"],
        "mainnet_root_path": "m/86'/0'/0'/8128'",
        "mainnet_root_pubkey": "02a1b2c3...",
        "testnet_root_path": "m/86'/1'/0'/8128'",
        "testnet_root_pubkey": "03d4e5f6...",
        "operations_count": 42,
        "auto_lock_timeout": 0,
        "auto_lock_remaining": null
    }
}
```

---

#### `hsm_get_pubkey`

Get public key for a specific index.

**Request:**
```json
{
    "id": "2",
    "method": "hsm_get_pubkey",
    "params": {
        "network": "mainnet",
        "index": 0
    }
}
```

**Response:**
```json
{
    "id": "2",
    "result": {
        "pubkey": "02abc123...",
        "path": "m/86'/0'/0'/8128'/0"
    }
}
```

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `network` | string | Yes | `"mainnet"` or `"testnet"` |
| `index` | uint32 | Yes | Key index (0 to 2^31-1) |

---

#### `hsm_get_xpub`

Get extended public key for HSM root (allows external public key derivation).

**Request:**
```json
{
    "id": "3",
    "method": "hsm_get_xpub",
    "params": {
        "network": "mainnet"
    }
}
```

**Response:**
```json
{
    "id": "3",
    "result": {
        "xpub": "xpub6...",
        "path": "m/86'/0'/0'/8128'"
    }
}
```

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `network` | string | Yes | `"mainnet"` or `"testnet"` |

---

#### `hsm_sign`

Sign a 32-byte message hash using Schnorr (BIP-340) or ECDSA signature.

**Request:**
```json
{
    "id": "4",
    "method": "hsm_sign",
    "params": {
        "network": "mainnet",
        "index": 0,
        "hash": "<32 bytes hex>",
        "algo": "schnorr"
    }
}
```

**Response (Schnorr):**
```json
{
    "id": "4",
    "result": {
        "signature": "<64 bytes hex>",
        "pubkey": "02abc123...",
        "algo": "schnorr"
    }
}
```

**Response (ECDSA):**
```json
{
    "id": "4",
    "result": {
        "signature": "<DER encoded hex, 70-72 bytes>",
        "pubkey": "02abc123...",
        "algo": "ecdsa"
    }
}
```

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `network` | string | Yes | `"mainnet"` or `"testnet"` |
| `index` | uint32 | Yes | Key index |
| `hash` | bytes(32) | Yes | Message hash to sign |
| `algo` | string | No | `"schnorr"` (default) or `"ecdsa"` |

**Signature Formats:**
| Algorithm | Format | Size | Use Case |
|-----------|--------|------|----------|
| `schnorr` | BIP-340 raw | 64 bytes | Bitcoin Taproot, modern protocols |
| `ecdsa` | DER encoded | 70-72 bytes | Legacy Bitcoin, TLS, JWT, most existing systems |

---

#### `hsm_ecdh`

Compute ECDH shared secret with a counterparty public key.

**Request:**
```json
{
    "id": "5",
    "method": "hsm_ecdh",
    "params": {
        "network": "mainnet",
        "index": 0,
        "their_pubkey": "<33 or 65 bytes hex>"
    }
}
```

**Response:**
```json
{
    "id": "5",
    "result": {
        "shared_secret": "<32 bytes hex>"
    }
}
```

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `network` | string | Yes | `"mainnet"` or `"testnet"` |
| `index` | uint32 | Yes | Key index |
| `their_pubkey` | bytes | Yes | Counterparty public key (compressed or uncompressed) |

---

#### `hsm_encrypt`

Encrypt data using ECIES (Elliptic Curve Integrated Encryption Scheme).

**Request:**
```json
{
    "id": "6",
    "method": "hsm_encrypt",
    "params": {
        "network": "mainnet",
        "index": 0,
        "plaintext": "<bytes hex>",
        "their_pubkey": "<33 bytes hex, optional>",
        "aad": "<bytes hex, optional>"
    }
}
```

**Response:**
```json
{
    "id": "6",
    "result": {
        "ciphertext": "<bytes hex>",
        "nonce": "<12 bytes hex>",
        "tag": "<16 bytes hex>",
        "ephemeral_pubkey": "<33 bytes hex>"
    }
}
```

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `network` | string | Yes | `"mainnet"` or `"testnet"` |
| `index` | uint32 | Yes | Key index |
| `plaintext` | bytes | Yes | Data to encrypt (max 1024 bytes) |
| `their_pubkey` | bytes | No | Recipient public key. If omitted, encrypts to self |
| `aad` | bytes | No | Additional authenticated data (not encrypted, but authenticated) |

**Encryption Scheme:**
```
1. If their_pubkey provided:
      recipient_pubkey = their_pubkey
   Else:
      recipient_pubkey = derive_pubkey(index)  // Self-encryption

2. ephemeral_privkey = random()
   ephemeral_pubkey = ephemeral_privkey * G

3. shared_point = ephemeral_privkey * recipient_pubkey
   shared_secret = SHA256(shared_point.x || ephemeral_pubkey || recipient_pubkey)

4. encryption_key = shared_secret[0:32]
   nonce = random(12 bytes)

5. ciphertext, tag = AES-256-GCM(encryption_key, nonce, plaintext, aad)

6. Return: ciphertext, nonce, tag, ephemeral_pubkey
```

---

#### `hsm_decrypt`

Decrypt ECIES encrypted data.

**Request:**
```json
{
    "id": "7",
    "method": "hsm_decrypt",
    "params": {
        "network": "mainnet",
        "index": 0,
        "ciphertext": "<bytes hex>",
        "nonce": "<12 bytes hex>",
        "tag": "<16 bytes hex>",
        "ephemeral_pubkey": "<33 bytes hex>",
        "aad": "<bytes hex, optional>"
    }
}
```

**Response:**
```json
{
    "id": "7",
    "result": {
        "plaintext": "<bytes hex>"
    }
}
```

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `network` | string | Yes | `"mainnet"` or `"testnet"` |
| `index` | uint32 | Yes | Key index |
| `ciphertext` | bytes | Yes | Encrypted data |
| `nonce` | bytes(12) | Yes | AES-GCM nonce |
| `tag` | bytes(16) | Yes | AES-GCM authentication tag |
| `ephemeral_pubkey` | bytes(33) | Yes | Sender's ephemeral public key |
| `aad` | bytes | No | Additional authenticated data |

**Decryption Scheme:**
```
1. privkey = derive_privkey(index)

2. shared_point = privkey * ephemeral_pubkey
   shared_secret = SHA256(shared_point.x || ephemeral_pubkey || derive_pubkey(index))

3. decryption_key = shared_secret[0:32]

4. plaintext = AES-256-GCM-decrypt(decryption_key, nonce, ciphertext, tag, aad)

5. Return: plaintext (or error if authentication fails)
```

---

#### `hsm_lock`

Exit HSM mode and clear keys from memory.

**Request:**
```json
{
    "id": "8",
    "method": "hsm_lock",
    "params": {}
}
```

**Response:**
```json
{
    "id": "8",
    "result": true
}
```

---

### Error Codes

| Code | Name | Description |
|------|------|-------------|
| -32001 | HSM_NOT_ACTIVE | HSM mode not active, unlock required |
| -32002 | HSM_INVALID_INDEX | Index out of valid range |
| -32003 | HSM_DECRYPT_FAILED | Decryption failed (authentication error) |
| -32004 | HSM_PAYLOAD_TOO_LARGE | Plaintext exceeds maximum size |
| -32005 | HSM_USER_CANCELLED | User rejected operation on device |
| -32006 | HSM_ALREADY_ACTIVE | HSM mode already active |
| -32007 | HSM_WALLET_ACTIVE | Cannot enter HSM mode while wallet is unlocked |

---

## Security Analysis

### Threat Model

| Threat | Mitigation |
|--------|------------|
| **Bitcoin key extraction in HSM mode** | Master seed wiped immediately after HSM key derivation |
| **HSM key extraction in wallet mode** | HSM branch not derived in normal wallet mode |
| **Device theft (powered)** | Physical security required; optional auto-lock timeout |
| **Device theft (unpowered)** | PIN required to unlock; standard Jade security |
| **Side-channel attacks** | Limited by ESP32 hardware (no secure element) |
| **Memory dump while active** | Only HSM branch key in memory, not master seed |
| **Replay attacks** | Application-level concern; use nonces in protocols |
| **Man-in-the-middle** | ECDH provides forward secrecy with ephemeral keys |

### Security Properties

1. **Key Isolation**: HSM mode cannot access paths outside `m/86'/coin'/0'/8128'/*`
2. **Forward Secrecy**: ECIES uses ephemeral keys for each encryption
3. **Authenticated Encryption**: AES-256-GCM provides confidentiality and integrity
4. **Deterministic Derivation**: Same index always produces same key (reproducible)

### Limitations

1. **No Secure Element**: ESP32 does not have a dedicated secure element
2. **No Certification**: Not FIPS 140-2 or Common Criteria certified
3. **Single User**: No multi-tenant or role-based access control
4. **No Audit Log**: Operations not logged (could be added)

---

## Implementation Plan

### Phase 1: Core HSM Infrastructure

**Files to modify:**
- `main/ui/dashboard.c` - Add "Unlock HSM" menu option
- `main/keychain.h` / `main/keychain.c` - Add HSM keychain structure
- `main/process/dashboard.c` - Add HSM RPC dispatch

**New files:**
- `main/hsm.h` - HSM mode header
- `main/hsm.c` - HSM mode implementation
- `main/process/hsm_*.c` - RPC handlers for each HSM method

**Tasks:**
1. Define `hsm_keychain_t` structure
2. Implement HSM unlock flow (PIN → derive → wipe seed)
3. Implement `hsm_get_info` RPC
4. Implement `hsm_lock` RPC
5. Add UI for HSM mode status screen

### Phase 2: Key Derivation

**Tasks:**
1. Implement child key derivation from HSM root
2. Implement `hsm_get_pubkey` RPC
3. Implement `hsm_get_xpub` RPC
4. Add unit tests for key derivation

### Phase 3: Signing

**Tasks:**
1. Implement Schnorr signing with derived keys
2. Implement `hsm_sign` RPC
3. Add optional confirmation screen
4. Add unit tests for signing

### Phase 4: ECDH

**Tasks:**
1. Implement ECDH shared secret computation
2. Implement `hsm_ecdh` RPC
3. Add unit tests for ECDH

### Phase 5: Encryption/Decryption

**Tasks:**
1. Implement ECIES encryption scheme
2. Implement `hsm_encrypt` RPC
3. Implement `hsm_decrypt` RPC
4. Add unit tests for encryption round-trip

### Phase 6: C# Client Integration

**Tasks:**
1. Add HSM methods to `JadeRpc.cs`
2. Create `HsmClient` high-level wrapper
3. Add examples and documentation
4. Add integration tests

---

## Configuration Options

### Build-time Options

```c
// sdkconfig or menuconfig
#define CONFIG_HSM_MODE_ENABLED         1   // Enable HSM mode feature
#define CONFIG_HSM_MAX_PLAINTEXT_SIZE   1024 // Max encryption payload
#define CONFIG_HSM_DEFAULT_COIN_TYPE    0   // 0=mainnet, 1=testnet
#define CONFIG_HSM_REQUIRE_CONFIRMATION 0   // 1=always show confirmation
#define CONFIG_HSM_AUTO_LOCK_TIMEOUT    0   // 0=disabled, >0=seconds
```

### Runtime Options

Options configurable via device menu:
- Network selection (mainnet/testnet) at unlock time
- Auto-lock timeout
- Confirmation requirement toggle

---

## Example Use Cases

### 1. API Request Signing

```python
# Server-side: Sign API requests
jade.hsm_unlock(pin="123456")

# Sign each API request
request_hash = sha256(request_body)
result = jade.hsm_sign(index=0, hash=request_hash)
signature = result["signature"]

# Client verifies with known public key
```

### 2. Encrypted Configuration Storage

```python
# Encrypt sensitive config
result = jade.hsm_encrypt(
    index=0,
    plaintext=json.dumps(config).encode()
)
# Store: ciphertext, nonce, tag, ephemeral_pubkey

# Later: decrypt
config_json = jade.hsm_decrypt(
    index=0,
    ciphertext=stored["ciphertext"],
    nonce=stored["nonce"],
    tag=stored["tag"],
    ephemeral_pubkey=stored["ephemeral_pubkey"]
)
config = json.loads(config_json)
```

### 3. Secure Messaging Between Devices

```python
# Alice's Jade (index 1)
alice_pubkey = alice_jade.hsm_get_pubkey(index=1)["pubkey"]

# Bob's Jade (index 2) encrypts to Alice
result = bob_jade.hsm_encrypt(
    index=2,
    plaintext=message,
    their_pubkey=alice_pubkey
)

# Alice decrypts (needs Bob's ephemeral pubkey from result)
plaintext = alice_jade.hsm_decrypt(
    index=1,
    ciphertext=result["ciphertext"],
    nonce=result["nonce"],
    tag=result["tag"],
    ephemeral_pubkey=result["ephemeral_pubkey"]
)
```

### 4. SSH Key Agent

```python
# Get SSH public key
pubkey = jade.hsm_get_pubkey(index=100)  # Dedicated SSH index

# When SSH challenge arrives
signature = jade.hsm_sign(
    index=100,
    hash=ssh_challenge_hash,
    require_confirmation=True  # User approves on device
)
```

---

## Design Decisions

The following decisions have been finalized for this implementation:

| Question | Decision | Rationale |
|----------|----------|-----------|
| **Network support** | Both mainnet and testnet simultaneously | More flexible; different keys for production vs testing |
| **Max plaintext size** | 1024 bytes | Sufficient for most use cases; memory-constrained device |
| **Signature algorithms** | Both Schnorr and ECDSA | Schnorr for modern protocols, ECDSA for legacy compatibility |
| **Auto-lock timeout** | User-configurable, default disabled | Flexibility for different security requirements |
| **Operation confirmation** | No confirmation, fully automatic | Designed for automated/server use cases |
| **Key export** | No export allowed | Security: rely on BIP39 seed backup only |

### Auto-lock Behavior

When auto-lock is enabled:
- Timer resets on each RPC operation
- Timer runs from last activity (inactivity timeout)
- When timeout expires, HSM mode locks automatically
- Device returns to startup screen
- Requires PIN re-entry to unlock again

### Signature Algorithm Selection

| Use Case | Recommended Algorithm |
|----------|----------------------|
| Bitcoin Taproot transactions | Schnorr |
| Modern protocols (new designs) | Schnorr |
| JWT/OAuth signing | ECDSA |
| TLS client certificates | ECDSA |
| Legacy Bitcoin (SegWit, Legacy) | ECDSA |
| Ethereum transactions | ECDSA |

---

## References

- [BIP-32: Hierarchical Deterministic Wallets](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki)
- [BIP-86: Key Derivation for Single Key P2TR](https://github.com/bitcoin/bips/blob/master/bip-0086.mediawiki)
- [BIP-340: Schnorr Signatures for secp256k1](https://github.com/bitcoin/bips/blob/master/bip-0340.mediawiki)
- [ECIES: Elliptic Curve Integrated Encryption Scheme](https://en.wikipedia.org/wiki/Integrated_Encryption_Scheme)
- [AES-GCM: NIST SP 800-38D](https://csrc.nist.gov/publications/detail/sp/800-38d/final)

---

## Implementation Status

**Status: COMPLETE**

All phases have been implemented and tested successfully.

### Firmware Implementation

| Component | File | Status |
|-----------|------|--------|
| HSM Core Module | `main/hsm.h`, `main/hsm.c` | Complete |
| RPC Handlers | `main/process/hsm_process.c` | Complete |
| Dashboard Integration | `main/process/dashboard.c` | Complete |
| UI Session Menu | `main/ui/dashboard.c` | Complete |
| Button Events | `main/button_events.h` | Complete |
| Startup Screen HSM Option | `main/process/dashboard.c` | Complete |

### C# Client Implementation

| Component | File | Status |
|-----------|------|--------|
| HSM RPC Methods | `jade-csharp-client/src/JadeClient/Protocol/JadeRpc.cs` | Complete |
| HSM Data Models | `jade-csharp-client/src/JadeClient/Models/HsmModels.cs` | Complete |
| HSM Test Sample | `jade-csharp-client/samples/HsmTest/Program.cs` | Complete |
| BasicUsage Integration | `jade-csharp-client/samples/BasicUsage/Program.cs` | Complete |

### Implemented RPC Methods

| Method | Description | Tested |
|--------|-------------|--------|
| `hsm_get_info` | Get HSM status, networks, paths, pubkeys, counters | Yes |
| `hsm_get_pubkey` | Get public key at index | Yes |
| `hsm_get_xpub` | Get extended public key (base58) | Yes |
| `hsm_sign` | Sign hash (Schnorr or ECDSA) | Yes |
| `hsm_ecdh` | Compute ECDH shared secret | Yes |
| `hsm_encrypt` | ECIES encryption (AES-256-GCM) | Yes |
| `hsm_decrypt` | ECIES decryption | Yes |
| `hsm_lock` | Deactivate HSM mode | Yes |

### Test Results (2026-01-08)

All tests passed successfully:

```
--- HSM Test Summary ---
- Device Unlock: PIN authentication successful
- HSM Activation: Mode activated via device menu
- HSM Get Info: Returns networks, paths, pubkeys, counters
- HSM Get XPub: xpub6DXuQW1Q2Jvj3TVFQpw7YidMMBuBCWBbytftitSDWQc8pey...
- HSM Get Pubkeys: Retrieved pubkeys at indices 0, 1, 2
- Schnorr Signing: 64-byte BIP-340 signature
- ECDSA Signing: 70-byte DER-encoded signature
- ECDH: 32-byte shared secret, symmetric verified
- ECIES Encrypt/Decrypt: Round-trip encryption successful
- ECIES with AAD: Additional authenticated data works
- ECIES Cross-Key: Encrypt to one key, decrypt with another
- Testnet Keys: tpub and testnet pubkeys working
- Operations Count: Counter incremented correctly
- **Seed Isolation: PASS** - Wallet seed is NOT accessible in HSM mode
```

### Security: Seed Isolation

A critical security feature ensures that when HSM mode is activated, the wallet's master seed is completely wiped from memory:

1. **On HSM Activation** (`dashboard.c`): After `hsm_activate()` succeeds, `keychain_clear()` is called to wipe the seed
2. **Message Dispatch** (`dashboard.c`): HSM methods are allowed when HSM is active, even without keychain
3. **Verification Test** (`HsmTest/Program.cs`): Attempts to call wallet functions (e.g., `GetXpubAsync`) which should fail

```c
// In dashboard.c - HSM activation handler
if (hsm_activate(keychain_get()->seed, keychain_get()->seed_len,
                (uint8_t)keychain_get_userdata())) {
    // CRITICAL: Clear the keychain to wipe the seed from memory
    keychain_clear();
    // ... show success message
}
```

This ensures:
- HSM keys are derived from the seed at activation time and stored in HSM module
- The master seed is then wiped from memory
- Wallet operations (requiring the seed) fail with "hardware locked" error
- HSM operations continue to work using the pre-derived keys

### Key Implementation Details

#### CBOR Response Format

All HSM RPC handlers use `rpc_init_cbor()` to properly format responses:

```c
CborEncoder root_map;
cbor_encoder_create_map(&root_encoder, &root_map, 2);  // id + result

const char* id = NULL;
size_t id_len = 0;
rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

CborEncoder result_map;
cbor_encoder_create_map(&root_map, &result_map, N);  // N fields in result
// ... add result fields ...
```

#### XPub Generation

Uses `bip32_key_init()` with computed hash160 for proper xpub serialization:

```c
uint8_t hash160[HASH160_LEN];
wally_hash160(root_pubkey, EC_PUBLIC_KEY_LEN, hash160, sizeof(hash160));

bip32_key_init(
    version, 4, HSM_PATH_HSM_BRANCH,
    root_chaincode, 32,
    root_pubkey, EC_PUBLIC_KEY_LEN,
    NULL, 0,  // no private key
    hash160, sizeof(hash160),
    NULL, 0,  // no parent160
    &key);

bip32_key_to_base58(&key, BIP32_FLAG_KEY_PUBLIC, xpub_out);
```

---

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 0.1 | 2026-01-08 | Draft | Initial design document |
| 0.2 | 2026-01-08 | Draft | Finalized design decisions: dual network support, Schnorr+ECDSA signing, configurable auto-lock timeout, no confirmation (automatic), no key export |
| 0.3 | 2026-01-08 | Draft | Removed `hsm_set_timeout` RPC - timeout must be configured via device UI only for security |
| 1.0 | 2026-01-08 | Release | Implementation complete - all RPC methods implemented and tested |
| 1.1 | 2026-01-08 | Security | Added seed isolation: keychain cleared after HSM activation to ensure wallet seed is not accessible in HSM mode |
| 1.2 | 2026-01-08 | UI | Added "Unlock HSM" option directly on startup screen as per design document |
