# Jade Blind Oracle PIN Server - Complete Guide

## Table of Contents
1. [Introduction](#introduction)
2. [What is a Blind Oracle](#what-is-a-blind-oracle)
3. [Is the PIN Server Always Used?](#is-the-pin-server-always-used)
4. [Architecture](#architecture)
5. [Cryptographic Protocol](#cryptographic-protocol)
6. [Implementation Details](#implementation-details)
7. [Code Walkthrough](#code-walkthrough)
8. [Security Analysis](#security-analysis)
9. [Running Your Own PIN Server](#running-your-own-pin-server)

---

## Introduction

Jade uses a **Blind Oracle** system (PIN server) to enforce a 3-attempt limit on PIN entry without storing the PIN itself. The server is completely "blind" to:
- The actual PIN value
- The wallet keys
- Any user data

This document explains how this cryptographic protocol works, when it's used, and provides detailed code examples.

---

## What is a Blind Oracle?

A **Blind Oracle** is a cryptographic protocol where:
1. The client needs to prove knowledge of a secret (the PIN)
2. The server helps verify the secret without ever learning it
3. The server can limit attempts without knowing the secret

### Key Properties

**Blindness**: The server never sees:
- Your PIN in plaintext
- Your encrypted wallet data
- Your actual secret values

**Rate Limiting**: The server enforces:
- Maximum 3 PIN attempts
- Counter must increment (anti-replay)
- Can't bypass by creating new "accounts"

**Privacy**: The server can't:
- Correlate requests to specific users
- Read your wallet data
- Link multiple requests together

---

## Is the PIN Server Always Used?

### **YES** - PIN Server is MANDATORY (Not Optional)

The PIN server is **always required** during authentication if you have encrypted wallet data stored on device.

**⚠️ CRITICAL: Your wallet is INACCESSIBLE without PIN server connectivity**

### What "When Available" Really Means

Documentation may say "when available" - this refers to **network connectivity**, NOT whether the feature is optional.

**The Reality:**
```
Encrypted Wallet + Network Available   = MUST use PIN server ✓
Encrypted Wallet + Network Unavailable = WALLET LOCKED ❌
```

### What Happens if Server is Unreachable?

**From pinclient.c and auth_user.c analysis:**

#### 1. **Initial Connection Failure** (pinclient.c:297-303)
```c
// If server doesn't respond properly
if (cberr != CborNoError || !cbor_value_is_valid(&params) || ...) {
    // Returns: PIN_CAN_RETRY
    RETURN_RESULT(PIN_CAN_RETRY, CBOR_RPC_BAD_PARAMETERS,
                  "Failed to read parameters from Oracle");
}
```

#### 2. **User is Prompted to Retry** (pinclient.c:518-524)
```c
if (pir.result == PIN_CAN_RETRY) {
    const char* question[] = { "Failed communicating", "with Oracle - retry ?" };
    if (await_yesno_activity("Network Error", question, 2, true, NULL)) {
        // User chooses "Yes" → Retry connection
        continue;
    }
    // User chooses "No" → Proceeds to failure
}
```

#### 3. **Authentication FAILS Completely** (pinclient.c:527-536)
```c
if (pir.result != PIN_SUCCESS && pir.result != PIN_CANCELLED) {
    JADE_LOGE("Failed to complete pinserver interaction");
    jade_process_reject_message(process, pir.errorcode, pir.message);

    const char* message[] = { "Network or server", "error" };
    await_error_activity(message, 2);
    return false;  // ❌ AUTHENTICATION FAILS
}
```

#### 4. **Wallet Remains Locked** (auth_user.c:210-214)
```c
if (!get_pin_get_aeskey(process, unlock_pin_msg, pin, sizeof(pin),
                        aeskey, sizeof(aeskey))) {
    // Server error or user abandoned
    // NOTE: reply message will have already been sent
    goto cleanup;  // ❌ WALLET STAYS LOCKED
}
```

### Why There's NO Fallback

**The AES decryption key is ONLY available from the PIN server:**

```
Device Storage:
├── Encrypted wallet data     ✓ (stored in flash)
├── PIN private key           ✓ (stored in flash)
├── PIN attempt counter       ✓ (stored locally)
└── AES decryption key        ❌ (NOT stored - must fetch from server!)

Decryption Flow:
User enters PIN → Contact server → Get AES key → Decrypt wallet
                      ↑ REQUIRED
                   (no alternative path)
```

**Without server response:**
- Device has encrypted wallet data ✓
- Device cannot derive decryption key ❌
- Wallet remains encrypted ❌
- **Device is effectively bricked until connectivity restored**

### When PIN Server is NOT Used

The PIN server is **bypassed** only in these specific scenarios:

#### 1. **Temporary Wallet Mode** (auth_user.c:382-403)
```python
# Python client - ephemeral wallet in RAM only
jade.set_mnemonic(
    mnemonic=words,
    temporary_wallet=True  # ← PIN server NOT used
)
# Wallet exists ONLY in RAM, cleared on restart
```

```c
// From auth_user.c
if (keychain_has_temporary()) {
    JADE_LOGI("using temporary keychain already present - skipping PIN step");
    // No PIN server interaction needed
    jade_process_reply_to_message_ok(process);
}
```

#### 2. **Already Unlocked in Current Session** (auth_user.c:406-409)
```c
if (keychain_has_pin()) {
    if (KEYCHAIN_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        JADE_LOGI("keychain already unlocked by this message-source");
        // No need to contact server again
        jade_process_reply_to_message_ok(process);
    }
}
```

#### 3. **First-Time Setup** (auth_user.c:420-439)
```c
if (!keychain_get()) {
    // Brand new device, no wallet exists yet
    JADE_LOGI("no wallet data, requesting mnemonic");
    initialise_with_mnemonic(...);

    // After mnemonic created:
    JADE_LOGI("requesting new pin");
    set_pin_save_keys(process);  // Registers with server using pinclient_set()
}
```

#### 4. **Debug/CI Testing Mode**
```c
// Configured in sdkconfig for automated testing
#ifdef CONFIG_DEBUG_UNATTENDED_CI
    // Auto-use test PIN, skip server interaction
    const uint8_t testpin[] = {0, 1, 2, 3, 4, 5};
#endif
```

### Default Configuration

```c
// main/process/pinclient.c:21-22
static const char PINSERVER_URL[] = "https://j8d.io";
static const char PINSERVER_ONION[] = "http://mrrxtq6t...onion";
```

**Blockstream's default server is always used unless explicitly configured otherwise.**

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Jade Device (Client)                      │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ User Secrets (Never Leave Device)                      │ │
│  │ - PIN: 6 digits (e.g., "123456")                       │ │
│  │ - PIN Private Key: 32 bytes random                     │ │
│  │ - Encrypted Wallet: AES-256 encrypted mnemonic        │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
│                          │ Derives                           │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ PIN Secret (32 bytes)                                  │ │
│  │ = HMAC-SHA256(PIN_PrivKey, PIN)                        │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
│                          │ Encrypts with ECDH               │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Encrypted Payload + Signature                          │ │
│  │ - PIN Secret (encrypted)                               │ │
│  │ - Random Entropy (for set_pin)                         │ │
│  │ - ECDSA Signature                                      │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
└──────────────────────────┼───────────────────────────────────┘
                           │ HTTPS POST
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                  PIN Server (Blind Oracle)                   │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Decrypts with ECDH                                     │ │
│  │ 1. Recovers Client Public Key from signature          │ │
│  │ 2. Verifies signature                                  │ │
│  │ 3. Looks up: Hash(ClientPubKey)                        │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Stored Data (per ClientPubKey hash):                   │ │
│  │ {                                                      │ │
│  │   "encrypted_data": AES(                              │ │
│  │       pin_secret_hash +                               │ │
│  │       wallet_aes_key +                                │ │
│  │       attempt_counter                                 │ │
│  │   ),                                                   │ │
│  │   "replay_counter": monotonic_counter                 │ │
│  │ }                                                      │ │
│  │                                                        │ │
│  │ Server CANNOT decrypt this without ClientPubKey!      │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
│                          │ Compares PIN secrets              │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ If Match:                                              │ │
│  │   - Return encrypted wallet_aes_key                   │ │
│  │   - Reset attempt counter                             │ │
│  │                                                        │ │
│  │ If Mismatch:                                           │ │
│  │   - Increment attempt counter                         │ │
│  │   - Return error                                      │ │
│  │   - If counter >= 3: Delete stored data              │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
└──────────────────────────┼───────────────────────────────────┘
                           │ Returns AES key (encrypted)
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    Jade Device (Client)                      │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Decrypts with ECDH                                     │ │
│  │ - Receives wallet_aes_key                              │ │
│  │ - Decrypts wallet from NVS storage                     │ │
│  │ - Loads keys into memory                               │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│              🔓 Wallet Unlocked!                            │
└─────────────────────────────────────────────────────────────┘
```

---

## Cryptographic Protocol

### Protocol Version 2 (Current)

#### Key Generation

**Client Side**:
```c
// 1. Generate ephemeral key pair
uint8_t client_privkey[32];
uint8_t client_pubkey[33];  // cke = Client Key Exchange
keychain_get_new_privatekey(client_privkey, 32);
ec_public_key_from_private_key(client_privkey, 32, client_pubkey, 33);

// 2. Get replay counter from storage
uint32_t replay_counter = storage_get_replay_counter();  // Monotonic

// 3. Derive session server public key via BIP341 tweak
uint8_t tweak[32] = SHA256(HMAC-SHA256(client_pubkey, replay_counter));
uint8_t session_server_pubkey[33] = BIP341_Tweak(
    static_server_pubkey,
    tweak
);
```

**Why Tweak?**
- Unique session key for each request
- Prevents replay attacks
- No handshake needed (Protocol v2 improvement)

#### PIN Secret Derivation

```c
// PIN Private Key is derived from wallet entropy (32 bytes)
// Stored in encrypted NVS, same as wallet keys

uint8_t pin_secret[32];
uint8_t hmac_key[32];

// Derive HMAC key from PIN private key
HMAC-SHA256(pin_privkey, subkey=0) → hmac_key

// Derive PIN secret from actual PIN digits
HMAC-SHA256(hmac_key, pin_digits) → pin_secret
```

**Properties**:
- Different for each wallet (different PIN privkey)
- Deterministic for same PIN + wallet
- Cannot reverse to get PIN from secret

#### Request Construction (get_pin / set_pin)

```c
// === PLAINTEXT PAYLOAD ===
struct Payload {
    pin_secret[32];        // HMAC of PIN
    entropy[32];           // Random (only for set_pin)
    signature[65];         // ECDSA recoverable signature
};

// === SIGNATURE ===
uint8_t msg_to_sign[32] = SHA256(
    client_pubkey ||
    replay_counter ||
    pin_secret ||
    entropy  // (if set_pin)
);

ECDSA_Sign(pin_privkey, msg_to_sign) → signature

// === ENCRYPTION ===
encrypted_payload = AES-256-CBC-ECDH(
    client_privkey,
    session_server_pubkey,
    label="blind_oracle_request",
    plaintext=Payload
);

// === REQUEST ===
POST /get_pin or /set_pin
{
    "cke": base64(client_pubkey),
    "encrypted": base64(encrypted_payload),
    "replay_counter": replay_counter
}
```

#### Server Processing

```python
# 1. Derive same session key
tweak = SHA256(HMAC-SHA256(client_pubkey, replay_counter))
session_privkey = BIP341_Tweak(server_static_privkey, tweak)

# 2. Decrypt payload
plaintext = AES_Decrypt_ECDH(
    session_privkey,
    client_pubkey,
    label="blind_oracle_request",
    encrypted_payload
)

# Extract: pin_secret, entropy, signature

# 3. Recover client public key from signature
msg_signed = SHA256(client_pubkey || replay_counter || pin_secret || entropy)
recovered_pubkey = ECDSA_Recover(msg_signed, signature)

# 4. Lookup database by hash
lookup_key = SHA256(recovered_pubkey)
stored_record = database[lookup_key]

# 5. Check anti-replay
assert client_replay_counter > stored_replay_counter

# 6. Decrypt stored data (blinded from server!)
storage_aes_key = HMAC-SHA256(server_aes_pin_key, recovered_pubkey)
decrypted_record = AES_Decrypt(storage_aes_key, stored_record)

# Extract: stored_pin_secret_hash, wallet_aes_key, attempt_counter

# 7. Verify PIN
if SHA256(pin_secret) == stored_pin_secret_hash:
    # Correct PIN
    attempt_counter = 0  # Reset
    response = wallet_aes_key
else:
    # Wrong PIN
    attempt_counter += 1
    if attempt_counter >= 3:
        database.delete(lookup_key)  # Lockout!
        raise Error("Too many attempts")
    response = Error("Wrong PIN")

# 8. Update database
updated_record = AES_Encrypt(
    storage_aes_key,
    stored_pin_secret_hash || wallet_aes_key || attempt_counter
)
database[lookup_key] = updated_record
database[lookup_key].replay_counter = client_replay_counter

# 9. Encrypt response
encrypted_response = AES_Encrypt_ECDH(
    session_privkey,
    client_pubkey,
    label="blind_oracle_response",
    wallet_aes_key  # or error
)

return {
    "data": base64(encrypted_response)
}
```

#### Client Receives Response

```c
// Decrypt response
uint8_t wallet_aes_key[32];
AES_Decrypt_ECDH(
    client_privkey,
    session_server_pubkey,
    label="blind_oracle_response",
    encrypted_response
) → wallet_aes_key

// Decrypt wallet from NVS
uint8_t encrypted_wallet[...];
storage_get_encrypted_blob("wallet", "keys", &encrypted_wallet, &len);

uint8_t wallet_data[...];
AES_256_GCM_Decrypt(wallet_aes_key, encrypted_wallet) → wallet_data

// Load into keychain
keychain_set(&wallet_data);

// ✅ Authenticated!
```

---

## Implementation Details

### Client-Side Files

| File | Purpose |
|------|---------|
| `main/process/pinclient.c` | PIN server client implementation |
| `main/process/auth_user.c` | Authentication handler (calls pinclient) |
| `main/storage.c` | Local storage for encrypted wallet & config |
| `main/keychain.c` | Key derivation and management |

### Server-Side Files (pinserver/)

| File | Purpose |
|------|---------|
| `pinserver/server.py` | Main PIN server logic (PINServerECDH) |
| `pinserver/pindb.py` | Database operations (FileStorage/RedisStorage) |
| `pinserver/lib.py` | ECDH cryptographic primitives |
| `pinserver/flaskserver.py` | HTTP/Flask web server |
| `pinserver/client.py` | Reference client implementation (Python) |

---

## Code Walkthrough

### Example 1: Setting a PIN (Client Side)

**File**: `main/process/auth_user.c:115-191`

```c
bool set_pin_get_aeskey(jade_process_t* process, const char* title,
                        uint8_t* pin, const size_t pin_len,
                        uint8_t* aeskey, const size_t aes_len)
{
    // 1. Get PIN from user (with confirmation)
    pin_insert_t pin_insert = { .initial_state = RANDOM, .pin_digits_shown = false };
    make_pin_insert_activity(&pin_insert, title, NULL);

    while (true) {
        // First entry
        if (!run_pin_entry_loop(&pin_insert)) {
            return false;  // User cancelled
        }
        memcpy(pin, pin_insert.pin, PIN_SIZE);

        // Confirm entry
        reset_pin(&pin_insert, "Confirm PIN");
        if (!run_pin_entry_loop(&pin_insert)) {
            continue;  // Retry
        }

        // Check pins match
        if (!sodium_memcmp(pin, pin_insert.pin, PIN_SIZE)) {
            break;  // Matched!
        }

        // Mismatch - show error and retry
        const char* message[] = { "Pin mismatch,", "please try again." };
        if (!await_continueback_activity(NULL, message, 2, true, NULL)) {
            return false;  // User abandoned
        }
    }

    // 2. Contact PIN server to set PIN
    display_message_activity((const char*[]){"Persisting PIN data..."}, 1);

    // pinclient_set will:
    // - Generate ephemeral keys
    // - Derive PIN secret
    // - Create signature
    // - Encrypt and send to server
    // - Receive and decrypt wallet AES key
    return pinclient_set(process, pin, pin_len, aeskey, aes_len);
}
```

### Example 2: PIN Client Set Operation

**File**: `main/process/pinclient.c:550-650` (simplified)

```c
bool pinclient_set(jade_process_t* process,
                   const uint8_t* pin, const size_t pin_len,
                   uint8_t* aeskey, const size_t aes_len)
{
    // 1. Generate ephemeral ECDH keys
    pin_keys_t pinkeys;
    pinserver_result_t result = generate_ephemeral_pinkeys(&pinkeys);
    if (result.result != PIN_SUCCESS) {
        return false;
    }

    // 2. Get PIN private key from keychain
    uint8_t pin_privatekey[EC_PRIVATE_KEY_LEN];
    if (!keychain_get_pin_privatekey(pin_privatekey, sizeof(pin_privatekey))) {
        return false;
    }

    // 3. Derive PIN secret from actual PIN digits
    uint8_t pin_secret[PIN_SECRET_LEN];
    if (!get_pin_secret(pin, pin_len, pin_privatekey,
                        sizeof(pin_privatekey),
                        pin_secret, sizeof(pin_secret))) {
        return false;
    }

    // 4. Generate random entropy (for blinding)
    uint8_t entropy[ENTROPY_LEN];
    get_random(entropy, sizeof(entropy));

    // 5. Sign: SHA256(cke || replay_counter || pin_secret || entropy)
    uint8_t sig[EC_SIGNATURE_RECOVERABLE_LEN];
    if (!sign_payload(pin_privatekey, sizeof(pin_privatekey),
                     &pinkeys, pin_secret, sizeof(pin_secret),
                     entropy, sizeof(entropy), sig, sizeof(sig))) {
        return false;
    }

    // 6. Encrypt payload: pin_secret || entropy || sig
    uint8_t encrypted[CLIENT_REQUEST_MAX_PAYLOAD_LEN];
    size_t encrypted_len;
    if (!encrypt_payload(&pinkeys, pin_secret, sizeof(pin_secret),
                        entropy, sizeof(entropy), sig, sizeof(sig),
                        encrypted, sizeof(encrypted), &encrypted_len)) {
        return false;
    }

    // 7. Assemble request: base64(cke || encrypted || replay_counter)
    char request_data[1024];
    if (!assemble_reply_data(&pinkeys, encrypted, encrypted_len,
                            request_data, sizeof(request_data))) {
        return false;
    }

    // 8. Send HTTP POST request to pinserver
    send_http_request_reply(process, PINSERVER_DOC_SET_PIN, request_data);

    // 9. Wait for response and decrypt wallet AES key
    result = handle_pin(process, &pinkeys, aeskey, aes_len);

    if (result.result == PIN_SUCCESS) {
        // 10. Increment replay counter for next request
        uint32_t new_counter;
        memcpy(&new_counter, pinkeys.replay_counter, sizeof(new_counter));
        new_counter++;
        storage_set_replay_counter(new_counter);
        return true;
    }

    return false;
}
```

### Example 3: Server Processing (Python)

**File**: `pinserver/pindb.py:190-270` (simplified)

```python
def set_pin(cke, encrypted_payload, replay_counter, aes_pin_data_key):
    """
    Set a new PIN for a device.

    Args:
        cke: Client ephemeral public key (33 bytes)
        encrypted_payload: Encrypted (pin_secret || entropy || signature)
        replay_counter: Anti-replay counter (4 bytes)
        aes_pin_data_key: Server's master key for encrypting stored data

    Returns:
        Encrypted wallet AES key
    """

    # 1. Decrypt payload using ECDH
    plaintext = decrypt_with_ecdh(cke, encrypted_payload, replay_counter)

    # 2. Extract fields: pin_secret, entropy, signature
    pin_secret, entropy, client_pubkey = extract_fields(
        cke, plaintext, replay_counter
    )

    # 3. Generate wallet AES key (random)
    wallet_aes_key = os.urandom(32)

    # 4. Compute lookup key (hash of client public key)
    pin_pubkey_hash = sha256(client_pubkey)

    # 5. Check if record already exists
    if PINDb.storage.exists(pin_pubkey_hash):
        raise Exception("PIN already set for this device")

    # 6. Store encrypted data
    #    Server can't decrypt without client_pubkey!
    hash_pin_secret = sha256(pin_secret)
    attempt_counter = 0

    PINDb._save_pin_fields(
        pin_pubkey_hash=pin_pubkey_hash,
        hash_pin_secret=hash_pin_secret,
        aes_key=wallet_aes_key,
        pin_pubkey=client_pubkey,
        aes_pin_data_key=aes_pin_data_key,
        count=attempt_counter,
        replay_counter=replay_counter
    )

    # 7. Return encrypted wallet AES key
    return wallet_aes_key  # Server will encrypt this for client
```

```python
def _save_pin_fields(pin_pubkey_hash, hash_pin_secret, aes_key,
                     pin_pubkey, aes_pin_data_key, count,
                     replay_counter=None):
    """
    Store PIN data in database (blinded from server).

    The stored data is encrypted with a key derived from pin_pubkey,
    which the server doesn't have. It can only decrypt when the client
    sends the correct public key (via signature recovery).
    """

    # Derive storage encryption key from client public key
    # Server has aes_pin_data_key, but needs pin_pubkey to decrypt
    storage_aes_key = hmac_sha256(aes_pin_data_key, pin_pubkey)

    # Pack data to encrypt
    count_bytes = struct.pack('B', count)
    plaintext = hash_pin_secret + aes_key + count_bytes

    # Add version and replay counter
    version_bytes = struct.pack('B', VERSION_SUPPORTED)
    if replay_counter:
        plaintext += replay_counter
        version_bytes = struct.pack('B', VERSION_LATEST)

    # Encrypt (AES-256-CBC)
    encrypted_data = encrypt(storage_aes_key, plaintext)

    # HMAC for integrity
    hmac_tag = hmac_sha256(aes_pin_data_key, encrypted_data)

    # Store: version || encrypted_data || hmac
    final_blob = version_bytes + encrypted_data + hmac_tag
    PINDb.storage.set(pin_pubkey_hash, final_blob)
```

### Example 4: Getting PIN (Server Verification)

**File**: `pinserver/pindb.py:140-188` (simplified)

```python
def get_pin(cke, encrypted_payload, replay_counter, aes_pin_data_key):
    """
    Verify PIN and return wallet AES key if correct.

    Returns:
        wallet_aes_key if PIN correct
        raises Exception if wrong PIN or locked out
    """

    # 1. Decrypt and extract fields
    plaintext = decrypt_with_ecdh(cke, encrypted_payload, replay_counter)
    pin_secret, entropy, client_pubkey = extract_fields(
        cke, plaintext, replay_counter
    )

    # 2. Lookup by hash
    pin_pubkey_hash = sha256(client_pubkey)
    stored_blob = PINDb.storage.get(pin_pubkey_hash)

    # 3. Extract stored data (encrypted!)
    version = stored_blob[0]
    encrypted_data = stored_blob[1:-32]  # Skip version and HMAC
    hmac_tag = stored_blob[-32:]

    # 4. Verify HMAC
    expected_hmac = hmac_sha256(aes_pin_data_key, encrypted_data)
    if not compare_digest(hmac_tag, expected_hmac):
        raise Exception("HMAC verification failed")

    # 5. Decrypt stored data (needs client_pubkey!)
    storage_aes_key = hmac_sha256(aes_pin_data_key, client_pubkey)
    plaintext = decrypt(storage_aes_key, encrypted_data)

    # Extract fields
    stored_pin_secret_hash = plaintext[0:32]
    wallet_aes_key = plaintext[32:64]
    attempt_counter = plaintext[64]

    if version == VERSION_LATEST:
        stored_replay_counter = plaintext[65:69]
        # Check anti-replay
        check_v2_anti_replay(stored_replay_counter, replay_counter)

    # 6. Verify PIN secret
    submitted_hash = sha256(pin_secret)

    if compare_digest(submitted_hash, stored_pin_secret_hash):
        # ✅ CORRECT PIN
        # Reset counter and return key
        PINDb._save_pin_fields(
            pin_pubkey_hash, stored_pin_secret_hash,
            wallet_aes_key, client_pubkey,
            aes_pin_data_key, count=0,  # Reset!
            replay_counter=replay_counter
        )
        return wallet_aes_key

    else:
        # ❌ WRONG PIN
        attempt_counter += 1

        if attempt_counter >= 3:
            # Lockout - delete record
            PINDb.storage.remove(pin_pubkey_hash)
            raise Exception("Too many failed attempts - record deleted")

        # Save incremented counter
        PINDb._save_pin_fields(
            pin_pubkey_hash, stored_pin_secret_hash,
            wallet_aes_key, client_pubkey,
            aes_pin_data_key, count=attempt_counter,
            replay_counter=replay_counter
        )

        remaining = 3 - attempt_counter
        raise Exception(f"Wrong PIN. {remaining} attempts remaining")
```

---

## Security Analysis

### What the Server CANNOT Do

1. **Cannot Learn the PIN**
   - Only sees `HMAC(HMAC(PIN_PrivKey), PIN)` - double-hashed
   - Cannot brute-force (rate limited by network, not useful anyway)

2. **Cannot Read Wallet Data**
   - Wallet AES key is stored encrypted with `HMAC(server_key, client_pubkey)`
   - Server doesn't have `client_pubkey` until signature recovery
   - Signature is valid only if client knows PIN private key

3. **Cannot Correlate Requests**
   - Each request uses unique ephemeral keys
   - Lookup key is `Hash(recovered_client_pubkey)` which changes per-wallet
   - No persistent user identifier

4. **Cannot Bypass Attempt Limits**
   - Attempts are tied to `Hash(client_pubkey)` derived from wallet
   - Can't create new "account" without new wallet (which needs PIN first)
   - Anti-replay counter prevents reusing old successful requests

### Attack Scenarios

#### 1. Server Compromise
**Impact**: Minimal
- Attacker gets encrypted blobs
- Cannot decrypt without client public keys (which aren't stored)
- Cannot brute-force PINs (need wallet's PIN private key)

#### 2. Network MITM
**Impact**: None
- Communication over HTTPS (TLS)
- Additionally encrypted with ECDH
- Server pubkey hardcoded in firmware

#### 3. Replay Attacks
**Impact**: Prevented
- Monotonic counter enforced
- Old requests rejected
- Each session uses unique derived server key

#### 4. Device Theft + Server Compromise
**Impact**: Minimal
- Attacker has: device (encrypted wallet) + server data (encrypted blob)
- Still needs: PIN (3 attempts, then lockout)
- No offline brute-force possible

### Why This Design?

1. **Privacy**: Server can't track users or read data
2. **Rate Limiting**: Enforce 3 attempts without local storage attacks
3. **Decentralization**: Easy to run your own server
4. **Simplicity**: No user accounts, registration, or passwords
5. **Resilience**: Works over Tor, can have multiple servers

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
# Creates:
# - server_private_key.key (KEEP SECRET!)
# - server_public_key.pub (distribute to clients)

# 3. Prepare storage directory
mkdir pinsdir

# 4. Run server
python flaskserver.py
# Listens on http://localhost:8096
```

### Docker Deployment

```bash
# Build image
docker build -f Dockerfile . -t my-pinserver

# Run server
docker run \
  -v $PWD/server_private_key.key:/server_private_key.key \
  -v $PWD/pinsdir:/pins \
  -p 8096:8096 \
  my-pinserver
```

### Tor Hidden Service

```bash
# 1. Install Tor
sudo apt install tor

# 2. Configure torrc
cat >> /etc/tor/torrc <<EOF
HiddenServiceDir /var/lib/tor/jade-pinserver/
HiddenServicePort 80 127.0.0.1:8096
EOF

# 3. Restart Tor
sudo systemctl restart tor

# 4. Get onion address
sudo cat /var/lib/tor/jade-pinserver/hostname
# Example: abc123...xyz.onion

# 5. Start PIN server
python flaskserver.py
```

### Configure Jade to Use Your Server

**Method 1: Python Script**

```python
from jadepy import JadeAPI

jade = JadeAPI.create_serial('/dev/ttyUSB0')
jade.connect()

# Set custom PIN server
urlA = "https://my-pinserver.example.com"
urlB = "http://abc123xyz.onion"  # Optional Tor

# Read your server's public key
with open('server_public_key.pub', 'rb') as f:
    pubkey = f.read()

jade.make_rpc_call('update_pinserver', {
    'urlA': urlA,
    'urlB': urlB,
    'pubkey': pubkey.hex()
})

jade.disconnect()
```

**Method 2: Using set_jade_pinserver.py**

```bash
# From Jade root directory
./set_jade_pinserver.py \
  --url https://my-pinserver.example.com \
  --onion http://abc123xyz.onion \
  --pubkey server_public_key.pub
```

### Storage Backends

#### File Storage (Default)
```python
# In pinserver/.env (or environment)
# Leave REDIS_HOST unset
```

Stores each PIN as separate file:
```
pinsdir/
├── a1b2c3d4e5f6...hex.pin
├── f6e5d4c3b2a1...hex.pin
└── ...
```

#### Redis Storage (Production)
```bash
# Install Redis
sudo apt install redis-server

# Configure
cat > pinserver/.env <<EOF
REDIS_HOST=localhost
REDIS_PORT=6379
REDIS_PASSWORD=your_secret_password
REDIS_HEALTH_CHECK_INTERVAL=25
EOF

# Run
python flaskserver.py
```

### Security Recommendations

1. **Secure the Private Key**
   ```bash
   chmod 600 server_private_key.key
   # Backup securely!
   ```

2. **Use HTTPS**
   ```bash
   # With nginx reverse proxy
   sudo apt install nginx certbot
   sudo certbot --nginx -d pinserver.example.com
   ```

3. **Rate Limiting**
   ```nginx
   # nginx config
   limit_req_zone $binary_remote_addr zone=pinlimit:10m rate=10r/m;

   location / {
       limit_req zone=pinlimit burst=5;
       proxy_pass http://localhost:8096;
   }
   ```

4. **Monitoring**
   ```bash
   # Log all requests
   tail -f /var/log/nginx/access.log | grep pinserver
   ```

---

## Testing the Protocol

### Test Client (Python)

```python
from pinserver.client import PINClientECDHv2
from pinserver.server import PINServerECDHv2
import os

# Server setup
server_privkey = os.urandom(32)
server_pubkey = ec_public_key_from_private_key(server_privkey)

# Client setup
pin = b"123456"
pin_privkey = os.urandom(32)
replay_counter = b'\x00\x00\x00\x01'

client = PINClientECDHv2(server_pubkey, replay_counter)

# Derive PIN secret
pin_secret = hmac_sha256(
    hmac_sha256(pin_privkey, b'\x00'),  # Derive HMAC key
    pin
)

# Generate entropy
entropy = os.urandom(32)

# Sign payload
msg = sha256(client.public_key + replay_counter + pin_secret + entropy)
signature = ec_sig_from_bytes(pin_privkey, msg, EC_FLAG_ECDSA | EC_FLAG_RECOVERABLE)

# Encrypt
payload = pin_secret + entropy + signature
encrypted = client.encrypt_request_payload(payload)

# === Server side ===
server = PINServerECDHv2(server_privkey, replay_counter)

# Decrypt
decrypted = server.decrypt_request_payload(
    client.public_key,
    encrypted
)

# Verify
assert decrypted == payload
print("✅ Encryption/decryption works!")

# Recover client pubkey from signature
recovered_pubkey = ec_sig_to_public_key(msg, signature)
print(f"✅ Signature verification works!")
print(f"   Client pubkey: {client.public_key.hex()}")
print(f"   Recovered:     {recovered_pubkey.hex()}")
```

### Integration Test

```bash
# 1. Start test server
cd pinserver/
python flaskserver.py &
SERVER_PID=$!

# 2. Configure Jade
./set_jade_pinserver.py \
  --url http://localhost:8096 \
  --pubkey server_public_key.pub

# 3. Run Jade tests
cd ..
python test_jade.py TestJade.test_auth_user

# 4. Cleanup
kill $SERVER_PID
```

---

## Debugging

### Enable Verbose Logging

**Client (Jade)**:
```c
// main/process/pinclient.c
#define JADE_LOG_LEVEL_DEBUG
JADE_LOGD("PIN secret: %s", pin_secret_hex);
JADE_LOGD("Encrypted payload len: %zu", encrypted_len);
```

**Server (Python)**:
```python
# pinserver/flaskserver.py
import logging
logging.basicConfig(level=logging.DEBUG)

# In code
print(f"Received cke: {cke.hex()}")
print(f"Decrypted payload: {plaintext.hex()}")
```

### Common Issues

1. **"Failed to decrypt payload"**
   - Check: Server pubkey matches on client
   - Check: Replay counter synced
   - Check: No MITM modifying requests

2. **"Wrong PIN" (but PIN is correct)**
   - Check: PIN private key loaded correctly
   - Check: PIN derivation consistent
   - Verify: `storage_get_pin_privatekey()` returns correct key

3. **"Too many attempts"**
   - Server has locked out after 3 failures
   - Must `erase_pinserver_details()` and `set_pin` again
   - Or wait for server record to expire (if implemented)

---

## Summary

The Blind Oracle PIN server is a clever cryptographic protocol that:

✅ **Always enforces 3-attempt limit** (when network available)
✅ **Never sees your PIN** in any form
✅ **Cannot read your wallet data** even if compromised
✅ **Provides privacy** - no user tracking
✅ **Easy to self-host** - no vendor lock-in
✅ **Works over Tor** - censorship resistant

**Used when**: Wallet exists and network available (default)
**Not used when**: No wallet yet, temporary wallet, or debug mode

The server is truly "blind" - it helps enforce security without learning secrets!

---

## References

- **Code**: `main/process/pinclient.c`, `pinserver/`
- **Protocol**: Blind Oracle ECDH with BIP341 tweaking
- **Server**: https://j8d.io (Blockstream default)
- **Spec**: See pinserver/README.md

**Questions? Check the source code - it's well-commented!** 🔐
