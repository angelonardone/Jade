# Jade API Reference

Quick reference for Jade firmware development and Python client usage.

## Table of Contents
1. [C Firmware API](#c-firmware-api)
2. [Python Client API](#python-client-api)
3. [RPC Methods](#rpc-methods)
4. [Common Code Patterns](#common-code-patterns)

---

## C Firmware API

### Process/Message Handling

#### Create New RPC Handler
```c
// main/process/my_handler.c
#include "../process.h"
#include "../utils/cbor_rpc.h"

void my_handler_process(void* process_ptr)
{
    jade_process_t* process = process_ptr;

    // 1. Parse params
    CborValue params;
    if (!rpc_get_map("params", &process->ctx, &params)) {
        jade_process_reject_message(
            process, CBOR_RPC_BAD_PARAMETERS,
            "Missing params", NULL);
        return;
    }

    // 2. Extract parameters
    const char* network = NULL;
    size_t network_len = 0;
    rpc_get_string_ptr("network", &params, &network, &network_len);

    uint64_t amount = 0;
    rpc_get_uint64_t("amount", &params, &amount);

    // 3. Do work
    // ...

    // 4. Build response
    uint8_t buffer[256];
    CborEncoder root_encoder, root_container;
    cbor_encoder_init(&root_encoder, buffer, sizeof(buffer), 0);

    rpc_init_cbor(&root_container,
                  process->ctx.id,
                  process->ctx.id_len);

    add_string_to_map(&root_container, "result", "success");
    add_uint_to_map(&root_container, "value", 42);

    cbor_encoder_close_container(&root_encoder, &root_container);
    size_t written = cbor_encoder_get_buffer_size(&root_encoder, buffer);

    // 5. Send response
    jade_process_reply(process, buffer, written, SOURCE_INTERNAL);
}
```

#### Register Handler
```c
// In main/process.c, in jade_process() function:
extern void my_handler_process(void* process_ptr);

// Add to method routing:
} else if (rpc_is_method(&ctx, "my_method")) {
    make_rpc_call(&ctx, my_handler_process);
```

---

### Keychain Functions

#### Derive BIP32 Key
```c
#include "keychain.h"

// Path: m/84'/0'/0'/0/5
uint32_t path[] = {
    BIP32_INITIAL_HARDENED_CHILD + 84,
    BIP32_INITIAL_HARDENED_CHILD + 0,
    BIP32_INITIAL_HARDENED_CHILD + 0,
    0,
    5
};
size_t path_len = 5;

struct ext_key* hdkey = NULL;
keychain_derive_key(path, path_len, &hdkey);

// Use key...
uint8_t pubkey[EC_PUBLIC_KEY_LEN];
wally_ext_key_get_pub_key(hdkey, pubkey, sizeof(pubkey));

// Cleanup
wally_bip32_key_free(hdkey);
```

#### Get Master Fingerprint
```c
uint8_t fingerprint[BIP32_KEY_FINGERPRINT_LEN];
keychain_get_fingerprint(fingerprint, sizeof(fingerprint));
```

#### Sign Data
```c
#include <wally_crypto.h>

uint8_t hash[SHA256_LEN];
sha256(data, data_len, hash, sizeof(hash));

uint8_t sig[EC_SIGNATURE_DER_MAX_LEN];
size_t sig_len = 0;

// Sign with private key from derived hdkey
wally_ec_sig_from_bytes(
    hdkey->priv_key + 1,  // Skip leading 0x00
    EC_PRIVATE_KEY_LEN,
    hash, sizeof(hash),
    EC_FLAG_ECDSA | EC_FLAG_GRIND_R,
    sig, sizeof(sig),
    &sig_len
);
```

---

### Storage Functions

#### Store Encrypted Data
```c
#include "storage.h"

const char* namespace = "wallet";
const char* key = "config";

uint8_t data[] = {0x01, 0x02, 0x03};
size_t data_len = sizeof(data);

bool success = storage_set_encrypted_blob(
    namespace, key, data, data_len);
```

#### Retrieve Encrypted Data
```c
uint8_t* data = NULL;
size_t data_len = 0;

bool success = storage_get_encrypted_blob(
    namespace, key, &data, &data_len);

if (success) {
    // Use data...
    free(data);  // Don't forget to free!
}
```

#### Store Simple Values
```c
// Store string
storage_set_string(namespace, "name", "My Wallet");

// Store uint32
storage_set_u32(namespace, "version", 1);

// Retrieve
char name[64];
storage_get_string(namespace, "name", name, sizeof(name));

uint32_t version = 0;
storage_get_u32(namespace, "version", &version);
```

---

### GUI Functions

#### Display Text
```c
#include "gui.h"
#include "display.h"

// Set color
gui_set_color(TFT_WHITE);

// Draw text at position
gui_text(x, y, "Hello", TFT_FONT);

// Centered text
gui_text_center(y, "Centered", TFT_FONT);

// Large font
gui_text(x, y, "Big", TFT_FONT_LARGE);
```

#### Display Primitives
```c
// Fill rectangle
gui_set_color(TFT_JADE);  // Jade green
fill_rect(x, y, width, height);

// Draw line
gui_set_color(TFT_WHITE);
draw_line(x1, y1, x2, y2);

// Draw circle
draw_circle(cx, cy, radius);

// Clear screen
gui_set_color(TFT_BLACK);
fill_rect(0, 0, DISPLAY_WIDTH, DISPLAY_HEIGHT);

// Update display
display_flush();
```

#### Color Definitions
```c
// From display.h
#define TFT_BLACK       0x0000
#define TFT_WHITE       0xFFFF
#define TFT_RED         0xF800
#define TFT_GREEN       0x07E0
#define TFT_BLUE        0x001F
#define TFT_JADE        0x0550  // Jade brand color
#define TFT_ORANGE      0xFD20
#define TFT_DARKGREY    0x7BEF
```

#### Show Message Dialog
```c
#include "gui.h"

const char* message = "Transaction signed successfully!";
await_message_activity(message);
```

#### Show Yes/No Dialog
```c
bool confirmed = false;
const char* message = "Do you want to continue?";

confirmed = await_yesno_activity(message);

if (confirmed) {
    // User clicked Yes
} else {
    // User clicked No
}
```

#### Show QR Code
```c
const char* data = "bitcoin:bc1q...";
await_qr_activity("Receive Address", data);
```

---

### Logging

```c
#include "jade_log.h"

JADE_LOGI("Info message: %d", value);
JADE_LOGW("Warning: %s", string);
JADE_LOGE("Error code: %d", error);
JADE_LOGD("Debug: %p", pointer);

// Conditional logging
#ifdef DEBUG_MODE
JADE_LOGD("This only logs in debug builds");
#endif
```

---

### Memory Management

```c
#include "utils/malloc_ext.h"

// Regular malloc
void* ptr = JADE_MALLOC(size);

// Prefer SPIRAM if available (for large buffers)
void* large_buffer = JADE_MALLOC_PREFER_SPIRAM(large_size);

// Always free
free(ptr);

// Calloc equivalent
void* zero_buffer = JADE_CALLOC(count, size);
```

---

### CBOR Helpers

#### Parse CBOR Parameters
```c
// Get string
const char* network = NULL;
size_t network_len = 0;
rpc_get_string_ptr("network", &params, &network, &network_len);

// Get fixed-size bytes
uint8_t hash[32];
rpc_get_n_bytes("hash", &params, sizeof(hash), hash);

// Get variable bytes
const uint8_t* data = NULL;
size_t data_len = 0;
rpc_get_bytes_ptr("data", &params, &data, &data_len);

// Get number
uint64_t amount = 0;
rpc_get_uint64_t("amount", &params, &amount);

size_t index = 0;
rpc_get_sizet("index", &params, &index);

// Get boolean
bool flag = false;
rpc_get_boolean("enabled", &params, &flag);

// Get BIP32 path
uint32_t path[MAX_PATH_LEN];
size_t path_len = 0;
rpc_get_bip32_path("path", &params, path, MAX_PATH_LEN, &path_len);

// Get array
CborValue array;
if (rpc_get_array("items", &params, &array)) {
    size_t arr_len = 0;
    cbor_value_get_array_length(&array, &arr_len);
    // Iterate array...
}

// Get map
CborValue map;
if (rpc_get_map("config", &params, &map)) {
    // Parse nested map...
}
```

#### Build CBOR Response
```c
uint8_t buffer[512];
CborEncoder root_encoder, container;

cbor_encoder_init(&root_encoder, buffer, sizeof(buffer), 0);

// Initialize response
rpc_init_cbor(&container, id_string, id_len);

// Add fields
add_string_to_map(&container, "network", "mainnet");
add_uint_to_map(&container, "version", 1);
add_int_to_map(&container, "code", -1);
add_boolean_to_map(&container, "success", true);
add_bytes_to_map(&container, "data", bytes, bytes_len);

// Add array
CborEncoder array_encoder;
cbor_encoder_create_array(&container, &array_encoder, 3);
cbor_encode_uint(&array_encoder, 1);
cbor_encode_uint(&array_encoder, 2);
cbor_encode_uint(&array_encoder, 3);
cbor_encoder_close_container(&container, &array_encoder);

// Finalize
cbor_encoder_close_container(&root_encoder, &container);
size_t written = cbor_encoder_get_buffer_size(&root_encoder, buffer);
```

---

## Python Client API

### Connection Management

```python
from jadepy import JadeAPI

# Serial connection
jade = JadeAPI.create_serial(
    device='/dev/ttyUSB0',     # Device path
    baud=115200,                # Baud rate
    timeout=120                 # Timeout in seconds
)

# BLE connection
jade = JadeAPI.create_ble(
    device_name='Jade',         # Device name
    serial_number='ABCDEF',     # Specific device
    scan_timeout=60             # Scan timeout
)

# TCP connection (QEMU emulator)
jade = JadeAPI.create_tcp(
    host='localhost',
    port=30121
)

# Connect
jade.connect()

# Always disconnect when done
try:
    # ... operations ...
finally:
    jade.disconnect()
```

### Device Information

```python
# Get version info
info = jade.get_version_info()
print(f"Version: {info['JADE_VERSION']}")
print(f"Config: {info['JADE_CONFIG']}")
print(f"Board: {info['BOARD_TYPE']}")
print(f"Features: {info['JADE_FEATURES']}")

# Check if logged in
is_logged_in = jade.is_authenticated()
```

### Authentication

```python
# Unlock with PIN
jade.auth_user(network='mainnet')

# Set new PIN (must be authenticated first)
jade.set_pin(pin='123456')  # For testing only!

# Logout
jade.logout()
```

### Key Export

```python
# Get xpub at BIP32 path
# Path: m/84'/0'/0'
path = [
    0x80000000 + 84,  # 84' (hardened)
    0x80000000 + 0,   # 0'
    0x80000000 + 0    # 0'
]

xpub = jade.get_xpub(
    network='mainnet',
    path=path
)
print(f"xpub: {xpub}")

# Get multiple xpubs
xpubs_result = jade.get_xpubs(
    network='mainnet',
    paths=[
        [0x80000000 + 84, 0x80000000 + 0, 0x80000000 + 0],  # m/84'/0'/0'
        [0x80000000 + 49, 0x80000000 + 0, 0x80000000 + 0],  # m/49'/0'/0'
        [0x80000000 + 44, 0x80000000 + 0, 0x80000000 + 0],  # m/44'/0'/0'
    ]
)

for xpub in xpubs_result:
    print(xpub)
```

### Address Generation

```python
# Get receive address
# Variant: sh(wpkh(k)) = nested segwit
# Variant: wpkh(k) = native segwit (bech32)
# Variant: pkh(k) = legacy

address_info = jade.get_receive_address(
    network='mainnet',
    path=[0x80000000 + 84, 0x80000000 + 0, 0x80000000 + 0, 0, 5],
    variant='wpkh(k)',  # Native segwit
    recovery_xpub=None
)

print(f"Address: {address_info['address']}")
```

### Mnemonic Management

```python
# Generate new mnemonic (12 or 24 words)
jade.set_mnemonic(mnemonic=None)  # Auto-generate
# User writes down words shown on device

# Import existing mnemonic
mnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about"
jade.set_mnemonic(mnemonic=mnemonic)

# Temporary mnemonic (not stored)
jade.set_mnemonic(
    mnemonic=mnemonic,
    temporary_wallet=True
)
```

### Transaction Signing

```python
from binascii import hexlify, unhexlify

# Prepare PSBT
psbt_hex = "70736274..."  # Your PSBT
psbt_bytes = unhexlify(psbt_hex)

# Describe inputs for Jade
inputs = [
    {
        'is_witness': True,
        'script': unhexlify('00142f...'),  # scriptPubKey
        'value_satoshi': 100000,
        'path': [0x80000000 + 84, 0x80000000 + 0, 0x80000000 + 0, 0, 0]
    }
]

# Optional: describe change outputs
change = [
    {
        'path': [0x80000000 + 84, 0x80000000 + 0, 0x80000000 + 0, 1, 0],
        'variant': 'wpkh(k)',
        'recovery_xpub': None
    }
]

# Sign
signatures = jade.sign_tx(
    network='mainnet',
    txn=psbt_bytes,
    inputs=inputs,
    change=change,
    use_ae_signatures=False  # Anti-exfil protocol
)

# Signatures is list of dicts:
# [{'sig': bytes, 'is_der': bool}, ...]
for i, sig_info in enumerate(signatures):
    print(f"Input {i}: {hexlify(sig_info['sig']).decode()}")
```

### Message Signing

```python
# Sign text message
message = "Hello, Bitcoin!"
path = [0x80000000 + 84, 0x80000000 + 0, 0x80000000 + 0, 0, 0]

signature = jade.sign_message(
    path=path,
    message=message,
    use_ae_protocol=False
)

print(f"Signature: {signature}")
# Returns base64-encoded signature
```

### Multisig Wallets

```python
# Register 2-of-3 multisig
multisig_name = "My Multisig"

# Cosigner details
signers = [
    {
        'fingerprint': unhexlify('11223344'),
        'derivation': [0x80000000 + 48, 0x80000000 + 0, 0x80000000 + 0],
        'xpub': 'tpubD6NzV...'
    },
    {
        'fingerprint': unhexlify('55667788'),
        'derivation': [0x80000000 + 48, 0x80000000 + 0, 0x80000000 + 0],
        'xpub': 'tpubD6NzV...'
    }
]

result = jade.register_multisig(
    network='testnet',
    multisig_name=multisig_name,
    variant='wsh(sortedmulti(k))',  # Native segwit, sorted
    threshold=2,
    signers=signers,
    master_blinding_key=None
)

print(f"Registered: {result}")

# List registered multisigs
multisigs = jade.get_registered_multisigs()
for ms in multisigs['multisigs']:
    print(f"{ms['multisig_name']}: {ms['variant']}")

# Get specific multisig
multisig = jade.get_registered_multisig(multisig_name)
```

### Output Descriptors

```python
# Register output descriptor
descriptor_name = "My Descriptor"
descriptor = "wpkh([11223344/84'/0'/0']tpubD6NzV.../0/*)"

result = jade.register_descriptor(
    network='testnet',
    descriptor_name=descriptor_name,
    descriptor=descriptor
)

# List descriptors
descriptors = jade.get_registered_descriptors()

# Get specific descriptor
desc = jade.get_registered_descriptor(descriptor_name)
```

### OTA Updates

```python
# Update firmware from file
firmware_path = "jade_v1.0.36.bin"

with open(firmware_path, 'rb') as f:
    firmware_data = f.read()

# Compute hash
import hashlib
fw_hash = hashlib.sha256(firmware_data).digest()

# Upload firmware
chunk_size = 4096
offset = 0

while offset < len(firmware_data):
    chunk = firmware_data[offset:offset + chunk_size]

    jade.ota_data(
        fwcmp=chunk,
        fwlen=len(firmware_data),
        cmplen=len(chunk)
    )

    offset += chunk_size
    print(f"Uploaded: {offset}/{len(firmware_data)}")

# Complete update
jade.ota_complete(fw_hash)

# Device will reboot with new firmware
```

### Low-Level RPC

```python
# Make custom RPC call
result = jade.make_rpc_call(
    method='my_custom_method',
    params={
        'param1': 'value',
        'param2': 42
    },
    timeout=30
)

print(result)
```

---

## RPC Methods

### Complete Method List

| Method | Description | Parameters |
|--------|-------------|------------|
| `get_version_info` | Get firmware version | - |
| `auth_user` | Unlock with PIN | `network`, `epoch` (optional) |
| `logout` | Lock device | - |
| `set_pin` | Change PIN | - (interactive) |
| `set_mnemonic` | Import/generate seed | `mnemonic` (optional), `passphrase` (optional) |
| `get_xpub` | Export xpub | `network`, `path` |
| `get_xpubs` | Export multiple xpubs | `network`, `paths` |
| `get_receive_address` | Get address | `network`, `path`, `variant` |
| `sign_tx` | Sign transaction | `network`, `txn`, `inputs`, `change` |
| `sign_message` | Sign message | `path`, `message` |
| `sign_psbt` | Enhanced PSBT signing | `psbt`, ... |
| `register_multisig` | Register multisig | `multisig_name`, `variant`, `threshold`, `signers` |
| `get_registered_multisigs` | List multisigs | - |
| `get_registered_multisig` | Get specific multisig | `multisig_name` |
| `register_descriptor` | Register descriptor | `descriptor_name`, `descriptor` |
| `get_registered_descriptors` | List descriptors | - |
| `get_registered_descriptor` | Get specific descriptor | `descriptor_name` |
| `ota_data` | Upload firmware chunk | `fwcmp`, `fwlen`, `cmplen` |
| `ota_complete` | Finalize update | `fwhash` |
| `get_bip85_entropy` | BIP85 entropy | `network`, `path`, `length` |
| `get_identity_pubkey` | Identity key | `identity`, `curve` |
| `sign_identity` | Sign identity challenge | `identity`, `challenge` |
| `get_otp_code` | TOTP/HOTP code | `otp_name` |

---

## Common Code Patterns

### Pattern 1: Handler with User Confirmation

```c
// C firmware - main/process/my_handler.c
void my_handler_process(void* process_ptr)
{
    jade_process_t* process = process_ptr;

    // Parse parameters
    CborValue params;
    rpc_get_map("params", &process->ctx, &params);

    const char* data = NULL;
    size_t data_len = 0;
    rpc_get_string_ptr("data", &params, &data, &data_len);

    // Show to user and get confirmation
    char message[128];
    snprintf(message, sizeof(message), "Confirm: %.*s", (int)data_len, data);

    bool confirmed = await_yesno_activity(message);

    if (!confirmed) {
        jade_process_reject_message(
            process, CBOR_RPC_USER_CANCELLED,
            "User declined", NULL);
        return;
    }

    // Process confirmed action
    // ...

    // Send success response
    uint8_t buffer[256];
    jade_process_reply_to_message_ok(
        process->ctx, buffer, sizeof(buffer), &written);
    jade_process_reply(process, buffer, written, SOURCE_INTERNAL);
}
```

### Pattern 2: Multi-Step UI Flow

```c
// C firmware - multi-step activity
gui_activity_t* make_my_multi_step_activity(void)
{
    // Step 1: Get user input
    char input[32];
    if (!await_keyboard_activity("Enter value:", input, sizeof(input))) {
        return NULL;  // Cancelled
    }

    // Step 2: Show confirmation
    char confirm_msg[128];
    snprintf(confirm_msg, sizeof(confirm_msg), "You entered: %s", input);

    if (!await_yesno_activity(confirm_msg)) {
        return NULL;  // Not confirmed
    }

    // Step 3: Process and show result
    // ... do work ...

    await_message_activity("Success!");

    return NULL;  // Return to previous activity
}
```

### Pattern 3: Safe Resource Cleanup

```c
void my_function(void)
{
    uint8_t* buffer = NULL;
    struct ext_key* key = NULL;

    // Allocate resources
    buffer = JADE_MALLOC(1024);
    if (!buffer) goto cleanup;

    keychain_derive_key(path, path_len, &key);
    if (!key) goto cleanup;

    // Use resources
    // ...

cleanup:
    // Always cleanup
    if (buffer) free(buffer);
    if (key) wally_bip32_key_free(key);
}
```

### Pattern 4: Python Error Handling

```python
from jadepy import JadeAPI, JadeError

jade = JadeAPI.create_serial('/dev/ttyUSB0')

try:
    jade.connect()
    jade.auth_user(network='mainnet')

    # Operations that might fail
    result = jade.sign_tx(...)

except JadeError as e:
    if e.code == -32000:  # User cancelled
        print("User cancelled operation")
    elif e.code == -32002:  # Device locked
        print("Device is locked, please unlock first")
    else:
        print(f"Jade error: {e}")

except Exception as e:
    print(f"Unexpected error: {e}")

finally:
    jade.disconnect()
```

---

## Configuration Macros

### Important Build Flags

```c
// Board types
#ifdef CONFIG_BOARD_TYPE_JADE
#ifdef CONFIG_BOARD_TYPE_JADE_V1_1
#ifdef CONFIG_BOARD_TYPE_M5_FIRE
#ifdef CONFIG_BOARD_TYPE_M5_BLACK_GRAY
#ifdef CONFIG_BOARD_TYPE_M5_CORES3

// Features
#ifdef CONFIG_HAS_CAMERA
#ifdef CONFIG_BT_ENABLED
#ifdef CONFIG_DEBUG_MODE
#ifdef CONFIG_LOG_DEFAULT_LEVEL_DEBUG

// Hardware
#ifdef CONFIG_IDF_TARGET_ESP32
#ifdef CONFIG_IDF_TARGET_ESP32S3

// Security
#ifdef CONFIG_SECURE_BOOT
#ifdef CONFIG_FLASH_ENCRYPTION
```

### Conditional Compilation Example

```c
void my_function(void)
{
#ifdef CONFIG_HAS_CAMERA
    // Camera-specific code
    camera_init();
    scan_qr_code();
#else
    // Fallback for no camera
    JADE_LOGW("Camera not available");
#endif

#ifdef CONFIG_DEBUG_MODE
    // Debug-only code
    JADE_LOGD("Debug info: %d", value);
#endif
}
```

---

## Useful Scripts

### Flash Specific Partition

```bash
# Flash only app partition (faster during development)
esptool.py --port /dev/ttyUSB0 write_flash \
    0x10000 build/jade.bin

# Flash everything
idf.py -p /dev/ttyUSB0 flash
```

### Monitor with Filtering

```bash
# Only show errors and warnings
idf.py monitor --print_filter "*:W"

# Show only specific tags
idf.py monitor --print_filter "jade:I"
```

### Generate Test Mnemonic

```python
from mnemonic import Mnemonic

mnemo = Mnemonic("english")
words = mnemo.generate(strength=128)  # 12 words
print(words)
```

---

## Performance Tips

### Memory Optimization

```c
// Use SPIRAM for large buffers
uint8_t* large_buffer = JADE_MALLOC_PREFER_SPIRAM(100000);

// Free immediately when done
free(large_buffer);

// Reuse buffers instead of allocating
static uint8_t static_buffer[4096];  // Persists between calls
```

### Display Optimization

```c
// Batch updates
display_disable_auto_flush();

// ... many drawing operations ...
gui_text(...);
fill_rect(...);
gui_text(...);

// Single flush at end
display_flush();
```

### CBOR Optimization

```c
// Pre-calculate buffer size
size_t estimated_size = 256 + data_len;
uint8_t* buffer = JADE_MALLOC(estimated_size);

// Use buffer
// ...

free(buffer);
```

---

This API reference should serve as a quick lookup while developing. For more detailed information, refer to the DEVELOPMENT_GUIDE.md and source code comments.
