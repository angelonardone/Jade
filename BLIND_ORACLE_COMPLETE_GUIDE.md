# Jade Blind Oracle PIN Server - Complete Technical Guide

**A Unified Reference for the Blind Oracle Protocol, Implementation, and Client Integration**

**Document Version**: 1.0
**Date**: January 2026
**Repository**: Blockstream Jade
**Verified Against**: `main/process/pinclient.c`, `pinserver/`, `jade-csharp-client/`

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [What is the Blind Oracle?](#what-is-the-blind-oracle)
3. [Architecture Overview](#architecture-overview)
4. [All Cryptographic Keys (Numbered Reference)](#all-cryptographic-keys-numbered-reference)
5. [Phase 1: Device Initialization](#phase-1-device-initialization)
6. [Phase 2: Wallet Creation (set_pin)](#phase-2-wallet-creation-set_pin)
7. [Phase 3: Wallet Unlock (get_pin)](#phase-3-wallet-unlock-get_pin)
8. [jade-csharp Client Integration](#jade-csharp-client-integration)
9. [Protocol Details](#protocol-details)
10. [Security Properties](#security-properties)
11. [Attack Scenarios](#attack-scenarios)
12. [Running Your Own PIN Server](#running-your-own-pin-server)
13. [PIN Server Migration and Configuration](#pin-server-migration-and-configuration)
14. [Frequently Asked Questions](#frequently-asked-questions)
15. [Academic Foundations](#academic-foundations)
16. [Code References](#code-references)

---

## Executive Summary

The Jade Blind Oracle is a cryptographic protocol that protects your wallet with a 6-digit PIN while ensuring:

1. **The server NEVER sees your PIN** - Only a triple-derived hash is transmitted
2. **The server NEVER sees your wallet** - Only encrypted data components are exchanged
3. **3-attempt enforcement** - Both device and server independently track failed attempts
4. **Network required** - The wallet cannot be unlocked without server cooperation

**Key Innovation**: The PIN private key is stored **UNENCRYPTED** on the device because it's needed to bootstrap the protocol - but this key alone cannot decrypt the wallet without the correct PIN AND server cooperation.

---

## What is the Blind Oracle?

A **Blind Oracle** is a cryptographic protocol where:

1. The client proves knowledge of a secret (the PIN)
2. The server helps verify the secret without ever learning it
3. The server can limit attempts without knowing the secret

### Key Properties

| Property | Description |
|----------|-------------|
| **Blindness** | Server never sees PIN, mnemonic, or user data |
| **Rate Limiting** | Maximum 3 PIN attempts enforced by server |
| **Privacy** | Server cannot correlate requests or track users |
| **Two-Factor Security** | Requires BOTH device AND server to unlock |

### Is the PIN Server Always Required?

**YES** - The PIN server is **mandatory** for unlocking an encrypted wallet.

```
Encrypted Wallet + Network Available   = MUST use PIN server
Encrypted Wallet + Network Unavailable = WALLET LOCKED (no fallback)
```

**Exceptions where PIN server is NOT used:**
- Temporary wallet mode (`temporary_wallet=True`)
- Already unlocked in current session
- First-time setup (before any wallet exists)
- Debug/CI testing mode

---

## Architecture Overview

```
+---------------------------------------------------------------+
|                     Jade Device (Client)                       |
|                                                                |
|  +----------------------------------------------------------+ |
|  | NVS Flash Storage (Encrypted Partition)                  | |
|  |                                                          | |
|  |  "PIN" Namespace:                                        | |
|  |  - privatekey: KEY1_PIN_PRIVATE (32 bytes, UNENCRYPTED)  | |
|  |  - blob: IV || AES-encrypted(mnemonic) || HMAC           | |
|  |  - counter: 3 (attempts remaining)                       | |
|  |  - antireplay: uint32 (monotonic counter)                | |
|  +----------------------------------------------------------+ |
|                            |                                   |
|  User enters PIN           |  Device derives:                  |
|  (6 digits)                v  SECRET1_PIN_SECRET               |
|                                                                |
|  +----------------------------------------------------------+ |
|  | Ephemeral Session Keys (per request)                     | |
|  |                                                          | |
|  | - KEY5_CLIENT_EPHEMERAL_PRIVATE (random)                 | |
|  | - KEY6_CLIENT_EPHEMERAL_PUBLIC (cke)                     | |
|  | - KEY9_SERVER_SESSION_PUBLIC (ske, via BIP341 tweak)     | |
|  +----------------------------------------------------------+ |
|                            |                                   |
+----------------------------+-----------------------------------+
                             | HTTPS POST (JSON + Base64)
                             v
+---------------------------------------------------------------+
|                    PIN Server (Blind Oracle)                   |
|                                                                |
|  Endpoint: https://j8d.io (default) or custom                  |
|  Tor: http://mrrxtq6t...onion                                  |
|                                                                |
|  +----------------------------------------------------------+ |
|  | Static Keys                                              | |
|  | - KEY7_SERVER_STATIC_PRIVATE (from server_private_key.key) |
|  | - KEY8_SERVER_STATIC_PUBLIC (hardcoded in firmware)      | |
|  | - KEY10_AES_PIN_DATA (derived from KEY7)                 | |
|  +----------------------------------------------------------+ |
|                            |                                   |
|  +----------------------------------------------------------+ |
|  | Database (Redis or File)                                 | |
|  |                                                          | |
|  | Key: SHA256(KEY2_PIN_PUBLIC)                             | |
|  | Value: version || hmac || encrypted_data                 | |
|  |                                                          | |
|  | Encrypted content (server CANNOT decrypt without         | |
|  | KEY2_PIN_PUBLIC recovered from valid client signature):  | |
|  |   - SHA256(SECRET1_PIN_SECRET)                           | |
|  |   - KEY11_SERVER_AES                                     | |
|  |   - counter (0-3)                                        | |
|  |   - replay_counter                                       | |
|  +----------------------------------------------------------+ |
|                            |                                   |
+----------------------------+-----------------------------------+
                             | Returns encrypted AES key
                             v
+---------------------------------------------------------------+
|                     Jade Device (Client)                       |
|                                                                |
|  Final AES Key Derivation:                                     |
|  KEY4_FINAL_AES = HMAC-SHA256(                                 |
|      HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET),        |
|      PIN                                                       |
|  )                                                             |
|                                                                |
|  Decrypt wallet blob -> Load keys -> UNLOCKED                  |
+---------------------------------------------------------------+
```

---

## All Cryptographic Keys (Numbered Reference)

### Device-Side Keys

| # | Key Name | Size | Storage | Description |
|---|----------|------|---------|-------------|
| 1 | KEY1_PIN_PRIVATE | 32 bytes | NVS "PIN/privatekey" (UNENCRYPTED) | Device identity key, generated at first boot |
| 2 | KEY2_PIN_PUBLIC | 33 bytes | Not stored (recovered from signature) | Public key derived from KEY1 |
| 3 | KEY3_MNEMONIC_ENTROPY | 16-32 bytes | NVS encrypted blob | BIP39 entropy |
| 4 | KEY4_FINAL_AES | 32 bytes | NEVER stored | Derived each unlock, decrypts wallet |

### Ephemeral Keys (per request)

| # | Key Name | Size | Location | Description |
|---|----------|------|----------|-------------|
| 5 | KEY5_CLIENT_EPHEMERAL_PRIVATE | 32 bytes | `pin_keys_t.privkey` | Random per-request ECDH key |
| 6 | KEY6_CLIENT_EPHEMERAL_PUBLIC | 33 bytes | `pin_keys_t.cke` | "cke" = Client Key Exchange |
| 9 | KEY9_SERVER_SESSION_PUBLIC | 33 bytes | `pin_keys_t.ske` | "ske" = Server Key Exchange (BIP341 tweaked) |

### Server-Side Keys

| # | Key Name | Size | Storage | Description |
|---|----------|------|---------|-------------|
| 7 | KEY7_SERVER_STATIC_PRIVATE | 32 bytes | `server_private_key.key` | Server's permanent private key |
| 8 | KEY8_SERVER_STATIC_PUBLIC | 33 bytes | Hardcoded in firmware | Server's public key |
| 10 | KEY10_AES_PIN_DATA | 32 bytes | Derived from KEY7 | Master encryption key for database |
| 11 | KEY11_SERVER_AES | 32 bytes | Encrypted in database | Server's contribution to final AES key |

### Derived Secrets

| # | Secret Name | Derivation | Purpose |
|---|-------------|------------|---------|
| SECRET1 | PIN_SECRET | `HMAC(HMAC(KEY1, 0x00), PIN)` | Sent to server for PIN verification |
| SECRET2 | ECDH_SHARED | `ECDH(KEY5, KEY9)` | Encrypts request/response payloads |

---

## Phase 1: Device Initialization

### First Boot Key Generation

**When:** First boot, no existing PIN private key in NVS

**Code:** `keychain.c:716-737`, `main/main.c:225`

```c
// main/main.c - called during boot
keychain_init_unit_key()

// keychain.c:716-737
bool keychain_init_unit_key(void) {
    uint8_t privatekey[32];

    // Try to load existing key
    bool res = storage_get_pin_privatekey(privatekey, 32);
    if (!res) {
        // Generate NEW random key via ESP32 hardware RNG
        keychain_get_new_privatekey(privatekey, 32);
        // Store UNENCRYPTED in NVS
        storage_set_pin_privatekey(privatekey, 32);
    }
    return res;
}
```

**What Gets Stored:**

| NVS Namespace | NVS Key | Value | Encryption |
|---------------|---------|-------|------------|
| "PIN" | "privatekey" | KEY1_PIN_PRIVATE (32 bytes) | **NONE** |

### Why the PIN Private Key is Unencrypted

**Question:** Why not encrypt KEY1_PIN_PRIVATE with the PIN?

**Answer:** Chicken-and-egg problem:
1. Need KEY1 to create valid signature for server
2. Server validates signature before returning AES key
3. Can't decrypt KEY1 without first contacting server
4. Can't contact server without KEY1

**Security:** KEY1 alone cannot decrypt the wallet - you still need:
- Correct PIN (3 attempts max)
- Server cooperation (provides KEY11_SERVER_AES)

---

## Phase 2: Wallet Creation (set_pin)

### Overview Flow

```
1. User creates/imports mnemonic
2. User enters PIN (confirmed twice)
3. Device contacts PIN server with encrypted payload
4. Server stores encrypted data, returns AES key component
5. Device derives final AES key, encrypts mnemonic
6. Mnemonic blob stored in NVS
```

### Step-by-Step Implementation

#### 2.1 User Enters PIN

**Code:** `auth_user.c:115-183`

```c
bool set_pin_get_aeskey(jade_process_t* process, ...) {
    pin_insert_t pin_insert = { .initial_state = RANDOM };

    while (true) {
        // First entry
        run_pin_entry_loop(&pin_insert);
        memcpy(pin, pin_insert.pin, 6);

        // Confirm entry
        reset_pin(&pin_insert, "Confirm PIN");
        run_pin_entry_loop(&pin_insert);

        // Check match
        if (!sodium_memcmp(pin, pin_insert.pin, 6)) {
            break;  // Matched!
        }
        // Mismatch - retry
    }

    // Contact PIN server
    return pinclient_set(process, pin, pin_len, aeskey, aes_len);
}
```

#### 2.2 Generate Ephemeral Keys

**Code:** `pinclient.c:237-266`

```c
static pinserver_result_t generate_ephemeral_pinkeys(pin_keys_t* pinkeys) {
    // Generate random ephemeral client key pair
    keychain_get_new_privatekey(pinkeys->privkey, 32);  // KEY5
    wally_ec_public_key_from_private_key(
        pinkeys->privkey, 32,
        pinkeys->cke, 33);  // KEY6

    // Load replay counter from NVS
    uint32_t counter;
    storage_get_replay_counter(&counter);
    memcpy(pinkeys->replay_counter, &counter, 4);

    // Derive session server key via BIP341 tweak
    generate_ske(pinkeys);  // KEY9

    return PIN_SUCCESS;
}
```

#### 2.3 BIP341 Server Key Tweaking

**Code:** `pinclient.c:130-167`

```c
static bool generate_ske(pin_keys_t* pinkeys) {
    // Get hardcoded server public key (or user-configured)
    const uint8_t* pubkey = server_public_key_start;  // KEY8

    // tweak = SHA256(HMAC-SHA256(cke, replay_counter))
    uint8_t hmac_tweak[32], sha_tweak[32];
    wally_hmac_sha256(pinkeys->cke, 33,
                      pinkeys->replay_counter, 4,
                      hmac_tweak, 32);
    wally_sha256(hmac_tweak, 32, sha_tweak, 32);

    // Apply BIP341 tweak to get session server public key
    wally_ec_public_key_bip341_tweak(
        pubkey, 33,        // KEY8
        sha_tweak, 32,
        0,
        pinkeys->ske, 33); // KEY9

    return true;
}
```

**Why Tweak?**
- Each session gets unique server public key
- Prevents replay attacks
- No handshake required (Protocol v2 improvement)

#### 2.4 Derive PIN Secret

**Code:** `pinclient.c:329-350`

```c
static bool get_pin_secret(const uint8_t* pin, size_t pin_len,
                           const uint8_t* pin_privatekey, size_t pk_len,
                           uint8_t* pin_secret, size_t ps_len) {
    const uint8_t subkey = 0;
    uint8_t hmac_key[32];

    // Step 1: HMAC(KEY1_PIN_PRIVATE, 0x00) -> hmac_key
    wally_hmac_sha256(pin_privatekey, pk_len, &subkey, 1, hmac_key, 32);

    // Step 2: HMAC(hmac_key, PIN_digits) -> SECRET1_PIN_SECRET
    wally_hmac_sha256(hmac_key, 32, pin, pin_len, pin_secret, 32);

    return true;
}
```

**Formula:**
```
SECRET1_PIN_SECRET = HMAC-SHA256(HMAC-SHA256(KEY1_PIN_PRIVATE, 0x00), PIN)
```

#### 2.5 Sign Payload

**Code:** `pinclient.c:352-388`

```c
static bool sign_payload(const uint8_t* pin_privatekey, size_t pk_len,
                         const pin_keys_t* pinkeys,
                         const uint8_t* pinsecret, size_t ps_len,
                         const uint8_t* entropy, size_t ent_len,
                         uint8_t* sig, size_t sig_len) {
    // Concatenate: cke || replay_counter || pinsecret || entropy
    uint8_t shadata[33 + 4 + 32 + 32];  // = 101 bytes max
    memcpy(shadata, pinkeys->cke, 33);
    memcpy(shadata + 33, pinkeys->replay_counter, 4);
    memcpy(shadata + 37, pinsecret, 32);
    memcpy(shadata + 69, entropy, ent_len);  // 32 bytes for set_pin

    // Hash
    uint8_t shahash[32];
    wally_sha256(shadata, 69 + ent_len, shahash, 32);

    // Sign with recoverable signature (allows server to recover KEY2_PIN_PUBLIC)
    wally_ec_sig_from_bytes(
        pin_privatekey, pk_len,
        shahash, 32,
        EC_FLAG_ECDSA | EC_FLAG_RECOVERABLE,
        sig, sig_len);  // 65 bytes

    return true;
}
```

**Key Insight:** The recoverable signature allows the server to recover KEY2_PIN_PUBLIC from:
- The signature (65 bytes)
- The message hash
- Without the client ever sending the public key directly

#### 2.6 Encrypt Payload

**Code:** `pinclient.c:171-204`

```c
static bool encrypt_payload(const pin_keys_t* pinkeys,
                            const uint8_t* pin_secret, size_t ps_len,
                            const uint8_t* entropy, size_t ent_len,
                            const uint8_t* sig, size_t sig_len,
                            uint8_t* encrypted, size_t enc_len,
                            size_t* written) {
    // Plaintext: pin_secret || entropy || signature
    uint8_t cleartext[32 + 32 + 65];  // = 129 bytes max
    memcpy(cleartext, pin_secret, 32);
    memcpy(cleartext + 32, entropy, ent_len);
    memcpy(cleartext + 32 + ent_len, sig, 65);

    // Generate random IV
    uint8_t iv[16];
    get_random(iv, 16);

    // Encrypt using ECDH-derived key
    wally_aes_cbc_with_ecdh_key(
        pinkeys->privkey, 32,           // KEY5
        iv, 16,
        cleartext, 32 + ent_len + 65,
        pinkeys->ske, 33,               // KEY9
        LABEL_ORACLE_REQUEST, 20,       // "blind_oracle_request"
        AES_FLAG_ENCRYPT,
        encrypted, enc_len, written);

    return true;
}
```

**Domain Separation Labels:**
- Request: `"blind_oracle_request"` (20 bytes)
- Response: `"blind_oracle_response"` (21 bytes)

#### 2.7 Send to Server

**Code:** `pinclient.c:69-127`

```c
static void send_http_request_reply(jade_process_t* process,
                                     const char* document,
                                     const char* data) {
    client_data_request_t pin_data = {
        .request_type = CLIENT_REQUEST_TYPE_HTTP,
        .method = "POST",
        .accept = "json",
        .on_reply = "pin",
        .strdata = data,
    };

    // URLs: bespoke or defaults
    // Default: https://j8d.io/set_pin
    // Onion: http://mrrxtq6t.../set_pin
    ...
}
```

**HTTP Request Format:**
```
POST /set_pin
Content-Type: text/plain

Base64(cke || replay_counter || encrypted_payload)
```

#### 2.8 Server Processing

**Code:** `pinserver/pindb.py:267-299`

```python
@classmethod
def set_pin(cls, cke, payload, aes_pin_data_key, replay_counter=None):
    # 1. Decrypt payload using ECDH
    # 2. Extract fields and recover public key from signature
    pin_secret, entropy, pin_pubkey = cls._extract_fields(cke, payload, replay_counter)
    # pin_pubkey = KEY2_PIN_PUBLIC (recovered!)

    # 3. Compute lookup key
    pin_pubkey_hash = sha256(pin_pubkey)

    # 4. Generate server AES key from combined entropy
    our_random = os.urandom(32)
    new_key = hmac_sha256(our_random, entropy)  # KEY11_SERVER_AES

    # 5. Store encrypted data
    hash_pin_secret = sha256(pin_secret)
    cls._save_pin_fields(
        pin_pubkey_hash,    # Lookup key
        hash_pin_secret,    # For PIN verification
        new_key,            # KEY11_SERVER_AES
        pin_pubkey,         # For encryption key derivation
        aes_pin_data_key,   # KEY10
        0,                  # counter (attempts)
        replay_bytes
    )

    # 6. Return key derived from saved key + pin_secret
    return cls.make_client_aes_key(pin_secret, new_key)
```

#### 2.9 Device Receives AES Key

**Code:** `pinclient.c:484-487`

```c
// Server returns: HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET)

// Derive FINAL AES key by combining with raw PIN
wally_hmac_sha256(serverkey, 32, pin, pin_len, finalaes, 32);
// finalaes = KEY4_FINAL_AES
```

**Complete Derivation:**
```
KEY4_FINAL_AES = HMAC-SHA256(
    HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET),
    PIN
)
```

#### 2.10 Encrypt and Store Mnemonic

**Code:** `keychain.c:411-433`

```c
// Encrypt: AES-256-CBC with random IV, append HMAC
aes_encrypt_bytes(aeskey, 32, serialized_keys, len, output, output_len);
wally_hmac_sha256(aeskey, 32, output, output_len - 32, output + output_len - 32, 32);

// Store in NVS
storage_set_encrypted_blob(encrypted, encrypted_len);
```

**NVS Storage After set_pin:**

| Key | Value | Size |
|-----|-------|------|
| `privatekey` | KEY1_PIN_PRIVATE | 32 bytes |
| `blob` | IV \|\| AES(mnemonic) \|\| HMAC | Variable |
| `counter` | 3 | 1 byte |
| `antireplay` | replay_counter + 1 | 4 bytes |

---

## Phase 3: Wallet Unlock (get_pin)

### Overview Flow

```
1. User enters PIN on device screen
2. Device contacts PIN server with encrypted payload (NO entropy)
3. Server verifies PIN secret hash, returns AES key component
4. Device derives final AES key
5. Device decrypts mnemonic blob
6. Keys loaded into RAM -> UNLOCKED
```

### Key Differences from set_pin

| Aspect | set_pin | get_pin |
|--------|---------|---------|
| Client entropy | 32 bytes (for new AES key) | None (0 bytes) |
| Server action | Creates new record | Looks up existing record |
| Counter on success | Initialize to 0 | Reset to 0 |
| Counter on failure | N/A | Increment (+1) |

### Step-by-Step Implementation

#### 3.1 User Enters PIN

**Code:** `auth_user.c:193-296`

```c
static bool get_pin_get_aeskey(...) {
    pin_insert_t pin_insert;

    while (true) {
        run_pin_entry_loop(&pin_insert);
        memcpy(pin, pin_insert.pin, 6);

        // Check for wallet erase PIN
        if (is_wallet_erase_pin(pin, 6)) {
            erase_wallet_and_shutdown();
        }

        // Try PIN with server
        pinserver_result_t pir = pinclient_get(process, pin, 6, aeskey, 32);

        if (pir.result == PIN_SUCCESS) {
            return true;  // Got KEY4_FINAL_AES!
        }

        if (pir.result == PIN_CAN_RETRY) {
            // Network error - offer retry
            if (await_yesno_activity("Failed communicating with Oracle - retry?")) {
                continue;
            }
            return false;
        }

        // Wrong PIN - show error, loop continues
        await_error_activity(pir.message);
    }
}
```

#### 3.2 Contact Server (GET_PIN)

**Code:** `pinclient.c:545-552`

```c
bool pinclient_get(jade_process_t* process,
                   const uint8_t* pin, size_t pin_len,
                   uint8_t* finalaes, size_t finalaes_len) {
    JADE_LOGI("Fetching pinserver data");
    const bool pass_client_entropy = false;  // NO ENTROPY for GET
    return get_pinserver_aeskey(process, pin, pin_len,
                                PINSERVER_DOC_GET_PIN,
                                pass_client_entropy, finalaes, finalaes_len);
}
```

#### 3.3 Server Verifies PIN

**Code:** `pinserver/pindb.py:202-247`

```python
@classmethod
def get_aes_key_impl(cls, pin_pubkey, pin_secret, aes_pin_data_key, replay_counter=None):
    # 1. Lookup by hash
    pin_pubkey_hash = sha256(pin_pubkey)

    # 2. Load and decrypt stored data
    saved_hps, saved_key, counter, replay_local = cls._load_pin_fields(
        pin_pubkey_hash, pin_pubkey, aes_pin_data_key)

    # 3. Check anti-replay
    cls._check_v2_anti_replay(replay_local, replay_counter)

    # 4. Verify PIN
    hash_pin_secret = sha256(pin_secret)

    if compare_digest(saved_hps, hash_pin_secret):
        # CORRECT PIN - reset counter, return key
        if counter != 0 or replay_counter:
            cls._save_pin_fields(..., count=0, ...)  # Reset!
        return saved_key  # KEY11_SERVER_AES

    else:
        # WRONG PIN
        if counter >= 2:
            # 3rd failure - ERASE DATA
            cls.storage.remove(pin_pubkey_hash)
            raise Exception("Too many attempts")
        else:
            # Increment counter
            cls._save_pin_fields(..., count=counter + 1, ...)
            raise Exception(f"Invalid PIN ({2 - counter} remaining)")
```

**Important:** Server ALWAYS returns a key (real or junk) to prevent timing attacks:

```python
@classmethod
def get_aes_key(cls, cke, payload, aes_pin_data_key, replay_counter=None):
    try:
        saved_key = cls.get_aes_key_impl(...)
    except Exception:
        # Wrong PIN or error - return JUNK key
        saved_key = os.urandom(32)

    return cls.make_client_aes_key(pin_secret, saved_key)
```

#### 3.4 Device Decrypts Wallet

**Code:** `keychain.c:495-535`

```c
static bool keychain_load_and_decrypt_blob(const uint8_t* aeskey, ...) {
    // 1. Decrement local counter BEFORE attempting decryption
    if (!storage_decrement_counter()) {
        return false;  // Out of attempts
    }

    // 2. Load encrypted blob from NVS
    storage_get_encrypted_blob(encrypted, sizeof(encrypted), &len);

    // 3. Verify HMAC at tail of buffer
    uint8_t hmac_calculated[32];
    wally_hmac_sha256(aeskey, 32, encrypted, len - 32, hmac_calculated, 32);

    if (crypto_verify_32(hmac_calculated, encrypted + len - 32) != 0) {
        // WRONG PIN - HMAC mismatch!
        if (keychain_pin_attempts_remaining() == 0) {
            keychain_erase_encrypted();  // ERASE on 3rd failure
        }
        return false;
    }

    // 4. Decrypt with AES-256-CBC
    aes_decrypt_bytes(aeskey, 32, encrypted, len - 32, output, ...);

    // 5. Success - restore counter to 3
    storage_restore_counter();
    return true;
}
```

---

## jade-csharp Client Integration

The `jade-csharp-client` provides a .NET implementation for communicating with Jade devices and the Blind Oracle PIN server.

### Architecture

```
+------------------+     +----------------+     +---------------+
|  Your .NET App   |---->| JadeRpc        |---->| Jade Device   |
+------------------+     +----------------+     +---------------+
                               |
                               | HTTP (via IPinServerHandler)
                               v
                         +-----------------+
                         | PIN Server      |
                         | (j8d.io)        |
                         +-----------------+
```

### Key Components

#### IPinServerHandler Interface

**Code:** `jade-csharp-client/src/JadeClient/PinServer/IPinServerHandler.cs`

```csharp
public interface IPinServerHandler : IDisposable
{
    /// <summary>
    /// Process an HTTP request from Jade and return the response.
    /// </summary>
    /// <param name="endpoint">The endpoint path (e.g., "/get_pin", "/set_pin").</param>
    /// <param name="requestData">Request payload (JSON format).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response data to send back to Jade.</returns>
    Task<string> ProcessRequestAsync(string endpoint, string? requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the server's public key (33 bytes, compressed secp256k1).
    /// </summary>
    byte[] GetServerPublicKey();
}
```

#### RemotePinServerHandler

**Code:** `jade-csharp-client/src/JadeClient/PinServer/RemotePinServerHandler.cs`

```csharp
public class RemotePinServerHandler : IPinServerHandler
{
    /// <summary>
    /// Default Blockstream PIN server URL.
    /// </summary>
    public const string DefaultPinServerUrl = "https://j8d.io";

    /// <summary>
    /// Blockstream's default PIN server public key (hex).
    /// </summary>
    public const string DefaultServerPublicKeyHex = "0325f3c5a0f77b0b7346a13dd8c29f6ea91e4c8e9ed69c2c78717ac4b6ec6c4d33";

    public async Task<string> ProcessRequestAsync(string endpoint, string? requestData, CancellationToken cancellationToken = default)
    {
        var url = _baseUrl + endpoint;

        // Forward request to remote PIN server
        var content = new StringContent(requestData, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
```

### Authentication Flow

**Code:** `jade-csharp-client/src/JadeClient/Protocol/JadeRpc.cs:171-248`

```csharp
public async Task<bool> AuthUserAsync(
    IPinServerHandler pinServerHandler,
    string network = "mainnet",
    CancellationToken cancellationToken = default)
{
    // 1. Send initial auth_user request to Jade
    var parameters = new Dictionary<string, object>
    {
        ["network"] = network,
        ["epoch"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    var response = await CallAsync("auth_user", parameters, interactiveTimeout, cancellationToken);

    // 2. Handle http_request loop (PIN server round-trips)
    while (response.HasHttpRequest)
    {
        var httpRequest = CborSerializer.ExtractHttpRequest(response.Result);

        // Extract endpoint from URL (e.g., "/get_pin")
        var endpoint = ExtractEndpoint(httpRequest.Urls.FirstOrDefault() ?? "");

        // 3. Forward to PIN server handler
        string pinServerResponse = await pinServerHandler.ProcessRequestAsync(
            endpoint,
            httpRequest.Data,
            cancellationToken);

        // 4. Parse response and send back to Jade
        var replyParams = JsonSerializer.Deserialize<Dictionary<string, object>>(pinServerResponse);

        // 5. Call the on-reply method ("pin") with server response
        response = await CallAsync(httpRequest.OnReply, replyParams, interactiveTimeout, cancellationToken);
    }

    // 6. Check final result
    return response.Result is bool success && success;
}
```

### Usage Examples

#### Scenario 1: Unlock with Default Blockstream Server

```csharp
// Create transport (serial port)
using var transport = new SerialTransport("/dev/ttyUSB0");

// Create RPC client
using var jade = new JadeRpc(transport);
await jade.ConnectAsync();

// Create PIN server handler (uses Blockstream's j8d.io by default)
using var pinServer = new RemotePinServerHandler();

// Authenticate - user enters PIN on device screen
bool success = await jade.AuthUserAsync(pinServer, "mainnet");

if (success)
{
    // Wallet is now unlocked!
    string xpub = await jade.GetXpubAsync("mainnet", new uint[] { 84 | 0x80000000, 0 | 0x80000000, 0 | 0x80000000 });
    Console.WriteLine($"Account xpub: {xpub}");
}
```

#### Scenario 2: Unlock with Custom PIN Server

```csharp
// Your custom PIN server
var customUrl = "https://my-pinserver.example.com";
var customPubkey = Convert.FromHexString("02abcd...");

using var pinServer = new RemotePinServerHandler(customUrl, customPubkey);

// First, update Jade's PIN server configuration
await jade.UpdatePinServerAsync(
    urlA: customUrl,
    urlB: "http://my-pinserver.onion",  // Optional Tor
    pubkey: customPubkey);

// Now authenticate
bool success = await jade.AuthUserAsync(pinServer, "mainnet");
```

### Complete Message Flow

```
┌────────────────┐     ┌────────────────┐     ┌────────────────┐
│  C# Client App │     │  Jade Device   │     │  PIN Server    │
└───────┬────────┘     └───────┬────────┘     └───────┬────────┘
        │                       │                       │
        │ auth_user(network)    │                       │
        │──────────────────────>│                       │
        │                       │                       │
        │                       │ [User enters PIN      │
        │                       │  on device screen]    │
        │                       │                       │
        │ http_request          │                       │
        │ (method=POST,         │                       │
        │  url=/get_pin,        │                       │
        │  data=base64(...))    │                       │
        │<──────────────────────│                       │
        │                       │                       │
        │ POST /get_pin                                 │
        │ {data: base64(cke||counter||encrypted)}       │
        │──────────────────────────────────────────────>│
        │                       │                       │
        │                       │     [Server verifies] │
        │                       │     [PIN secret hash] │
        │                       │                       │
        │ {data: base64(encrypted_aes_key)}             │
        │<──────────────────────────────────────────────│
        │                       │                       │
        │ pin(data=...)         │                       │
        │──────────────────────>│                       │
        │                       │                       │
        │                       │ [Device derives       │
        │                       │  KEY4_FINAL_AES,      │
        │                       │  decrypts wallet]     │
        │                       │                       │
        │ result: true          │                       │
        │<──────────────────────│                       │
        │                       │                       │
        │ [WALLET UNLOCKED]     │                       │
        │                       │                       │
```

---

## Protocol Details

### Protocol Version 2 (Current)

Protocol v2 improves on v1 by:
- Using BIP341 tweaking for session-specific server keys
- Eliminating the handshake phase
- Embedding anti-replay counter in the tweak

#### Request Format

```
HTTP POST /get_pin (or /set_pin)
Content-Type: text/plain

Base64(
    cke (33 bytes) ||
    replay_counter (4 bytes, little-endian) ||
    encrypted_payload (variable)
)

encrypted_payload = AES-256-CBC-ECDH(
    label = "blind_oracle_request",
    key = ECDH(KEY5, KEY9),
    plaintext = pin_secret (32) || entropy (0 or 32) || signature (65)
)
```

#### Response Format

```json
{
    "data": "Base64(encrypted_aes_key)"
}

encrypted_aes_key = AES-256-CBC-ECDH(
    label = "blind_oracle_response",
    key = ECDH(KEY9_private, KEY6),
    plaintext = HMAC-SHA256(KEY11, pin_secret) (32 bytes)
)
```

### Anti-Replay Protection

**Client-side:**
```c
// Load from NVS, increment after each successful request
uint32_t counter;
storage_get_replay_counter(&counter);
// ... use in request ...
storage_set_replay_counter(counter + 1);
```

**Server-side:**
```python
# Server stores replay_counter per device
# Rejects if client_counter <= stored_counter
def _check_v2_anti_replay(server_counter, client_counter):
    assert client_counter > server_counter
```

---

## Security Properties

### What the Server CANNOT Do

| Capability | Why Not |
|------------|---------|
| Learn the PIN | Only sees `SHA256(HMAC(HMAC(KEY1, 0x00), PIN))` - triple-derived |
| Decrypt stored data | Needs KEY2_PIN_PUBLIC (not stored, only hash) |
| Impersonate device | Doesn't have KEY1_PIN_PRIVATE for signatures |
| Read mnemonic | Never receives encrypted blob (on device only) |
| Correlate users | Each device has unique KEY2, lookup is hash |

### What the Device CANNOT Do (Alone)

| Capability | Why Not |
|------------|---------|
| Decrypt mnemonic offline | Needs KEY11_SERVER_AES from server |
| Bypass attempt limit | Server enforces independent counter |

### Attempt Counters

**Total: 6 attempts maximum**

| Location | Counter | Behavior |
|----------|---------|----------|
| Device NVS | 3 | Decremented before decrypt attempt, erases blob on 0 |
| Server DB | 3 | Incremented on wrong PIN, deletes record on 3 |

**Note:** These are independent. You could have 3 wrong attempts on server (data deleted there) but still have 3 attempts locally showing. The wallet would be permanently locked because server data is gone.

---

## Attack Scenarios

### 1. Device Theft Only

**Attacker has:** KEY1_PIN_PRIVATE, encrypted wallet blob

**Attacker needs:** Correct PIN + server cooperation

**Result:**
- 3 attempts via server (then server deletes data)
- Offline brute force: **IMPOSSIBLE** (needs KEY11 from server)

### 2. Server Compromise Only

**Attacker has:** Server database, KEY10_AES_PIN_DATA

**Attacker needs:** KEY2_PIN_PUBLIC to decrypt stored data

**Result:**
- Cannot decrypt any user's data
- KEY2 only recoverable from valid signature
- Valid signature needs KEY1 (on device only)

### 3. Device + Server Compromise

**Attacker has:** Everything except PIN

**Attack:** Offline brute force of 1M PIN combinations

**Mitigation:** Can check locally by:
1. Derive SECRET1 for each PIN guess
2. Compare SHA256(SECRET1) against stored hash

**Result:** If attacker has both, PIN is only protection. 6-digit PIN = 1M combinations, crackable in minutes.

### 4. Network MITM

**Attacker can:** Intercept HTTPS traffic

**Attacker cannot:**
- Decrypt ECDH payloads (needs ephemeral private keys)
- Replay requests (monotonic counter)
- Modify requests (HMAC verification)

**Protection layers:**
1. TLS/HTTPS
2. Additional ECDH encryption
3. Server public key hardcoded in firmware
4. Anti-replay counter

---

## Running Your Own PIN Server

### Quick Start

```bash
cd pinserver/

# 1. Setup Python environment
python3 -m venv venv
source venv/bin/activate
pip install --require-hashes -r requirements.txt

# 2. Generate server key pair
python -m generateserverkey
# Creates: server_private_key.key (SECRET!) and server_public_key.pub

# 3. Prepare storage directory
mkdir pins

# 4. Run server
python flaskserver.py
# Listens on http://localhost:8096
```

### Configure Jade Device

```python
from jadepy import JadeAPI

jade = JadeAPI.create_serial('/dev/ttyUSB0')
jade.connect()

# Read your server's public key
with open('server_public_key.pub', 'rb') as f:
    pubkey = f.read()

jade.make_rpc_call('update_pinserver', {
    'urlA': 'https://my-pinserver.example.com',
    'urlB': 'http://abc123.onion',  # Optional Tor
    'pubkey': pubkey.hex()
})

jade.disconnect()
```

### C# Configuration

```csharp
// Read server public key
byte[] pubkey = File.ReadAllBytes("server_public_key.pub");

// Update Jade configuration
await jade.UpdatePinServerAsync(
    urlA: "https://my-pinserver.example.com",
    urlB: "http://abc123.onion",
    pubkey: pubkey);

// Use custom handler for authentication
using var customPinServer = new RemotePinServerHandler(
    "https://my-pinserver.example.com",
    pubkey);

await jade.AuthUserAsync(customPinServer);
```

---

## PIN Server Migration and Configuration

This section covers when and how you can change your PIN server configuration, including firmware constraints and migration procedures.

### When Can You Change the PIN Server?

The Jade firmware enforces strict rules about PIN server changes to protect your wallet security:

| Wallet State | URL Changes | Public Key Changes |
|--------------|-------------|-------------------|
| **Before wallet setup** | ✅ Allowed | ✅ Allowed |
| **After wallet setup** | ✅ Allowed | ❌ Blocked |

**Key insight:** Once you've set up a wallet (registered a PIN with a server), you're locked to that server's public key. This is a security feature, not a bug.

### Firmware Constraints Explained

**Code Reference:** `main/process/update_pinserver.c:127-149`

```c
#ifndef CONFIG_DEBUG_MODE
    if (keychain_has_pin()) {
        // Check that we are not trying to update the pinserver pubkey on a Jade unit
        // that already has a wallet set up/persisted in flash.
        // NOTE: we do allow an update of just the url/certs, as this may be a url change
        // that still connects to the same backend pinserver instance.

        // Cannot reset a non-default pubkey to the default value
        if (reset_details && have_user_pubkey && memcmp(server_public_key_start, user_pubkey, sizeof(user_pubkey))) {
            *errmsg = "Cannot update initialized unit";
            goto cleanup;
        }

        // Cannot set new pubkey unless effectively unchanged
        const uint8_t* effective_pubkey = have_user_pubkey ? user_pubkey : server_public_key_start;
        if (pubkey && memcmp(effective_pubkey, pubkey, pubkey_len)) {
            *errmsg = "Cannot update initialized unit";
            goto cleanup;
        }
    }
#endif
```

**Why is this enforced?**

1. **Prevents accidental lockout:** If you could change the public key after initialization, you'd lose access to your wallet (the old server has your encrypted AES key component)
2. **Prevents phishing attacks:** A malicious app can't silently redirect you to a rogue server
3. **Maintains trust chain:** Your wallet is cryptographically bound to a specific server's key pair

**Technical constraints:**
- Maximum URL length: 120 characters
- Public key: Exactly 33 bytes (compressed secp256k1)
- User confirmation required on device screen for all changes

### Migration Scenarios

#### Scenario A: New Device Setup with Custom Server

**When:** Setting up a fresh Jade device (no wallet yet) with your own PIN server.

**Procedure:**
1. Connect to new Jade device
2. Configure custom PIN server (URL + public key)
3. Initialize wallet - it will register with YOUR server

```csharp
public async Task SetupNewDeviceWithCustomServer(JadeRpc jade)
{
    // 1. Verify device is not initialized
    var versionInfo = await jade.GetVersionInfoAsync();
    if (versionInfo.HasPin)
    {
        throw new InvalidOperationException(
            "Device already has a wallet. Cannot change PIN server public key.");
    }

    // 2. Your custom server details
    var customUrl = "https://my-pinserver.example.com";
    var customOnion = "http://abc123.onion";  // Optional Tor URL
    var customPubkey = Convert.FromHexString(
        "02abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890ab");

    // 3. Configure device to use custom server
    await jade.UpdatePinServerAsync(customUrl, customOnion, customPubkey);

    // 4. Now initialize wallet - will register with YOUR server
    using var pinServer = new RemotePinServerHandler(customUrl, customPubkey);

    // This triggers wallet creation on device with your server
    bool success = await jade.AuthUserAsync(pinServer, "mainnet");

    if (success)
    {
        Console.WriteLine("Wallet initialized with custom PIN server!");
    }
}
```

#### Scenario B: URL Change for Same Backend

**When:** Your PIN server's URL changed but it's the same backend (same private key).

**Example situations:**
- Domain name change
- Moving from HTTP to HTTPS
- Adding/changing a Tor onion address
- Load balancer or CDN migration

**Procedure:**
1. Ensure new URL points to same server (same private key!)
2. Update URL(s) on device
3. Continue using device normally

```csharp
public async Task UpdatePinServerUrls(JadeRpc jade)
{
    // New URLs pointing to SAME backend (same server private key)
    var newUrl = "https://new-domain.example.com";
    var newOnion = "http://newonion123.onion";

    // Get current configuration to preserve public key
    var versionInfo = await jade.GetVersionInfoAsync();

    // Option 1: Update URLs only (pubkey parameter omitted or same value)
    // This works because we're not changing the public key
    await jade.UpdatePinServerAsync(
        urlA: newUrl,
        urlB: newOnion,
        pubkey: null);  // null = keep existing pubkey

    Console.WriteLine("PIN server URLs updated successfully");

    // Verify it works with the new URL
    using var pinServer = new RemotePinServerHandler(newUrl);
    bool success = await jade.AuthUserAsync(pinServer, "mainnet");

    if (!success)
    {
        Console.WriteLine("Warning: Authentication failed with new URL");
        Console.WriteLine("Ensure the new URL serves the same backend!");
    }
}
```

**Important:** If the new URL points to a DIFFERENT server (different private key), authentication will fail silently - the server will return a junk key and your wallet won't decrypt.

#### Scenario C: Complete Migration (Requires Factory Reset)

**When:** You need to switch to a completely different PIN server (different public key).

**Warning:** This requires wiping your device and re-importing your wallet from seed backup.

**Procedure:**
1. **Backup your seed phrase!** (You did write it down, right?)
2. Unlock wallet with current PIN server
3. Factory reset the device
4. Configure new PIN server on fresh device
5. Restore wallet from seed phrase

```csharp
public async Task MigrateToNewPinServer(
    JadeRpc jade,
    string newServerUrl,
    byte[] newServerPubkey)
{
    // STEP 1: Verify user has their seed backup
    Console.WriteLine("WARNING: This will factory reset your device!");
    Console.WriteLine("Make sure you have your 12/24 word seed phrase backup.");
    Console.WriteLine("Press Enter to continue or Ctrl+C to abort...");
    Console.ReadLine();

    // STEP 2: Unlock current wallet (proves user has access)
    var currentVersion = await jade.GetVersionInfoAsync();
    if (!currentVersion.HasPin)
    {
        throw new InvalidOperationException("No wallet to migrate");
    }

    // Use current server to unlock
    using (var currentPinServer = new RemotePinServerHandler())
    {
        bool unlocked = await jade.AuthUserAsync(currentPinServer, "mainnet");
        if (!unlocked)
        {
            throw new InvalidOperationException("Failed to unlock current wallet");
        }
    }

    // STEP 3: Factory reset
    // Note: User must confirm on device screen
    Console.WriteLine("Please confirm factory reset on your Jade device...");
    // This is typically done through device menu, not API
    // await jade.FactoryResetAsync();  // If API available

    // STEP 4: After reset, configure new server
    // Device will reboot - need to reconnect
    await jade.DisconnectAsync();
    await Task.Delay(5000);  // Wait for reboot
    await jade.ConnectAsync();

    // Now device is fresh - can set any PIN server
    await jade.UpdatePinServerAsync(newServerUrl, null, newServerPubkey);

    // STEP 5: User restores wallet from seed
    Console.WriteLine("Device reset complete.");
    Console.WriteLine("Please restore your wallet using your seed phrase.");
    Console.WriteLine("The new PIN server is now configured.");

    // When user sets up wallet again, it will use the new server
    using var newPinServer = new RemotePinServerHandler(newServerUrl, newServerPubkey);
    // User enters seed and creates new PIN...
}
```

### Reset to Blockstream Defaults

To revert to Blockstream's default PIN server (`https://j8d.io`):

```csharp
public async Task ResetToBlockstreamDefaults(JadeRpc jade)
{
    // Check if device is initialized
    var versionInfo = await jade.GetVersionInfoAsync();

    if (versionInfo.HasPin)
    {
        // Device has wallet - check current pubkey
        // Can only reset to defaults if already using defaults
        // or if using custom URL with default pubkey

        // This will fail if using a custom pubkey
        try
        {
            await jade.ResetPinServerAsync();
            Console.WriteLine("Reset to Blockstream defaults successful");
        }
        catch (JadeException ex) when (ex.Message.Contains("Cannot update initialized unit"))
        {
            Console.WriteLine("Cannot reset: device uses a custom PIN server public key");
            Console.WriteLine("Factory reset required to change PIN servers");
        }
    }
    else
    {
        // No wallet - can freely reset
        await jade.ResetPinServerAsync();
        Console.WriteLine("Reset to Blockstream defaults successful");
    }
}
```

### Check Current Configuration

```csharp
public async Task DisplayPinServerConfig(JadeRpc jade)
{
    var versionInfo = await jade.GetVersionInfoAsync();

    Console.WriteLine("=== PIN Server Configuration ===");
    Console.WriteLine($"Wallet initialized: {versionInfo.HasPin}");

    // The version info includes PIN server details if custom
    if (versionInfo.PinServerUrl != null)
    {
        Console.WriteLine($"Custom URL: {versionInfo.PinServerUrl}");
    }
    else
    {
        Console.WriteLine("Using default: https://j8d.io");
    }

    if (versionInfo.PinServerOnion != null)
    {
        Console.WriteLine($"Tor URL: {versionInfo.PinServerOnion}");
    }

    // Note: For security, the public key may not be exposed via API
    // You would need to track this separately if using custom servers
}
```

### Migration Checklist

Before changing PIN server configuration:

- [ ] **Do I have my seed phrase backup?** (Critical for Scenario C)
- [ ] **Is my wallet initialized?** (Determines what changes are allowed)
- [ ] **Am I changing just the URL or also the public key?**
- [ ] **Is the new URL pointing to the same backend?** (For URL-only changes)
- [ ] **Do I have network access to both old and new servers?** (For migration)
- [ ] **Have I tested the new server works?** (Try with a test wallet first)

### Common Errors

| Error Message | Cause | Solution |
|--------------|-------|----------|
| `Cannot update initialized unit` | Trying to change pubkey after wallet setup | Factory reset or use URL-only change |
| `Invalid Oracle pubkey` | Public key not 33 bytes or invalid point | Check key format (compressed secp256k1) |
| `Empty or invalid first URL` | URL doesn't start with http:// or https:// | Fix URL protocol |
| `Cannot set pubkey without URL` | Provided pubkey but no URL | Include urlA parameter |
| `User declined to confirm Oracle details` | User rejected on device screen | User must approve changes |

### Security Recommendations

1. **Test with a dummy wallet first:** Before migrating your real wallet, test the process with a throwaway seed
2. **Keep your old server running:** If migrating URLs, keep both old and new URLs active during transition
3. **Document your server's public key:** Store it securely - you'll need it for client configuration
4. **Use HTTPS:** Always use TLS for your PIN server URL in production
5. **Consider Tor:** The onion URL provides privacy and censorship resistance

---

## Frequently Asked Questions

### Q1: Can I use Jade offline?

**Answer:** Only if already unlocked in current session. To unlock from cold start, you MUST have network connectivity to the PIN server.

### Q2: What if Blockstream's server goes down?

**Answer:** Your wallet is locked until:
- Blockstream restores service, OR
- You set up and configure your own PIN server (requires knowing the server was going to be unavailable beforehand - you need to configure a new server BEFORE you're locked out)

### Q3: Why not store the encryption key locally?

**Answer:** That would defeat the purpose. Local storage can be:
- Brute-forced offline (6-digit PIN = 1M combinations)
- Extracted via hardware attacks

The server provides rate limiting that cannot be bypassed locally.

### Q4: Is my PIN safe if the server is hacked?

**Answer:** Yes! The server only stores `SHA256(SHA256(HMAC(HMAC(KEY1, 0x00), PIN)))`. Even with the database, an attacker cannot:
- Reverse the hash to get PIN
- Brute force without KEY1 (on device)

### Q5: How many PIN attempts do I have?

**Answer:** Effectively 3. Although device and server each track separately:
- 3 wrong attempts on server = record deleted = permanent lockout
- 3 wrong attempts on device = blob erased = permanent lockout

Either one hitting 0 locks you out permanently.

### Q6: Can I change my PIN?

**Answer:** Yes, but it requires:
1. Authenticating with current PIN (get_pin)
2. Setting new PIN (set_pin creates new server record)
3. Re-encrypting wallet with new AES key

---

## Academic Foundations

### Primary Influences

1. **Two-Factor Signatures (FC 2019)**
   - Authors: Marcedone, Pass, shelat
   - Establishes theoretical foundation for device + server security model

2. **Anti-Exfiltration (Wuille, 2020)**
   - Bitcoin-dev mailing list overview
   - Jade implements Scheme #6 (most secure)

3. **OPAQUE Protocol (EUROCRYPT 2018)**
   - Authors: Jarecki, Krawczyk, Xu
   - Similar goals: protect low-entropy secrets, server cannot learn secret

### Cryptographic Primitives

| Primitive | Standard | Usage in Blind Oracle |
|-----------|----------|----------------------|
| ECDH | RFC 6090 | Session key derivation |
| BIP341 Tweak | Bitcoin BIP-341 | Session server key |
| AES-256-CBC | FIPS 197, NIST SP 800-38A | Payload encryption |
| HMAC-SHA256 | RFC 2104 | PIN secret derivation, integrity |
| ECDSA Recovery | SEC 1 | Server recovers client pubkey |

---

## Code References

### Client-Side (C Firmware)

| File | Purpose | Key Functions |
|------|---------|---------------|
| `main/process/pinclient.c` | PIN server client | `pinclient_get()`, `pinclient_set()` |
| `main/process/auth_user.c` | Authentication | `get_pin_get_aeskey()`, `set_pin_get_aeskey()` |
| `main/storage.c` | NVS operations | `storage_get_pin_privatekey()` |
| `main/keychain.c` | Key management | `keychain_init_unit_key()` |
| `main/aes.c` | AES crypto | `aes_encrypt_bytes()`, `aes_decrypt_bytes()` |

### Server-Side (Python)

| File | Purpose | Key Classes/Functions |
|------|---------|----------------------|
| `pinserver/server.py` | ECDH protocol | `PINServerECDHv2` |
| `pinserver/pindb.py` | Database ops | `PINDb.set_pin()`, `PINDb.get_aes_key()` |
| `pinserver/lib.py` | Crypto primitives | `E_ECDH`, `encrypt()`, `decrypt()` |
| `pinserver/flaskserver.py` | HTTP endpoints | Flask routes |

### C# Client

| File | Purpose | Key Classes/Methods |
|------|---------|---------------------|
| `JadeClient/Protocol/JadeRpc.cs` | RPC layer | `AuthUserAsync()` |
| `JadeClient/PinServer/IPinServerHandler.cs` | Interface | `ProcessRequestAsync()` |
| `JadeClient/PinServer/RemotePinServerHandler.cs` | HTTP proxy | Remote PIN server communication |

---

## Summary

The Jade Blind Oracle is a production-grade cryptographic protocol that provides:

- **Strong security**: Two-factor protection requiring device + server + PIN
- **Privacy**: Server is blind to PIN, wallet, and user identity
- **Enforced rate limiting**: 3 attempts maximum, no bypass possible
- **Open source**: Auditable code, self-hostable server

**Trade-off:** Network connectivity required for every unlock.

**Critical understanding:**
- PIN private key stored unencrypted = necessary for protocol bootstrap
- Server stores encrypted data = only client can trigger decryption
- Final AES key = combination of server key + PIN secret + raw PIN
- Neither party alone can derive the decryption key

The protocol achieves its security goals by ensuring that compromising any single component (device, server, or PIN) is insufficient to access the wallet.

---

*This document consolidates information from BLIND_ORACLE_PIN_SERVER.md, BLIND_ORACLE_PIN_SERVER_CORRECTED.md, and BLIND_ORACLE_RESEARCH_ORIGINS.md, verified against the actual source code implementation.*
