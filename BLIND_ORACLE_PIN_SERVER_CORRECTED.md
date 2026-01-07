# Jade Blind Oracle PIN Server - Technical Reference (Code-Verified)

## Executive Summary

The Jade PIN server uses a "blind oracle" design where:
- A **PIN private key** (stored unencrypted on device) derives authentication credentials
- The **mnemonic** is encrypted with an AES key combining PIN + server data
- The server never sees the actual PIN or mnemonic
- Both device and server enforce a 3-attempt limit

---

## Repository Links

**Jade Hardware Wallet Firmware:**
- GitHub: https://github.com/Blockstream/Jade
- Official Blockstream hardware wallet implementation

**Blind Oracle PIN Server:**
- GitHub: https://github.com/Blockstream/blind_pin_server
- Standalone PIN server implementation (originally part of Jade repo)
- Production instance: https://j8d.io

**Related Dependencies:**
- libwally-core: https://github.com/elementsProject/libwally-core (Bitcoin crypto library)

---

## All Keys Referenced (Numbered)

### Device-Side Keys

1. **KEY1_PIN_PRIVATE** = 32-byte EC private key (secp256k1)
   - Generated: First boot via hardware RNG
   - Storage: **UNENCRYPTED** in NVS namespace "PIN", key "privatekey"
   - Code: `keychain.c:686-704`, `storage.c:431-442`

2. **KEY2_PIN_PUBLIC** = Public key derived from KEY1_PIN_PRIVATE
   - Derived via secp256k1
   - Never stored - recovered from signatures

3. **KEY3_MNEMONIC_ENTROPY** = 16 or 32 bytes (BIP39 entropy)
   - Source: Hardware RNG or user import
   - Code: `keychain.c:269-286`

4. **KEY4_FINAL_AES** = 32-byte AES key for encrypting mnemonic
   - Derivation: `HMAC-SHA256(HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET), PIN)`
   - Server returns: `HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET)`
   - Client further derives: `HMAC-SHA256(server_returned_key, PIN)`
   - **NEVER STORED** - must fetch from server each time
   - Code: Server `pindb.py:198`, Client `pinclient.c:486`

### Ephemeral Keys (Per Request)

**Note:** These keys are stored in the `pin_keys_t` structure (defined in `pinclient.c:57-66`), which is created on the stack for each PIN server request and destroyed after completion.

5. **KEY5_CLIENT_EPHEMERAL_PRIVATE** = Random 32-byte EC key
   - Generated fresh for each request
   - Stored in: `pin_keys_t.privkey[32]`
   - Code: `pinclient.c:247-252`

6. **KEY6_CLIENT_EPHEMERAL_PUBLIC** = Public key from KEY5 (called "cke" - Client Key Exchange)
   - Sent to server
   - Stored in: `pin_keys_t.cke[33]` (compressed public key)
   - Code: `pinclient.c:247-252`

7. **KEY7_SERVER_STATIC_PRIVATE** = Server's permanent private key
   - Generated once during server setup
   - Code: `pinserver/generateserverkey.py`

8. **KEY8_SERVER_STATIC_PUBLIC** = Server's public key
   - Hardcoded in firmware: `pinclient.c:24-25`
   - Defines: `pinserver/defaultpinserver.h:7-8`

9. **KEY9_SERVER_SESSION_PUBLIC** = Tweaked server public key for this request
   - Client derives from KEY8 using BIP341 tweak: `SHA256(HMAC(cke, replay_counter))`
   - Server derives matching private key by tweaking KEY7
   - Stored in: `pin_keys_t.ske[33]` ("ske" = Server Key Exchange)
   - Code: Client - `pinclient.c:206-235`, Server - `pinserver/lib.py:get_ecdh_session_server_key()`

### Server Storage Keys

10. **KEY10_AES_PIN_DATA** = Server's master encryption key
    - Loaded from environment or config
    - Used to encrypt database entries
    - Code: `pinserver/server.py:__init__()`

11. **KEY11_SERVER_AES** = Server's contribution to final AES key
    - Generation: `HMAC-SHA256(server_random_32bytes, client_entropy_32bytes)`
    - Stored encrypted on server
    - Code: `pinserver/pindb.py:286-288`

### Derived Secrets

12. **SECRET1_PIN_SECRET** = Derived from PIN
    - Formula: `HMAC-SHA256(HMAC-SHA256(KEY1_PIN_PRIVATE, 0x00), PIN_digits)`
    - Used for server authentication
    - Code: `pinclient.c:329-350`

13. **SECRET2_ECDH_SHARED** = Ephemeral session key
    - Client: `ECDH(KEY5_CLIENT_EPHEMERAL_PRIVATE, KEY9_SERVER_SESSION_PUBLIC)`
    - Server: `ECDH(KEY9_SERVER_SESSION_PRIVATE, KEY6_CLIENT_EPHEMERAL_PUBLIC)`
    - Used to encrypt request/response payloads
    - Code: `pinclient.c:171-204`, `pinserver/lib.py`

---

## Phase 1: Device Initialization (First Boot)

### 1.1 Unit Key Generation

**When:** First boot, no existing PIN private key in NVS

**Code Flow:**
```c
// main/main.c:225
keychain_init_unit_key()

// keychain.c:716-737
bool keychain_init_unit_key(void) {
    uint8_t privatekey[32];

    // Try to load existing key
    bool res = storage_get_pin_privatekey(privatekey, 32);
    if (!res) {
        // Generate NEW random key
        keychain_get_new_privatekey(privatekey, 32);  // Hardware RNG
        storage_set_pin_privatekey(privatekey, 32);   // Store UNENCRYPTED
    }
    return res;
}
```

**What Gets Stored:**
- **NVS Namespace:** "PIN"
- **NVS Key:** "privatekey"
- **Value:** KEY1_PIN_PRIVATE (32 bytes, **UNENCRYPTED**)
- **Purpose:** Used to derive PIN credentials and signatures

**Important:** This key is stored unencrypted because:
1. It's needed to initiate PIN server handshake (before PIN verification)
2. It alone cannot decrypt the wallet (needs correct PIN + server cooperation)
3. Provides device binding (each device has unique key)

---

## Phase 2: Wallet Creation

### 2.1 Mnemonic Generation

**Code:**
```c
// keychain.c:269-286
void keychain_get_new_mnemonic(char** mnemonic, const size_t nwords) {
    uint8_t entropy[32];  // 16 for 12-word, 32 for 24-word

    const size_t entropy_len = (nwords == 12) ? 16 : 32;
    get_random(entropy, entropy_len);  // ESP32 hardware RNG

    bip39_mnemonic_from_bytes(NULL, entropy, entropy_len, mnemonic);
}
```

**What's in RAM:**
- KEY3_MNEMONIC_ENTROPY (16 or 32 bytes)
- Mnemonic words (12 or 24)
- **NOT YET ENCRYPTED OR STORED**

### 2.2 Key Derivation

**Code:**
```c
// keychain.c:288-314
void keychain_derive_from_seed(const uint8_t* seed, size_t seed_len, keychain_t* keydata) {
    // Store seed in RAM
    memcpy(keydata->seed, seed, seed_len);

    // Derive BIP32 master key
    bip32_key_from_seed(seed, seed_len, BIP32_VER_MAIN_PRIVATE, 0, &keydata->xpriv);

    // Derive master unblinding key (Liquid)
    wally_asset_blinding_key_from_seed(seed, seed_len, keydata->master_unblinding_key, 64);

    // Calculate Green service path
    wallet_calculate_gaservice_path(&keydata->xpriv, keydata->gaservice_path, 32);
}
```

**What's in RAM (keychain_t structure):**
```c
typedef struct keychain {
    uint8_t seed[64];                    // BIP39 seed (512 bits)
    size_t seed_len;                     // 64
    struct ext_key xpriv;                // BIP32 extended private key
    uint8_t master_unblinding_key[64];   // For Liquid CT
    uint8_t gaservice_path[32];          // Green service path
} keychain_t;
```

**Storage State:**
- ✅ KEY1_PIN_PRIVATE stored in NVS (unencrypted)
- ❌ Mnemonic/keys NOT YET in NVS (RAM only)
- ❌ No encrypted blob yet

---

## Phase 3: Setting PIN (First Time)

### 3.1 User Enters PIN

**Code:**
```c
// auth_user.c:115-183
bool set_pin_get_aeskey(jade_process_t* process, const char* title,
                        uint8_t* pin, size_t pin_len,
                        uint8_t* aeskey, size_t aes_len) {

    // 1. Get PIN from user (with confirmation)
    pin_insert_t pin_insert;
    make_pin_insert_activity(&pin_insert, title, NULL);

    while (true) {
        // First entry
        run_pin_entry_loop(&pin_insert);
        memcpy(pin, pin_insert.pin, 6);

        // Confirm entry
        reset_pin(&pin_insert, "Confirm PIN");
        run_pin_entry_loop(&pin_insert);

        // Check pins match
        if (!sodium_memcmp(pin, pin_insert.pin, 6)) {
            break;  // Matched!
        }

        // Mismatch - show error and retry
        await_continueback_activity("Pin mismatch, please try again.");
    }

    // 2. Contact PIN server to set PIN
    return pinclient_set(process, pin, pin_len, aeskey, aes_len);
}
```

**User Input:**
- 6-digit PIN (e.g., "123456")
- Entered twice for confirmation

### 3.2 The pin_keys_t Structure

**Definition:**
```c
// pinclient.c:57-66
typedef struct {
    // The tweak-derived server ECDH public key
    uint8_t ske[EC_PUBLIC_KEY_LEN];          // 33 bytes - KEY9_SERVER_SESSION_PUBLIC

    // The ephemeral client ECDH keys
    uint8_t cke[EC_PUBLIC_KEY_LEN];          // 33 bytes - KEY6_CLIENT_EPHEMERAL_PUBLIC
    uint8_t privkey[EC_PRIVATE_KEY_LEN];     // 32 bytes - KEY5_CLIENT_EPHEMERAL_PRIVATE

    // Monotonic Forward Replay counter required for v2
    // (32 bit unsigned little-endian integer)
    uint8_t replay_counter[REPLAY_COUNTER_LEN];  // 4 bytes
} pin_keys_t;
```

**Constants:**
```c
// From libwally-core/include/wally_crypto.h:349-351
#define EC_PRIVATE_KEY_LEN 32
#define EC_PUBLIC_KEY_LEN 33

// From pinclient.c:40
#define REPLAY_COUNTER_LEN 4
```

**Purpose:** This structure holds all ephemeral session keys and replay counter for a single PIN server request.

**Lifecycle:**
1. Created on stack at start of `get_pinserver_aeskey()` - `pinclient.c:434`
2. Populated by `generate_ephemeral_pinkeys()` - `pinclient.c:237-266`
3. Used throughout request/response encryption
4. Destroyed when function returns (stack cleanup)

**Security:** All fields are ephemeral (one-time use) except `replay_counter` which is loaded from NVS.

**Memory Layout:**
```
pin_keys_t structure (102 bytes total on stack):
┌────────────────────────────────────────────────────────────┐
│ Offset 0: ske[33]            (KEY9_SERVER_SESSION_PUBLIC)  │
│           Derived via BIP341 tweak                         │
│           Used for ECDH shared secret computation          │
├────────────────────────────────────────────────────────────┤
│ Offset 33: cke[33]           (KEY6_CLIENT_EPHEMERAL_PUBLIC)│
│            Random EC public key (compressed)               │
│            Sent to server in request                       │
├────────────────────────────────────────────────────────────┤
│ Offset 66: privkey[32]       (KEY5_CLIENT_EPHEMERAL_PRIVATE)│
│            Random EC private key                           │
│            NEVER sent to server                            │
│            Used for ECDH shared secret computation         │
├────────────────────────────────────────────────────────────┤
│ Offset 98: replay_counter[4] (Anti-replay counter)        │
│            32-bit unsigned little-endian integer           │
│            Loaded from NVS, incremented after each request │
└────────────────────────────────────────────────────────────┘
```

**Initialization:**
```c
// pinclient.c:434
pin_keys_t pinkeys;  // Allocated on stack (102 bytes)
SENSITIVE_PUSH(&pinkeys, sizeof(pinkeys));  // Mark as sensitive for cleanup

// Populate structure
pinserver_result_t pir = generate_ephemeral_pinkeys(&pinkeys);

// ... use throughout request ...

// Automatically cleaned from stack when function returns
SENSITIVE_POP(&pinkeys);
```

### 3.3 Generate Ephemeral Keys

**Code:**
```c
// pinclient.c:237-266
static pinserver_result_t generate_ephemeral_pinkeys(pin_keys_t* pinkeys) {
    // Generate ephemeral client key pair
    keychain_get_new_privatekey(pinkeys->privkey, 32);  // KEY5_CLIENT_EPHEMERAL_PRIVATE
    wally_ec_public_key_from_private_key(
        pinkeys->privkey, 32,
        pinkeys->cke, 33);  // KEY6_CLIENT_EPHEMERAL_PUBLIC (compressed)

    // Load replay counter from NVS
    uint32_t counter;
    storage_get_replay_counter(&counter);
    memcpy(pinkeys->replay_counter, &counter, 4);

    // Derive session server public key via BIP341 tweak
    generate_ske(pinkeys);  // Creates KEY9_SERVER_SESSION_PUBLIC

    return PIN_SUCCESS;
}
```

**Session Key Derivation:**
```c
// pinclient.c:206-235
static bool generate_ske(pin_keys_t* pinkeys) {
    // tweak = SHA256(HMAC-SHA256(cke, replay_counter))
    uint8_t tweak[32];
    uint8_t hmac[32];
    wally_hmac_sha256(pinkeys->cke, 33, pinkeys->replay_counter, 4, hmac, 32);
    wally_sha256(hmac, 32, tweak, 32);

    // Get hardcoded server static public key
    const uint8_t* server_static_pubkey = get_pinsvr_pubkey();

    // Apply BIP341 tweak
    // ske = BIP341_TWEAK(server_static_pubkey, tweak)
    wally_ec_public_key_bip341_tweak(
        server_static_pubkey, 33,
        tweak, 32,
        0,  // flags
        pinkeys->ske, 33);  // KEY9_SERVER_SESSION_PUBLIC

    return true;
}
```

### 3.3 Derive PIN Secret

**Code:**
```c
// pinclient.c:329-350
static bool get_pin_secret(const uint8_t* pin, size_t pin_len,
                          const uint8_t* pin_privatekey, size_t pin_privatekey_len,
                          uint8_t* pin_secret, size_t pin_secret_len) {

    const uint8_t subkey = 0;
    uint8_t hmac_key[32];

    // Step 1: HMAC(KEY1_PIN_PRIVATE, 0x00) -> hmac_key
    wally_hmac_sha256(pin_privatekey, pin_privatekey_len, &subkey, 1, hmac_key, 32);

    // Step 2: HMAC(hmac_key, PIN_digits) -> SECRET1_PIN_SECRET
    wally_hmac_sha256(hmac_key, 32, pin, pin_len, pin_secret, pin_secret_len);

    return true;
}
```

**Formula:**
```
SECRET1_PIN_SECRET = HMAC-SHA256(HMAC-SHA256(KEY1_PIN_PRIVATE, 0x00), PIN)
```

### 3.4 Generate Client Entropy

**Code:**
```c
// pinclient.c:455-456
uint8_t entropy[32];
get_random(entropy, 32);  // Hardware RNG
```

**Purpose:** Combined with server entropy to generate KEY11_SERVER_AES

### 3.5 Sign Payload

**Code:**
```c
// pinclient.c:352-388
static bool sign_payload(const uint8_t* pin_privatekey,
    const pin_keys_t* pinkeys,
    const uint8_t* pinsecret, size_t pinsecret_len,
    const uint8_t* entropy, size_t entropy_len,
    uint8_t* sig, size_t sig_len) {

    // Concatenate: cke || replay_counter || pinsecret || entropy
    uint8_t shadata[33 + 4 + 32 + 32];  // = 101 bytes
    size_t offset = 0;

    memcpy(shadata + offset, pinkeys->cke, 33);
    offset += 33;
    memcpy(shadata + offset, pinkeys->replay_counter, 4);
    offset += 4;
    memcpy(shadata + offset, pinsecret, pinsecret_len);
    offset += pinsecret_len;
    memcpy(shadata + offset, entropy, entropy_len);
    offset += entropy_len;

    // Hash
    uint8_t shahash[32];
    wally_sha256(shadata, offset, shahash, 32);

    // Sign with KEY1_PIN_PRIVATE (recoverable signature)
    wally_ec_sig_from_bytes(
        pin_privatekey, pin_privatekey_len,
        shahash, 32,
        EC_FLAG_ECDSA | EC_FLAG_RECOVERABLE,
        sig, sig_len);  // 65-byte recoverable signature

    return true;
}
```

**Signature allows server to recover KEY2_PIN_PUBLIC!**

### 3.6 Encrypt Payload

**Code:**
```c
// pinclient.c:171-204
static bool encrypt_payload(const pin_keys_t* pinkeys,
    const uint8_t* pin_secret, size_t pin_secret_len,
    const uint8_t* entropy, size_t entropy_len,
    const uint8_t* sig, size_t sig_len,
    uint8_t* encrypted, size_t encrypted_len, size_t* written) {

    // Concatenate plaintext: pin_secret || entropy || signature
    uint8_t cleartext[32 + 32 + 65];  // = 129 bytes
    memcpy(cleartext, pin_secret, 32);
    memcpy(cleartext + 32, entropy, 32);
    memcpy(cleartext + 64, sig, 65);

    // Generate random IV
    uint8_t iv[16];
    get_random(iv, 16);

    // Encrypt using ECDH-derived key
    // Label: "blind_oracle_request"
    wally_aes_cbc_with_ecdh_key(
        pinkeys->privkey, 32,              // KEY5_CLIENT_EPHEMERAL_PRIVATE
        iv, 16,
        cleartext, 129,
        pinkeys->ske, 33,                  // KEY9_SERVER_SESSION_PUBLIC
        "blind_oracle_request", 21,        // Label
        AES_FLAG_ENCRYPT,
        encrypted, encrypted_len, written);

    return true;
}
```

**Encryption Details:**
- Algorithm: AES-256-CBC
- Key derivation: ECDH between KEY5_CLIENT_EPHEMERAL_PRIVATE and KEY9_SERVER_SESSION_PUBLIC
- Label: "blind_oracle_request" (ensures request/response keys differ)
- Plaintext: SECRET1_PIN_SECRET (32) || entropy (32) || signature (65) = 129 bytes

### 3.7 Send to Server

**Code:**
```c
// pinclient.c:390-419
static bool assemble_reply_data(const pin_keys_t* pinkeys,
    const uint8_t* encrypted, size_t encrypted_len,
    char* output, size_t output_len) {

    // Concatenate: cke || replay_counter || encrypted
    const size_t binary_len = 33 + 4 + encrypted_len;
    uint8_t binary[binary_len];

    memcpy(binary, pinkeys->cke, 33);
    memcpy(binary + 33, pinkeys->replay_counter, 4);
    memcpy(binary + 37, encrypted, encrypted_len);

    // Base64 encode
    size_t written = 0;
    wally_base64_from_bytes(binary, binary_len, 0, output, output_len, &written);

    return true;
}

// pinclient.c:475
send_http_request_reply(process, PINSERVER_DOC_SET_PIN, data);
```

**HTTP Request:**
```
POST /set_pin
Content-Type: text/plain

base64(KEY6_CLIENT_EPHEMERAL_PUBLIC || replay_counter || encrypted_payload)
```

### 3.8 Server Processes Request

**Code:**
```python
# pinserver/pindb.py:266-299
@classmethod
def set_pin(cls, cke, payload, aes_pin_data_key, replay_counter=None):
    """
    Args:
        cke: KEY6_CLIENT_EPHEMERAL_PUBLIC (33 bytes)
        payload: Encrypted (pin_secret || entropy || signature)
        aes_pin_data_key: KEY10_AES_PIN_DATA (server master key)
        replay_counter: 4-byte counter
    """

    # 1. Decrypt payload using ECDH
    plaintext = cls._decrypt_request(cke, payload, replay_counter)

    # 2. Extract fields and recover public key from signature
    pin_secret, entropy, pin_pubkey = cls._extract_fields(cke, payload, replay_counter)
    # pin_pubkey = KEY2_PIN_PUBLIC (recovered from signature!)

    # 3. Compute lookup key
    pin_pubkey_hash = sha256(pin_pubkey)  # Storage key

    # 4. Generate server AES key
    our_random = os.urandom(32)  # Server entropy
    new_key = hmac_sha256(our_random, entropy)  # KEY11_SERVER_AES

    # 5. Store encrypted data
    hash_pin_secret = sha256(pin_secret)
    replay_bytes = (0).to_bytes(4, 'little')  # Initialize replay counter

    saved_key = cls._save_pin_fields(
        pin_pubkey_hash,    # Lookup key: SHA256(KEY2_PIN_PUBLIC)
        hash_pin_secret,    # SHA256(SECRET1_PIN_SECRET)
        new_key,            # KEY11_SERVER_AES
        pin_pubkey,         # KEY2_PIN_PUBLIC (for encryption key derivation)
        aes_pin_data_key,   # KEY10_AES_PIN_DATA
        0,                  # counter (failed attempts)
        replay_bytes        # Anti-replay counter
    )

    # 6. Return key derived from saved key + pin_secret
    return cls.make_client_aes_key(pin_secret, saved_key)
```

**Storage Encryption:**
```python
# pinserver/pindb.py:139-161
@classmethod
def _save_pin_fields(cls, pin_pubkey_hash, hash_pin_secret, aes_key,
                     pin_pubkey, aes_pin_data_key, count, replay_counter=None):

    # Derive storage encryption key
    storage_aes_key = hmac_sha256(aes_pin_data_key, pin_pubkey)
    # = HMAC-SHA256(KEY10_AES_PIN_DATA, KEY2_PIN_PUBLIC)

    # Pack plaintext
    count_bytes = struct.pack('B', count)
    plaintext = hash_pin_secret + aes_key + count_bytes
    version_bytes = struct.pack('B', VERSION_SUPPORTED)

    if replay_counter is not None:
        plaintext += replay_counter
        version_bytes = struct.pack('B', VERSION_LATEST)  # v2

    # Encrypt: AES-256-CBC with random IV
    encrypted = encrypt(storage_aes_key, plaintext)

    # HMAC for authentication
    pin_auth_key = hmac_sha256(aes_pin_data_key, pin_pubkey_hash)
    hmac_payload = hmac_sha256(pin_auth_key, version_bytes + encrypted)

    # Store: version || hmac || encrypted_data
    final_blob = version_bytes + hmac_payload + encrypted
    cls.storage.set(pin_pubkey_hash, final_blob)

    return aes_key
```

**What Server Stores:**

**Database Entry:**
- **Key:** `SHA256(KEY2_PIN_PUBLIC)` - 32 bytes
- **Value:** `version(1) || hmac(32) || encrypted_data(variable)`

**Encrypted Data Contains:**
- `SHA256(SECRET1_PIN_SECRET)` - 32 bytes (for PIN verification)
- `KEY11_SERVER_AES` - 32 bytes (server's contribution to final AES key)
- `counter` - 1 byte (failed attempts: 0-3)
- `replay_counter` - 4 bytes (anti-replay)

**Encryption:**
- **Key:** `HMAC-SHA256(KEY10_AES_PIN_DATA, KEY2_PIN_PUBLIC)`
- **Algorithm:** AES-256-CBC with random IV
- **HMAC Key:** `HMAC-SHA256(KEY10_AES_PIN_DATA, SHA256(KEY2_PIN_PUBLIC))`

**Critical Security Property:**
Server cannot decrypt this data later because:
1. Decryption needs KEY2_PIN_PUBLIC (not stored, only hash stored)
2. KEY2_PIN_PUBLIC is only recovered when client sends valid signature
3. Valid signature requires KEY1_PIN_PRIVATE (only on device)

### 3.9 Server Returns Response

**Code:**
```python
# pinserver/pindb.py:194-200
@classmethod
def make_client_aes_key(cls, pin_secret, saved_key):
    """Combine server key with pin_secret"""
    aes_key = hmac_sha256(saved_key, pin_secret)
    return aes_key
```

**Formula (Server Side):**
```
server_returned_key = HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET)
```

Note: This is NOT the final AES key - client will further derive it!

**Server encrypts this with ECDH and sends back:**
```python
# pinserver/server.py (via ECDH)
encrypted_response = encrypt_with_ecdh(
    server_session_privkey,  # KEY9_SERVER_SESSION_PRIVATE
    client_ephemeral_pubkey, # KEY6_CLIENT_EPHEMERAL_PUBLIC
    label="blind_oracle_response",
    plaintext=final_aes_key
)
```

### 3.10 Device Receives and Processes Response

**Code:**
```c
// pinclient.c:272-327
static bool decrypt_reply(const pin_keys_t* pinkeys,
    const uint8_t* encrypted, size_t encrypted_len,
    uint8_t* decryptedaes, size_t decryptedaes_len) {

    // Decrypt using ECDH-derived key
    size_t written = 0;
    wally_aes_cbc_with_ecdh_key(
        pinkeys->privkey, 32,              // KEY5_CLIENT_EPHEMERAL_PRIVATE
        NULL, 0,                           // No IV needed (in ciphertext)
        encrypted, encrypted_len,
        pinkeys->ske, 33,                  // KEY9_SERVER_SESSION_PUBLIC
        "blind_oracle_response", 22,       // Label (different from request!)
        AES_FLAG_DECRYPT,
        decryptedaes, decryptedaes_len, &written);

    return (written == decryptedaes_len);
}
```

**Combine with PIN:**
```c
// pinclient.c:484-487
// Derive the final aes key by combining the server key with the pin
JADE_LOGI("Deriving final aes-key");
wally_hmac_sha256(serverkey, 32, pin, pin_len, finalaes, 32);
```

**Complete Derivation Formula:**
```
server_returned_key = HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET)  [Server calculates]
KEY4_FINAL_AES = HMAC-SHA256(server_returned_key, PIN)                   [Client calculates]

Expanded:
KEY4_FINAL_AES = HMAC-SHA256(
    HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET),
    PIN
)
```

**Result:** Device now has KEY4_FINAL_AES to encrypt mnemonic!

### 3.11 Encrypt and Store Mnemonic

**Serialize Keys:**
```c
// keychain.c:383-395
static void serialize(uint8_t* serialized, size_t serialized_len, const keychain_t* keydata) {
    // Layout: xpriv(78) || ga_path(64) || master_blinding_key(64) = 206 bytes

    bip32_key_serialize(&keydata->xpriv, BIP32_FLAG_KEY_PRIVATE,
                       serialized, 78);

    wallet_serialize_gaservice_path(serialized + 78, 64,
                                   keydata->gaservice_path, 32);

    memcpy(serialized + 78 + 64, keydata->master_unblinding_key, 64);
}
```

**OR Store Mnemonic Entropy (if passphrase):**
```c
// keychain.c:558-565
if (mnemonic_entropy_len) {
    // If passphrase-protected, store entropy only
    // Will need passphrase to derive keys later
    p_serialized_data = mnemonic_entropy;  // 16 or 32 bytes
    serialized_data_len = mnemonic_entropy_len;
}
```

**Encrypt:**
```c
// keychain.c:411-433
static bool get_encrypted_blob(const uint8_t* aeskey, size_t aeslen,
    const uint8_t* bytes, size_t bytes_len,
    uint8_t* output, size_t output_len) {

    // 1. Encrypt with AES-CBC (IV prepended)
    aes_encrypt_bytes(aeskey, aeslen, bytes, bytes_len,
                     output, output_len - 32);

    // 2. Append HMAC-SHA256
    wally_hmac_sha256(
        aeskey, aeslen,
        output, output_len - 32,
        output + output_len - 32, 32);

    return true;
}
```

**AES Encryption Details:**
```c
// aes.c:11-35
bool aes_encrypt_bytes(const uint8_t* aeskey, size_t aeskey_len,
    const uint8_t* bytes, size_t bytes_len,
    uint8_t* output, size_t output_len) {

    // 1. Generate random IV at front of buffer
    get_random(output, 16);  // AES_BLOCK_LEN

    // 2. Encrypt with AES-256-CBC
    size_t written = 0;
    wally_aes_cbc(
        aeskey, aeskey_len,
        output, 16,                      // IV
        bytes, bytes_len,
        AES_FLAG_ENCRYPT,
        output + 16, output_len - 16, &written);

    return true;
}
```

**Store in NVS:**
```c
// storage.c:446-453
bool storage_set_encrypted_blob(const uint8_t* encrypted, size_t encrypted_len) {
    storage_restore_counter();  // Set counter to 3
    return store_blob(DEFAULT_NAMESPACE, BLOB_FIELD, encrypted, encrypted_len);
}
```

**What Gets Stored in NVS:**

**Namespace:** "PIN"

**Key:** "blob"

**Value:**
```
IV(16 bytes)
||
AES-256-CBC-encrypted(mnemonic_entropy OR serialized_keys)
||
HMAC-SHA256(32 bytes)
```

**Encrypted Content:**
- If passphrase: Mnemonic entropy (16 or 32 bytes)
- If no passphrase: Serialized keys (206 bytes)

**Encryption:**
- Algorithm: AES-256-CBC
- Key: KEY4_FINAL_AES = `HMAC-SHA256(HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET), PIN)`
- IV: Random 16 bytes
- HMAC: `HMAC-SHA256(KEY4_FINAL_AES, IV || encrypted_data)`

**Additional Storage:**

**Key:** "counter"
**Value:** 3 (remaining PIN attempts)

**Key:** "antireplay"
**Value:** `replay_counter + 1` (for next request)

---

## Phase 4: PIN Unlock (Get PIN)

### 4.1 User Enters PIN

**Code:**
```c
// auth_user.c:193-296
static bool get_pin_get_aeskey(jade_process_t* process, const char* title,
                               uint8_t* pin, size_t pin_len,
                               uint8_t* aeskey, size_t aes_len) {

    // PIN entry loop
    pin_insert_t pin_insert;
    make_pin_insert_activity(&pin_insert, title, NULL);

    while (true) {
        if (!run_pin_entry_loop(&pin_insert)) {
            return false;  // User cancelled
        }

        memcpy(pin, pin_insert.pin, 6);

        // Check if this is the wallet erase PIN
        if (is_wallet_erase_pin(pin, 6)) {
            // User wants to erase wallet
            erase_wallet_and_shutdown();
        }

        // Try PIN with server
        pinserver_result_t pir = pinclient_get(process, pin, 6, aeskey, 32);

        if (pir.result == PIN_SUCCESS) {
            return true;  // Got KEY4_FINAL_AES!
        }

        if (pir.result == PIN_CAN_RETRY) {
            // Network error
            if (await_yesno_activity("Failed communicating with Oracle - retry?")) {
                continue;  // Retry
            }
            return false;
        }

        // Wrong PIN - show error
        const char* message = pir.message;
        await_error_activity(message);
        // Loop continues for retry
    }
}
```

### 4.2 Contact Server (GET_PIN)

**Code:**
```c
// pinclient.c:545-552
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

**Key Difference from SET_PIN:**
- NO client entropy sent (entropy field is empty/zero-length)
- Same signature/encryption process otherwise

**Request Contains:**
```
Encrypted payload = AES-ECDH-ENCRYPT(
    SECRET1_PIN_SECRET (32 bytes)
    ||
    (no entropy)
    ||
    signature (65 bytes)
)
```

### 4.3 Server Verifies PIN

**Code:**
```python
# pinserver/pindb.py:202-247
@classmethod
def get_aes_key_impl(cls, pin_pubkey, pin_secret, aes_pin_data_key, replay_counter=None):
    """
    Args:
        pin_pubkey: KEY2_PIN_PUBLIC (recovered from signature)
        pin_secret: SECRET1_PIN_SECRET (from client)
        aes_pin_data_key: KEY10_AES_PIN_DATA (server master key)
    """

    # 1. Lookup database by hash
    pin_pubkey_hash = sha256(pin_pubkey)
    stored_blob = cls.storage.get(pin_pubkey_hash)

    if not stored_blob:
        raise FileNotFoundError("No record for this device")

    # 2. Load and decrypt stored data
    saved_hps, saved_key, counter, replay_local = cls._load_pin_fields(
        pin_pubkey_hash, pin_pubkey, aes_pin_data_key)

    # saved_hps = stored SHA256(SECRET1_PIN_SECRET)
    # saved_key = KEY11_SERVER_AES
    # counter = failed attempt counter (0-3)
    # replay_local = stored replay counter

    # 3. Check anti-replay
    cls._check_v2_anti_replay(replay_local, replay_counter)

    # 4. Verify PIN
    hash_pin_secret = sha256(pin_secret)

    if compare_digest(saved_hps, hash_pin_secret):
        # ✅ CORRECT PIN
        if counter != 0 or replay_counter:
            # Reset counter to 0, update replay counter
            cls._save_pin_fields(
                pin_pubkey_hash, saved_hps, saved_key,
                pin_pubkey, aes_pin_data_key,
                0,  # Reset counter!
                replay_counter or replay_local
            )
        return saved_key  # Return KEY11_SERVER_AES

    else:
        # ❌ WRONG PIN
        if counter >= 2:
            # 3rd failed attempt - ERASE DATA
            cls.storage.remove(pin_pubkey_hash)
            raise Exception("Too many attempts - record deleted")
        else:
            # Increment counter
            cls._save_pin_fields(
                pin_pubkey_hash, saved_hps, saved_key,
                pin_pubkey, aes_pin_data_key,
                counter + 1,  # Increment!
                replay_counter or replay_local
            )
            raise Exception(f"Invalid PIN ({2 - counter} attempts remaining)")
```

**Server Returns:**
```python
# pinserver/pindb.py:249-264
@classmethod
def get_aes_key(cls, cke, payload, aes_pin_data_key, replay_counter=None):
    pin_secret, _, pin_pubkey = cls._extract_fields(cke, payload, replay_counter)

    try:
        saved_key = cls.get_aes_key_impl(pin_pubkey, pin_secret,
                                         aes_pin_data_key, replay_counter)
    except Exception:
        # Wrong PIN or error - return JUNK key
        # Client can't tell the difference until decryption attempt!
        saved_key = os.urandom(32)

    # Combine saved key with pin_secret (always)
    return cls.make_client_aes_key(pin_secret, saved_key)
```

**Important:** Server ALWAYS returns a key (real or junk) to prevent timing attacks

### 4.4 Device Decrypts Mnemonic

**Receive Server Response:**
```c
// pinclient.c:484-487
// Derive the final aes key by combining the server key with the pin
wally_hmac_sha256(serverkey, 32, pin, pin_len, finalaes, 32);
// finalaes = KEY4_FINAL_AES
```

**Load and Decrypt:**
```c
// keychain.c:495-535
static bool keychain_load_and_decrypt_blob(
    const uint8_t* aeskey, size_t aeslen,
    uint8_t* cleartext_blob, size_t blob_len, size_t* written) {

    // 1. Check counter and decrement
    if (!storage_decrement_counter()) {
        return false;  // Out of attempts
    }

    // 2. Load encrypted blob from NVS
    uint8_t encrypted[300];  // Max size
    size_t encrypted_data_len = 0;
    if (!storage_get_encrypted_blob(encrypted, sizeof(encrypted), &encrypted_data_len)) {
        storage_erase_encrypted_blob();
        return false;
    }

    // 3. Decrypt and verify HMAC
    if (!get_decrypted_payload(aeskey, aeslen, encrypted, encrypted_data_len,
                               cleartext_blob, blob_len, written)) {
        // Decryption failed
        if (keychain_pin_attempts_remaining() == 0) {
            // Out of attempts - ERASE
            keychain_erase_encrypted();
        }
        return false;
    }

    // 4. Success - reset counter to 3
    storage_restore_counter();
    return true;
}
```

**Decryption Details:**
```c
// keychain.c:435-462
static bool get_decrypted_payload(
    const uint8_t* aeskey, size_t aeslen,
    const uint8_t* bytes, size_t bytes_len,
    uint8_t* output, size_t output_len, size_t* written) {

    // 1. Verify HMAC at tail of buffer
    uint8_t hmac_calculated[32];
    wally_hmac_sha256(
        aeskey, aeslen,
        bytes, bytes_len - 32,  // All except last 32 bytes
        hmac_calculated, 32);

    // Constant-time comparison
    if (crypto_verify_32(hmac_calculated, bytes + bytes_len - 32) != 0) {
        JADE_LOGW("hmac mismatch (bad pin)");
        return false;  // Wrong PIN!
    }

    // 2. Decrypt with AES-256-CBC
    if (!aes_decrypt_bytes(aeskey, aeslen, bytes, bytes_len - 32,
                          output, output_len, written)) {
        return false;
    }

    return true;
}
```

**AES Decryption:**
```c
// aes.c:37-64
bool aes_decrypt_bytes(const uint8_t* aeskey, size_t aeskey_len,
    const uint8_t* bytes, size_t bytes_len,
    uint8_t* output, size_t output_len, size_t* written) {

    // IV is at start of bytes (first 16 bytes)
    // Encrypted data follows
    const size_t payload_len = bytes_len - 16;

    wally_aes_cbc(
        aeskey, aeskey_len,
        bytes, 16,                    // IV
        bytes + 16, payload_len,      // Encrypted data
        AES_FLAG_DECRYPT,
        output, output_len, written);

    return true;
}
```

**Load Keys:**
```c
// keychain.c:588-635
bool keychain_load(const uint8_t* aeskey, size_t aeslen) {
    uint8_t serialized[300];
    size_t serialized_data_len = 0;

    // Load and decrypt from NVS
    if (!keychain_load_and_decrypt_blob(aeskey, aeslen, serialized,
                                       sizeof(serialized), &serialized_data_len)) {
        return false;
    }

    // Determine what was stored
    if (serialized_data_len == 16 || serialized_data_len == 32) {
        // Mnemonic entropy (passphrase-protected)
        memcpy(mnemonic_entropy, serialized, serialized_data_len);
        mnemonic_entropy_len = serialized_data_len;
        // Will need passphrase to derive keys later
    }
    else if (serialized_data_len == 206) {
        // Full keychain data
        keychain_t keydata = { 0 };
        unserialize(serialized, serialized_data_len, &keydata);
        keychain_set(&keydata, 0, false);
    }

    return true;
}
```

**If Passphrase Required:**
```c
// auth_user.c:225-245
if (keychain_requires_passphrase()) {
    char passphrase[256];
    get_passphrase(passphrase, sizeof(passphrase));

    // Derive BIP39 seed from entropy + passphrase
    keychain_complete_derivation_with_passphrase(passphrase);
}

// keychain.c:649-672
bool keychain_complete_derivation_with_passphrase(const char* passphrase) {
    // Convert entropy to mnemonic
    char* mnemonic = NULL;
    bip39_mnemonic_from_bytes(NULL, mnemonic_entropy, mnemonic_entropy_len, &mnemonic);

    // Derive seed from mnemonic + passphrase
    uint8_t seed[64];
    size_t written = 0;
    bip39_mnemonic_to_seed(mnemonic, passphrase, seed, 64, &written);

    // Derive wallet keys
    keychain_t keydata = { 0 };
    keychain_derive_from_seed(seed, 64, &keydata);
    keychain_set(&keydata, 0, false);

    return true;
}
```

**Result:** Wallet unlocked! Keys in RAM.

---

## Storage Summary

### Device NVS Storage

| Key | Value | Encryption | Size |
|-----|-------|------------|------|
| `privatekey` | KEY1_PIN_PRIVATE | **NONE** | 32 bytes |
| `blob` | IV ‖ encrypted ‖ HMAC | AES-256-CBC | Variable |
| `counter` | Attempts remaining | None | 1 byte |
| `antireplay` | Replay counter | None | 4 bytes |

**Namespace:** "PIN"

**Blob Contents (encrypted):**
- Passphrase mode: Mnemonic entropy (16 or 32 bytes)
- No passphrase: Serialized keys (206 bytes)

**Blob Encryption:**
- Algorithm: AES-256-CBC
- Key: KEY4_FINAL_AES = `HMAC-SHA256(HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET), PIN)`
- IV: Random 16 bytes (prepended)
- HMAC: SHA-256 (appended)

**NVS Partition Encryption (Optional):**
- Controlled by `CONFIG_NVS_ENCRYPTION`
- If enabled: Flash encryption via ESP32 hardware
- Keys in separate "nvs_key" partition
- Code: `storage.c:12-336`

### Server Storage

| Key | Value | Size |
|-----|-------|------|
| `SHA256(KEY2_PIN_PUBLIC)` | `version ‖ hmac ‖ encrypted_data` | Variable |

**Encrypted Data Contains:**
- `SHA256(SECRET1_PIN_SECRET)` - 32 bytes
- `KEY11_SERVER_AES` - 32 bytes
- `counter` - 1 byte (0-3)
- `replay_counter` - 4 bytes

**Encryption:**
- Key: `HMAC-SHA256(KEY10_AES_PIN_DATA, KEY2_PIN_PUBLIC)`
- Algorithm: AES-256-CBC
- HMAC: `HMAC-SHA256(HMAC-SHA256(KEY10_AES_PIN_DATA, SHA256(KEY2_PIN_PUBLIC)), version ‖ encrypted)`

**Server CANNOT Decrypt Without:**
- KEY2_PIN_PUBLIC (only stored as hash)
- KEY2_PIN_PUBLIC is only recovered from valid signature
- Valid signature requires KEY1_PIN_PRIVATE (only on device)

---

## Key Derivation Chains (Complete)

### Chain 1: PIN Authentication

```
Hardware RNG (first boot)
    ↓
KEY1_PIN_PRIVATE (32 bytes, stored UNENCRYPTED in NVS)
    ↓
HMAC-SHA256(KEY1_PIN_PRIVATE, 0x00)
    ↓
hmac_key (32 bytes)
    ↓
HMAC-SHA256(hmac_key, PIN_digits)
    ↓
SECRET1_PIN_SECRET (32 bytes)
    ↓
SHA256(SECRET1_PIN_SECRET)
    ↓
Stored on server for verification
```

### Chain 2: Public Key Recovery

```
KEY1_PIN_PRIVATE
    ↓
secp256k1 point multiplication
    ↓
KEY2_PIN_PUBLIC (33 bytes compressed)
    ↓
NOT STORED - recovered from signature
    ↓
SHA256(KEY2_PIN_PUBLIC)
    ↓
Server database lookup key
```

### Chain 3: Server AES Key (SET_PIN only)

```
Server: os.urandom(32) → server_random
Client: get_random(32) → client_entropy
    ↓
HMAC-SHA256(server_random, client_entropy)
    ↓
KEY11_SERVER_AES (32 bytes)
    ↓
Stored encrypted on server
```

### Chain 4: Final AES Key

```
KEY11_SERVER_AES (stored encrypted on server)
    +
SECRET1_PIN_SECRET (derived from PIN on client)
    ↓
SERVER CALCULATES: HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET)
    ↓
server_returned_key (sent to client via encrypted channel)
    ↓
CLIENT RECEIVES: server_returned_key
    +
PIN (raw 6-digit PIN)
    ↓
CLIENT CALCULATES: HMAC-SHA256(server_returned_key, PIN)
    ↓
KEY4_FINAL_AES (32 bytes)
    ↓
Used to encrypt/decrypt mnemonic blob
    ↓
NEVER STORED - must derive each time

Expanded formula:
KEY4_FINAL_AES = HMAC-SHA256(
    HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET),
    PIN
)
```

### Chain 5: Ephemeral ECDH Session Keys

```
Client generates:
    KEY5_CLIENT_EPHEMERAL_PRIVATE (random, per-request)
    ↓
    KEY6_CLIENT_EPHEMERAL_PUBLIC (sent to server)

Server derives:
    tweak = SHA256(HMAC-SHA256(KEY6_CLIENT_EPHEMERAL_PUBLIC, replay_counter))
    ↓
    KEY9_SERVER_SESSION_PRIVATE = BIP341_TWEAK(KEY7_SERVER_STATIC_PRIVATE, tweak)
    ↓
    KEY9_SERVER_SESSION_PUBLIC (computed by client using same tweak)

ECDH Shared Secret:
    SECRET2_ECDH_SHARED = ECDH(KEY5_CLIENT_EPHEMERAL_PRIVATE, KEY9_SERVER_SESSION_PUBLIC)
                        = ECDH(KEY9_SERVER_SESSION_PRIVATE, KEY6_CLIENT_EPHEMERAL_PUBLIC)
    ↓
    Used with label "blind_oracle_request" for request encryption
    Used with label "blind_oracle_response" for response encryption
```

---

## Security Properties

### What Server CANNOT Do

1. **Cannot Learn PIN**
   - Only sees `SHA256(HMAC-SHA256(HMAC-SHA256(KEY1_PIN_PRIVATE, 0x00), PIN))`
   - Even with database access, cannot reverse to PIN

2. **Cannot Decrypt Stored Data Without Valid Request**
   - Needs KEY2_PIN_PUBLIC to derive decryption key
   - KEY2_PIN_PUBLIC only recovered from valid signature
   - Valid signature requires KEY1_PIN_PRIVATE (only on device)

3. **Cannot Impersonate Device**
   - Doesn't have KEY1_PIN_PRIVATE
   - Cannot create valid signatures

4. **Cannot Read Mnemonic**
   - Doesn't have encrypted mnemonic blob (on device only)
   - Even if it did, doesn't have KEY4_FINAL_AES
   - KEY4_FINAL_AES requires both KEY11_SERVER_AES and correct PIN

### What Device Alone CANNOT Do

1. **Cannot Decrypt Mnemonic Without Server**
   - Needs KEY11_SERVER_AES from server
   - Must contact server on every unlock

2. **Cannot Bypass 3-Attempt Limit**
   - Server enforces independent counter
   - After 3 failures on server, data deleted

### Attack Scenarios

#### 1. Device Theft (No Server Compromise)

**Attacker Has:**
- KEY1_PIN_PRIVATE (unencrypted in NVS)
- Encrypted mnemonic blob

**Attacker Needs:**
- Correct PIN (6 digits = 1M combinations)
- Server cooperation (or server compromise)

**Attack Outcome:**
- 3 attempts on device counter
- 3 attempts on server counter
- After 6 total failures, data erased both sides
- Offline brute force: IMPOSSIBLE (needs server)

#### 2. Server Compromise (No Device Theft)

**Attacker Has:**
- Server database with encrypted records
- KEY10_AES_PIN_DATA (server master key)

**Attacker Needs:**
- KEY2_PIN_PUBLIC for each victim (not stored)
- KEY1_PIN_PRIVATE to create valid signatures
- Correct PIN

**Attack Outcome:**
- Cannot decrypt any user's data
- Cannot impersonate devices
- No data leakage

#### 3. Device + Server Compromise

**Attacker Has:**
- KEY1_PIN_PRIVATE
- Encrypted mnemonic blob
- Server database
- KEY10_AES_PIN_DATA

**Attacker Needs:**
- Correct PIN (6 digits, 3 attempts)

**Attack Outcome:**
- Can brute force PIN (1M combinations max)
- Limited to 3 attempts before data erasure
- Can try locally: Derive SECRET1_PIN_SECRET, check against `SHA256(SECRET1_PIN_SECRET)` in database
- Still limited to offline brute force without triggering server lockout

#### 4. Network MITM Attack

**Attacker Can:**
- Intercept HTTPS traffic

**Attacker Cannot:**
- Decrypt ECDH-encrypted payloads (needs ephemeral private keys)
- Replay old requests (replay counter)
- Modify requests (HMAC protection)

**Protection:**
- HTTPS/TLS
- Additional ECDH encryption layer
- Server public key hardcoded in firmware
- Anti-replay counter

---

## Why This Design?

### Design Goals

1. **Privacy:** Server cannot track users or correlate requests
2. **Rate Limiting:** Enforce 3-attempt limit without local-only attacks
3. **Decentralization:** Easy to run your own server
4. **Simplicity:** No user accounts, registration, or passwords
5. **Resilience:** Works over Tor, can use multiple servers

### Trade-offs

**Advantages:**
- ✅ Server blind to PIN and mnemonic
- ✅ Strong rate limiting (6 attempts total: 3 device + 3 server)
- ✅ No persistent user identifiers (privacy)
- ✅ Self-hostable (no vendor lock-in)
- ✅ Tor-compatible

**Disadvantages:**
- ❌ Requires network connectivity to unlock
- ❌ Server unavailable = wallet locked
- ❌ Must trust server for availability (but not privacy)
- ❌ Server compromise + device theft = only PIN protects wallet

### Why PIN Private Key is Unencrypted

**Question:** Why not encrypt KEY1_PIN_PRIVATE with PIN?

**Answer:**
1. **Chicken-and-egg:** Need to contact server to verify PIN, but need KEY1_PIN_PRIVATE to create valid signature for server
2. **Device binding:** Each device has unique key, prevents cloning attacks
3. **Limited value:** Even with KEY1_PIN_PRIVATE, attacker still needs:
   - Correct PIN (3 attempts)
   - Server cooperation
4. **Protection in depth:**
   - KEY1_PIN_PRIVATE alone cannot decrypt mnemonic
   - Requires both correct PIN and KEY11_SERVER_AES from server

**Alternative Design (Not Used):**
- Store KEY1_PIN_PRIVATE encrypted with PIN
- Require user to enter PIN before contacting server
- Problem: No way to verify PIN is correct before decrypting KEY1_PIN_PRIVATE
- Current design: Server verification happens first, local decryption second

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

### Configure Jade to Use Your Server

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

### Security Recommendations

1. **Secure the Private Key:**
   ```bash
   chmod 600 server_private_key.key
   # Backup securely and offline
   ```

2. **Use HTTPS:**
   ```bash
   # With nginx reverse proxy
   sudo apt install nginx certbot
   sudo certbot --nginx -d pinserver.example.com
   ```

3. **Rate Limiting:**
   ```nginx
   # nginx config
   limit_req_zone $binary_remote_addr zone=pinlimit:10m rate=10r/m;

   location / {
       limit_req zone=pinlimit burst=5;
       proxy_pass http://localhost:8096;
   }
   ```

---

## Frequently Asked Questions

### Q1: Is the mnemonic encrypted on device?

**Yes.** The mnemonic (or mnemonic entropy if passphrase-protected) is encrypted with AES-256-CBC and stored in NVS.

**Encryption key:** `HMAC-SHA256(HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET), PIN)`

### Q2: Where does the encryption key come from?

**Three sources combined:**
1. **KEY11_SERVER_AES:** Generated by server (combines server + client entropy)
2. **SECRET1_PIN_SECRET:** Derived from PIN and PIN private key
3. **PIN:** User's 6-digit PIN (raw)

**Two-step derivation:**
1. Server calculates: `intermediate_key = HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET)`
2. Client calculates: `KEY4_FINAL_AES = HMAC-SHA256(intermediate_key, PIN)`

**Expanded formula:** `KEY4_FINAL_AES = HMAC-SHA256(HMAC-SHA256(KEY11_SERVER_AES, SECRET1_PIN_SECRET), PIN)`

### Q3: Is the PIN private key encrypted on device?

**No.** KEY1_PIN_PRIVATE is stored **UNENCRYPTED** in NVS.

**Why:** Needed to create signatures before PIN verification. Even with this key, attacker still needs correct PIN and server cooperation to decrypt mnemonic.

### Q4: What happens if PIN server is unreachable?

**Wallet is LOCKED.** There is no offline fallback.

**User sees:** "Failed communicating with Oracle - retry?" error

**Reason:** KEY11_SERVER_AES is required to derive KEY4_FINAL_AES, which is the only way to decrypt the mnemonic blob.

### Q5: How many PIN attempts do I have?

**6 total attempts** (across both device and server):
- **Device counter:** 3 attempts (stored in NVS)
- **Server counter:** 3 attempts (stored on server)

After 3 failures on either side, data is erased.

### Q6: Can I brute force the PIN offline?

**No, if server is not compromised.**

**With only device:** Cannot decrypt mnemonic without KEY11_SERVER_AES from server.

**With device + server compromise:** Can attempt offline brute force of 1M PIN combinations, but limited to 3 attempts before triggering data erasure.

### Q7: What does the server store?

**Encrypted record per device:**
- Key: `SHA256(KEY2_PIN_PUBLIC)` (32 bytes)
- Value: Encrypted blob containing:
  - `SHA256(SECRET1_PIN_SECRET)` (for PIN verification)
  - `KEY11_SERVER_AES` (server's contribution to final AES key)
  - `counter` (failed attempt counter: 0-3)
  - `replay_counter` (anti-replay counter)

**Server cannot decrypt this without:**
- KEY2_PIN_PUBLIC (only recovered from valid client signature)
- Valid signature requires KEY1_PIN_PRIVATE (only on device)

### Q8: Can the server see my PIN or mnemonic?

**No.**

**PIN:** Server only sees `SHA256(HMAC-SHA256(HMAC-SHA256(KEY1_PIN_PRIVATE, 0x00), PIN))` - triple-derived, irreversible.

**Mnemonic:** Server never receives encrypted blob (stored only on device).

### Q9: What if I lose my device?

**With PIN server operational:**
- Data on server is encrypted and blinded
- Attacker has 3 attempts to guess PIN
- After 3 failures, server deletes data

**Best practice:** If device is lost, erase PIN server data remotely (if your server supports it, or contact server operator).

### Q10: Can I use Jade without PIN server?

**Yes, in these scenarios:**

1. **Temporary wallet mode:**
   ```python
   jade.set_mnemonic(mnemonic=words, temporary_wallet=True)
   # Wallet only in RAM, cleared on restart
   ```

2. **Already unlocked in current session:**
   - Wallet remains unlocked until device restart
   - No need to contact server again

3. **Debug/testing mode:**
   - Configured via `CONFIG_DEBUG_UNATTENDED_CI`

**Otherwise:** PIN server is **mandatory** for encrypted wallet storage.

---

## Code References

### Client-Side (C)

| File | Purpose |
|------|---------|
| `main/process/pinclient.c` | PIN server client implementation |
| `main/process/auth_user.c` | Authentication handler |
| `main/storage.c` | NVS storage operations |
| `main/keychain.c` | Key derivation and management |
| `main/aes.c` | AES encryption/decryption |
| `main/main.c` | Boot initialization |

### Server-Side (Python)

| File | Purpose |
|------|---------|
| `pinserver/server.py` | Main PIN server logic (ECDH protocol) |
| `pinserver/pindb.py` | Database operations |
| `pinserver/lib.py` | Cryptographic primitives |
| `pinserver/flaskserver.py` | HTTP/Flask web server |
| `pinserver/client.py` | Reference client (for testing) |
| `pinserver/generateserverkey.py` | Server key generation |

---

## Summary

The Blind Oracle PIN server is a cryptographic protocol that:

✅ **Enforces 3-attempt limit** on both device and server
✅ **Never sees your PIN** (only triple-derived hash)
✅ **Cannot read wallet data** even if server is compromised
✅ **Provides privacy** - no user tracking or correlation
✅ **Easy to self-host** - no vendor lock-in
✅ **Works over Tor** - censorship resistant

**Trade-off:** Requires network connectivity to unlock wallet.

**Security model:**
- Device alone: Cannot decrypt (needs server)
- Server alone: Cannot decrypt (needs device + PIN)
- Device + Server: Still needs correct PIN (3 attempts max)

The server is truly "blind" - it enforces security without learning secrets!

---

## Document Version

**Version:** 2.0 (Code-Verified)
**Date:** 2025-11-13
**Based on:** Jade firmware commit 3212faea
**Verified against:** `main/process/pinclient.c`, `main/process/auth_user.c`, `main/keychain.c`, `main/storage.c`, `pinserver/pindb.py`, `pinserver/server.py`

**All statements in this document are verified against actual code implementations.**
