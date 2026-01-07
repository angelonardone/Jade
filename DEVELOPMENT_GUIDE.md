# Jade Hardware Wallet - Comprehensive Development Guide

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Development Environment](#development-environment)
4. [Project Structure](#project-structure)
5. [Core Components & Modules](#core-components--modules)
6. [Communication Protocol](#communication-protocol)
7. [Building and Flashing](#building-and-flashing)
8. [Development Workflow](#development-workflow)
9. [Code Examples](#code-examples)
10. [Testing](#testing)

---

## Introduction

Jade is a fully open-source hardware wallet for Bitcoin and Liquid assets. The firmware runs on ESP32 and ESP32-S3 microcontrollers and provides secure key management, transaction signing, and multi-signature wallet support.

### Why Two Languages?

**C (Firmware - main/)**:
- Runs directly on the ESP32 microcontroller with bare-metal performance
- Handles cryptographic operations, UI rendering, hardware interfaces
- Uses FreeRTOS for task management
- Memory-constrained environment (~4MB flash, ~520KB RAM)
- Direct hardware access required for display, buttons, camera, BLE

**Python (Host Interface - jadepy/)**:
- Provides user-friendly API for communicating with the device
- Handles serialization/deserialization of CBOR messages
- Supports multiple transport layers (Serial, BLE, TCP)
- Used for testing, CLI tools, and integration with wallet software
- Platform-independent interface layer

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        Host Computer                         │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Python Client (jadepy/)                               │ │
│  │  - JadeAPI: High-level API                             │ │
│  │  - Transport: Serial/BLE/TCP                           │ │
│  │  - CBOR Message Encoding/Decoding                      │ │
│  └────────────────────────────────────────────────────────┘ │
└───────────────────────┬─────────────────────────────────────┘
                        │ CBOR-RPC over Serial/BLE/TCP
                        ▼
┌─────────────────────────────────────────────────────────────┐
│                     ESP32 Firmware (C)                       │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Message Router (process.c)                            │ │
│  │  - Ring Buffers for message queuing                    │ │
│  │  - CBOR-RPC parser                                     │ │
│  │  - Route to appropriate handler                        │ │
│  └────────────────────────────────────────────────────────┘ │
│                        │                                     │
│  ┌─────────────────────┴──────────────────────────────────┐ │
│  │  Request Handlers (process/*.c)                        │ │
│  │  - sign_tx.c, get_xpubs.c, ota.c, etc.                │ │
│  └────────────────────┬───────────────────────────────────┘ │
│                       │                                      │
│  ┌────────────────────┴───────────────────────────────────┐ │
│  │  Core Services                                         │ │
│  │  ┌────────────┐ ┌──────────┐ ┌────────────────────┐   │ │
│  │  │ Keychain   │ │ Storage  │ │ Wallet/Crypto      │   │ │
│  │  │ (keychain.c│ │(storage.c│ │ (wallet.c)         │   │ │
│  │  └────────────┘ └──────────┘ └────────────────────┘   │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  User Interface (ui/ & gui.c)                          │ │
│  │  - Display rendering, QR scanning, button handling     │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Hardware Abstraction Layer                            │ │
│  │  - Display (display.c/display_hw.c)                    │ │
│  │  - Camera (camera.c)                                   │ │
│  │  - Input (input/)                                      │ │
│  │  - BLE (ble/)                                          │ │
│  │  - Power Management (power.c)                          │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## Development Environment

### Prerequisites

1. **ESP-IDF v5.4** - Espressif IoT Development Framework
   - Location: `~/esp/esp-idf`
   - Commit: `67c1de1eebe095d554d281952fde63c16ee2dca0` (or release/v5.4 branch)

2. **Build Tools**
   - cmake (3.16+)
   - ninja-build
   - Python 3.10+

3. **Hardware**
   - Supported ESP32 devices: M5Stack (Gray/Black/Fire/Core2/CoreS3), TTGO T-Display, etc.
   - USB cable for flashing and serial communication

### Environment Setup

```bash
# Source ESP-IDF environment
. $HOME/esp/esp-idf/export.sh

# This sets up:
# - IDF_PATH environment variable
# - Xtensa/RISC-V toolchains in PATH
# - Python virtual environment with esptool, idf.py, etc.
```

### Why ESP-IDF?

ESP-IDF is Espressif's official development framework for ESP32 chips. It provides:
- **FreeRTOS Integration**: Real-time operating system for task management
- **Component System**: Modular architecture (like `components/`)
- **Build System**: CMake-based, handles dependencies, partitions, bootloader
- **Hardware Drivers**: SPI, I2C, WiFi, BLE, etc.
- **Security Features**: Secure boot, flash encryption, efuse management
- **Toolchain**: Cross-compilation for Xtensa (ESP32) and RISC-V (ESP32-C3/S3)

---

## Project Structure

```
Jade/
├── main/                          # Main firmware code (C)
│   ├── main.c                     # Entry point, initialization
│   ├── process.c                  # Message routing and RPC handling
│   ├── process/                   # RPC request handlers
│   │   ├── sign_tx.c             # Bitcoin transaction signing
│   │   ├── get_xpubs.c           # Export extended public keys
│   │   ├── ota.c                 # Over-the-air firmware updates
│   │   ├── mnemonic.c            # Seed phrase generation/import
│   │   └── ...                   # Other handlers
│   ├── ui/                        # User interface components
│   │   ├── dashboard.c           # Main menu/dashboard
│   │   ├── pin.c                 # PIN entry screen
│   │   ├── keyboard.c            # On-screen keyboard
│   │   ├── sign_tx.c             # Transaction review UI
│   │   └── ...
│   ├── gui.c                      # Core GUI rendering engine
│   ├── display.c                  # Display driver abstraction
│   ├── display_hw.c               # Hardware-specific display code
│   ├── keychain.c                 # Key derivation and management
│   ├── wallet.c                   # Bitcoin wallet logic
│   ├── storage.c                  # Persistent storage (NVS)
│   ├── camera.c                   # QR code scanning
│   ├── input/                     # Button/wheel input handling
│   ├── ble/                       # Bluetooth Low Energy
│   ├── utils/                     # Utility functions
│   │   ├── cbor_rpc.c/h          # CBOR-RPC protocol implementation
│   │   ├── malloc_ext.c/h        # Extended malloc (SPIRAM support)
│   │   └── ...
│   └── fonts/                     # Font definitions
│
├── components/                    # ESP-IDF components (libraries)
│   ├── libwally-core/            # Bitcoin crypto library
│   ├── esp32_bc-ur/              # BC-UR (Uniform Resources) codec
│   ├── esp32_bsdiff/             # Binary diff for OTA updates
│   ├── esp32_deflate/            # Compression
│   ├── esp32-quirc/              # QR code decoder
│   ├── assets/                   # Asset registry data
│   └── autogenlang/              # Auto-generated language strings
│
├── bootloader_components/         # Custom bootloader code
│   └── bootloader_support/       # Secure boot enhancements
│
├── jadepy/                        # Python client library
│   ├── jade.py                   # Main JadeAPI class
│   ├── jade_serial.py            # Serial transport
│   ├── jade_ble.py               # BLE transport
│   ├── jade_tcp.py               # TCP transport (for QEMU)
│   └── jade_error.py             # Error definitions
│
├── configs/                       # Hardware-specific configs
│   ├── sdkconfig_display_m5blackgray.defaults
│   ├── sdkconfig_display_m5fire.defaults
│   ├── sdkconfig_jade.defaults   # Official Jade hardware
│   └── ...
│
├── tools/                         # Development utilities
│   ├── fwprep.py                 # Firmware preparation
│   └── ...
│
├── pinserver/                     # Blind PIN server (Python)
│
├── test_jade.py                   # Comprehensive test suite
├── jade_ota.py                    # OTA update tool
├── update_jade_fw.py              # Firmware update script
├── CMakeLists.txt                 # Top-level CMake file
├── partitions.csv                 # Flash partition table
└── sdkconfig.defaults             # Build configuration
```

---

## Core Components & Modules

### 1. Message Processing System

**Location**: `main/process.c`, `main/process.h`

**Purpose**: Core message router that handles all communication between host and firmware.

**Key Concepts**:
- Uses **FreeRTOS ring buffers** for inter-task communication
- Supports multiple transport layers concurrently (Serial, BLE, TCP)
- CBOR-RPC protocol for structured message passing
- Deferred function execution for cleanup

**Code Flow**:
```c
// 1. Initialize message queues
jade_process_init() → creates ring buffers

// 2. Message arrives from serial/BLE
serial_task → writes to shared_in buffer

// 3. Process message
jade_process() → reads from shared_in
  ├─> cbor_decode() → parse CBOR
  ├─> route to handler based on "method" field
  ├─> handler executes (e.g., sign_tx_process())
  └─> cbor_encode() → send response back
```

**Key Files**:
- `main/process.c:138` - `jade_process_init()`: Initialize messaging system
- `main/process.c:293` - `jade_process()`: Main message processing loop
- `main/utils/cbor_rpc.c` - CBOR encoding/decoding utilities

---

### 2. Request Handlers (process/*.c)

Each file in `main/process/` implements a specific RPC method.

#### Example: Transaction Signing

**Location**: `main/process/sign_tx.c`

**RPC Method**: `sign_tx`

**Flow**:
1. Parse transaction from PSBT (Partially Signed Bitcoin Transaction)
2. Verify inputs and outputs
3. Show transaction details to user (via `ui/sign_tx.c`)
4. Get user confirmation
5. Sign with appropriate keys from keychain
6. Return signatures

**Key Functions**:
```c
void sign_tx_process(void* process_ptr)
{
    // 1. Parse incoming CBOR request
    // 2. Extract transaction data
    // 3. Call UI for confirmation: show_btc_transaction_outputs_activity()
    // 4. Sign with keychain_get_*() functions
    // 5. Encode and send response
}
```

#### Common Handler Pattern

All handlers follow this structure:
```c
void <method>_process(void* process_ptr) {
    jade_process_t* process = (jade_process_t*)process_ptr;

    // 1. Parse parameters from process->ctx
    CborValue params;
    rpc_get_map("params", &process->ctx, &params);

    // 2. Extract typed parameters
    const char* network;
    rpc_get_string("network", &params, network, &network_len);

    // 3. Perform operation (maybe show UI)
    bool user_confirmed = await_some_user_activity(...);

    // 4. Build response
    uint8_t buffer[256];
    jade_process_reply_to_message_ok(
        process->ctx, buffer, sizeof(buffer), &written);

    // 5. Send response
    jade_process_reply(process, buffer, written, SOURCE_INTERNAL);
}
```

---

### 3. Keychain & Cryptography

**Location**: `main/keychain.c`, `main/wallet.c`

**Purpose**: Secure key derivation and storage.

**Key Concepts**:
- Master seed stored in encrypted NVS (Non-Volatile Storage)
- BIP32/BIP39/BIP44/BIP84/BIP85 support via libwally
- Keys never leave the device in plain form
- Multiple wallet types: single-sig, multisig, descriptors

**BIP32 Path Derivation**:
```c
// Derive key at path m/84'/0'/0'/0/5
uint32_t path[] = {
    BIP32_INITIAL_HARDENED_CHILD + 84,  // Purpose
    BIP32_INITIAL_HARDENED_CHILD + 0,   // Coin type (Bitcoin)
    BIP32_INITIAL_HARDENED_CHILD + 0,   // Account
    0,                                   // Change
    5                                    // Address index
};

struct ext_key* hdkey;
keychain_derive_key(path, 5, &hdkey);
```

**libwally-core Integration**:
- Location: `components/libwally-core/`
- Provides: BIP32 derivation, signing, address generation
- All crypto operations use libwally for consistency

---

### 4. Storage System

**Location**: `main/storage.c`

**Purpose**: Persistent data storage using ESP-IDF NVS (Non-Volatile Storage).

**What's Stored**:
- Encrypted mnemonic seed
- Wallet configurations (multisig, descriptors)
- PIN data (encrypted)
- Network settings
- Attestation data

**Key Functions**:
```c
bool storage_get_encrypted_blob(const char* namespace,
                                 const char* key,
                                 uint8_t** output,
                                 size_t* output_len);

bool storage_set_encrypted_blob(const char* namespace,
                                 const char* key,
                                 const uint8_t* data,
                                 size_t data_len);
```

**Encryption**: Uses AES-256-GCM with key derived from device-specific efuses.

---

### 5. User Interface System

**Location**: `main/gui.c`, `main/ui/`, `main/display.c`

**Architecture**:
```
gui.c (High-level)
  ├─> Activity/Screen management
  ├─> Widget system (buttons, labels, text areas)
  └─> Event handling

display.c (Mid-level)
  ├─> Font rendering
  ├─> Primitive drawing (lines, rectangles, circles)
  └─> Frame buffer management

display_hw.c (Low-level)
  └─> Hardware-specific SPI commands for various displays
```

**Activity System**:
Each UI screen is an "activity" with lifecycle:
```c
typedef struct {
    void (*start_fn)(void* activity);      // Initialize
    gui_activity_t* (*run_fn)(void* activity);  // Main loop
    void (*cleanup_fn)(void* activity);    // Cleanup
} gui_activity_vtable_t;

// Example: PIN entry activity
gui_activity_t* make_pin_entry_activity(const char* title);
```

**Display Rendering**:
```c
// Clear screen
gui_set_color(BLACK);
fill_rect(0, 0, DISPLAY_WIDTH, DISPLAY_HEIGHT);

// Draw text
gui_set_color(WHITE);
gui_text(10, 10, "Hello Jade", TFT_FONT);

// Update display
display_flush();
```

---

### 6. Camera & QR Scanning

**Location**: `main/camera.c`, `components/esp32-quirc/`

**Purpose**: Scan QR codes for PSBTs, addresses, descriptors, etc.

**Flow**:
1. Initialize camera (ESP32-CAM or similar)
2. Capture frame
3. Decode with quirc library
4. Parse BC-UR encoding if multi-part
5. Return decoded data

**BC-UR Support**:
For large data (e.g., PSBTs), uses animated QR codes:
```c
// Components: esp32_bc-ur/
// Encodes/decodes fountain-coded UR (Uniform Resources)
// Allows scanning large data across multiple QR frames
```

---

### 7. BLE (Bluetooth Low Energy)

**Location**: `main/ble/ble.c`

**Purpose**: Wireless communication with mobile wallets.

**Protocol**:
- Custom GATT service for Jade
- Same CBOR-RPC messages as serial
- Encrypted channel (device pairing)

**Usage**:
```c
ble_init();  // Start BLE advertising
// Device advertises as "Jade XXXXXX" (based on MAC)
// Host connects and exchanges CBOR messages
```

---

### 8. Python Client (jadepy)

**Location**: `jadepy/jade.py`

**Purpose**: High-level Python API for host applications.

**Key Classes**:

```python
from jadepy import JadeAPI

# Connect via serial
jade = JadeAPI.create_serial(device='/dev/ttyUSB0',
                              baud=115200)
jade.connect()

# Get device info
info = jade.get_version_info()
print(f"Version: {info['JADE_VERSION']}")

# Authenticate (unlock with PIN)
jade.auth_user(network='mainnet')

# Get xpub
xpub = jade.get_xpub(network='mainnet',
                     path=[2147483732, 2147483648, 2147483648])

# Sign transaction
signatures = jade.sign_tx(network='mainnet',
                          txn=psbt_bytes,
                          inputs=input_details)

jade.disconnect()
```

**Transport Abstraction**:
```python
# Serial
JadeAPI.create_serial(device='/dev/ttyUSB0')

# BLE
JadeAPI.create_ble(device_name='Jade ABCDEF')

# TCP (for QEMU emulator)
JadeAPI.create_tcp(host='localhost', port=30121)
```

All use same CBOR-RPC protocol internally.

---

## Communication Protocol

### CBOR-RPC Format

Jade uses **CBOR** (Concise Binary Object Representation) for message encoding.

#### Request Format
```cbor
{
    "id": "001",           # Request ID (string or number)
    "method": "get_xpub",  # Method name
    "params": {            # Method-specific parameters
        "network": "mainnet",
        "path": [2147483732, 2147483648, 2147483648]
    }
}
```

#### Success Response
```cbor
{
    "id": "001",           # Matches request ID
    "result": {            # Method result
        "xpub": "xpub6C..."
    }
}
```

#### Error Response
```cbor
{
    "id": "001",
    "error": {
        "code": -32000,    # Error code (see cbor_rpc.h)
        "message": "User cancelled",
        "data": null       # Optional error data
    }
}
```

### Error Codes
Defined in `main/utils/cbor_rpc.h:13-22`:

```c
#define CBOR_RPC_INVALID_REQUEST -32600
#define CBOR_RPC_UNKNOWN_METHOD -32601
#define CBOR_RPC_BAD_PARAMETERS -32602
#define CBOR_RPC_INTERNAL_ERROR -32603
#define CBOR_RPC_USER_CANCELLED -32000
#define CBOR_RPC_PROTOCOL_ERROR -32001
#define CBOR_RPC_HW_LOCKED -32002
#define CBOR_RPC_NETWORK_MISMATCH -32003
```

### Available RPC Methods

**Device Info**:
- `get_version_info` - Firmware version, board type, features

**Authentication**:
- `auth_user` - Unlock with PIN
- `set_pin` - Change PIN

**Key Management**:
- `get_xpub` - Export extended public key
- `set_mnemonic` - Import seed phrase
- `get_receive_address` - Get receive address

**Transaction Signing**:
- `sign_tx` - Sign Bitcoin transaction (PSBT)
- `sign_message` - Sign arbitrary message
- `sign_psbt` - Enhanced PSBT signing

**Wallet Management**:
- `register_multisig` - Register multisig wallet
- `register_descriptor` - Register output descriptor
- `get_registered_multisigs` - List registered wallets

**OTA Updates**:
- `ota_data` - Upload firmware chunk
- `ota_complete` - Finalize update

**Attestation** (Jade v2+):
- `get_attestation` - Get device attestation

---

## Building and Flashing

### 1. Configure Hardware

```bash
cd /path/to/Jade

# Copy appropriate config for your hardware
cp configs/sdkconfig_display_m5blackgray.defaults sdkconfig.defaults

# For other hardware:
# M5Stack FIRE: configs/sdkconfig_display_m5fire.defaults
# M5Stack Core2: configs/sdkconfig_display_m5core2.defaults
# TTGO T-Display: configs/sdkconfig_display_ttgo_tdisplay.defaults
```

### 2. Build Firmware

```bash
# Source ESP-IDF environment
. $HOME/esp/esp-idf/export.sh

# Full clean build
rm -rf build sdkconfig
idf.py build
```

**Build Output**:
- `build/jade.bin` - Main firmware
- `build/bootloader/bootloader.bin` - Bootloader
- `build/partition_table/partition-table.bin` - Partition table

### 3. Flash to Device

```bash
# Auto-detect port and flash
idf.py flash

# Specify port
idf.py -p /dev/ttyUSB0 flash

# Flash and monitor serial output
idf.py -p /dev/ttyUSB0 flash monitor

# For lower baud rate (M5StickC-Plus)
idf.py -p /dev/ttyUSB0 -b 115200 flash monitor
```

### 4. Monitor Serial Output

```bash
idf.py monitor

# Exit: Ctrl+]
# Reset: Ctrl+T, Ctrl+R
```

### Build System Details

**CMake Structure**:
```cmake
# Top-level CMakeLists.txt
cmake_minimum_required(VERSION 3.16)
set(EXTRA_COMPONENT_DIRS bootloader_components/bootloader_support)
include($ENV{IDF_PATH}/tools/cmake/project.cmake)
project(jade)

# main/CMakeLists.txt
idf_component_register(
    SRCS "main.c" "process.c" "gui.c" ...
    INCLUDE_DIRS "." "utils" ...
    REQUIRES components...
)
```

**Component System**:
Each directory in `components/` is an ESP-IDF component with its own CMakeLists.txt.

**sdkconfig**:
Configuration options (menuconfig):
- `CONFIG_BOARD_TYPE_*` - Board selection
- `CONFIG_HAS_CAMERA` - Camera support
- `CONFIG_BT_ENABLED` - Bluetooth
- `CONFIG_SECURE_BOOT` - Secure boot
- `CONFIG_FLASH_ENCRYPTION` - Flash encryption

### Partition Table

`partitions.csv`:
```csv
# Name,   Type, SubType, Offset,  Size,   Flags
nvs,      data, nvs,     0xa000,  16K,
otadata,  data, ota,     0xe000,  8K,     encrypted
ota_0,    app,  ota_0,   0x10000, 1984K,
ota_1,    app,  ota_1,   0x200000,1984K,
nvs_key,  data, nvs_keys,0x3f0000,4K,     encrypted
```

**Dual OTA**: Two app partitions for safe updates.

---

## Development Workflow

### 1. Adding a New RPC Method

**Example**: Add `get_device_serial` method

**Step 1**: Create handler file
```bash
touch main/process/get_device_serial.c
```

**Step 2**: Implement handler
```c
// main/process/get_device_serial.c
#include "../process.h"
#include "../utils/cbor_rpc.h"

void get_device_serial_process(void* process_ptr)
{
    jade_process_t* process = process_ptr;

    // Get device serial (from MAC address)
    const char* serial = get_jade_id();

    // Build CBOR response
    uint8_t buffer[256];
    CborEncoder encoder, container;
    cbor_encoder_init(&encoder, buffer, sizeof(buffer), 0);

    // Create response map
    rpc_init_cbor(&container, process->ctx.id, process->ctx.id_len);

    // Add result
    add_string_to_map(&container, "serial", serial);

    // Finalize
    cbor_encoder_close_container(&encoder, &container);
    size_t written = cbor_encoder_get_buffer_size(&encoder, buffer);

    // Send response
    jade_process_reply(process, buffer, written, SOURCE_INTERNAL);
}
```

**Step 3**: Register in process.c
```c
// main/process.c
extern void get_device_serial_process(void* process_ptr);

// In jade_process() function, add:
} else if (rpc_is_method(&ctx, "get_device_serial")) {
    make_rpc_call(&ctx, get_device_serial_process);
```

**Step 4**: Add to CMakeLists.txt
```cmake
# main/CMakeLists.txt
idf_component_register(
    SRCS "main.c"
         "process.c"
         "process/get_device_serial.c"  # Add this
         ...
)
```

**Step 5**: Test with Python
```python
from jadepy import JadeAPI

jade = JadeAPI.create_serial('/dev/ttyUSB0')
jade.connect()

# Call our new method
result = jade.make_rpc_call('get_device_serial', {})
print(f"Serial: {result['serial']}")
```

---

### 2. Adding a UI Activity

**Example**: Add settings menu

**Step 1**: Create activity file
```c
// main/ui/settings.c
#include "../gui.h"

typedef struct {
    gui_activity_t* prev_activity;
} settings_activity_t;

static void settings_activity_cleanup(void* actptr)
{
    settings_activity_t* act = actptr;
    free(act);
}

static gui_activity_t* settings_activity_run(void* actptr)
{
    settings_activity_t* act = actptr;

    // Show menu
    const char* options[] = {
        "Change PIN",
        "Network",
        "About",
        "Back"
    };

    int selection = gui_menu_select(options, 4, "Settings");

    switch(selection) {
        case 0:
            return make_change_pin_activity(actptr);
        case 1:
            return make_network_settings_activity(actptr);
        case 2:
            return make_about_activity(actptr);
        default:
            return act->prev_activity;
    }
}

gui_activity_t* make_settings_activity(gui_activity_t* prev)
{
    settings_activity_t* act = malloc(sizeof(settings_activity_t));
    act->prev_activity = prev;

    static gui_activity_vtable_t vtable = {
        .run = settings_activity_run,
        .cleanup = settings_activity_cleanup
    };

    gui_activity_t* gui_act = make_gui_activity(&vtable, act);
    return gui_act;
}
```

**Step 2**: Call from dashboard
```c
// main/ui/dashboard.c
extern gui_activity_t* make_settings_activity(gui_activity_t* prev);

// In dashboard menu
if (selection == MENU_SETTINGS) {
    return make_settings_activity(actptr);
}
```

---

### 3. Modifying Display Layout

```c
// Example: Custom transaction confirmation screen
void show_custom_tx_screen(const char* amount,
                           const char* address,
                           bool* confirmed)
{
    gui_set_color(BLACK);
    fill_rect(0, 0, DISPLAY_WIDTH, DISPLAY_HEIGHT);

    // Title
    gui_set_color(TFT_JADE);
    gui_text_center(10, "Confirm Transaction", TFT_FONT);

    // Amount
    gui_set_color(WHITE);
    gui_text(10, 40, "Amount:", TFT_FONT);
    gui_text(10, 60, amount, TFT_FONT_LARGE);

    // Address (truncated)
    gui_text(10, 100, "To:", TFT_FONT);
    char addr_short[20];
    snprintf(addr_short, sizeof(addr_short),
             "%.8s...%.8s", address, address + strlen(address) - 8);
    gui_text(10, 120, addr_short, TFT_FONT);

    // Buttons
    gui_set_color(TFT_GREEN);
    fill_rect(10, 200, 100, 30);
    gui_set_color(BLACK);
    gui_text(30, 210, "Accept", TFT_FONT);

    gui_set_color(TFT_RED);
    fill_rect(130, 200, 100, 30);
    gui_set_color(BLACK);
    gui_text(150, 210, "Reject", TFT_FONT);

    display_flush();

    // Wait for button
    *confirmed = wait_for_button_press() == BUTTON_LEFT;
}
```

---

## Code Examples

### Example 1: Deriving Bitcoin Address

```c
#include "keychain.h"
#include "wallet.h"
#include <wally_address.h>

void get_native_segwit_address(char** address_out)
{
    // BIP84 path: m/84'/0'/0'/0/0
    uint32_t path[] = {
        BIP32_INITIAL_HARDENED_CHILD + 84,  // Purpose
        BIP32_INITIAL_HARDENED_CHILD + 0,   // Coin (BTC)
        BIP32_INITIAL_HARDENED_CHILD + 0,   // Account
        0,                                   // External chain
        0                                    // Address index
    };

    // Derive key
    struct ext_key* hdkey = NULL;
    keychain_derive_key(path, 5, &hdkey);

    // Get public key
    uint8_t pubkey[EC_PUBLIC_KEY_LEN];
    wally_ext_key_get_pub_key(hdkey, pubkey, sizeof(pubkey));

    // Generate bech32 address
    char* address = NULL;
    wally_bip32_key_to_addr_segwit(
        hdkey,
        "bc",    // Mainnet prefix
        0,       // Flags
        &address
    );

    *address_out = address;

    // Cleanup
    wally_bip32_key_free(hdkey);
}
```

---

### Example 2: Python Client - Sign Transaction

```python
import sys
from jadepy import JadeAPI
from binascii import hexlify, unhexlify

# Connect to Jade
jade = JadeAPI.create_serial(device='/dev/ttyUSB0')
jade.connect()

# Unlock device
jade.auth_user(network='testnet')

# Transaction details
psbt_hex = "70736274ff01007d..."  # Your PSBT here
psbt_bytes = unhexlify(psbt_hex)

# Input details for Jade
inputs = [{
    'is_witness': True,
    'script': unhexlify('001429...'),  # scriptPubKey
    'value_satoshi': 100000,
    'path': [2147483732, 2147483649, 2147483648, 0, 0]  # m/84'/1'/0'/0/0
}]

# Sign transaction
try:
    signatures = jade.sign_tx(
        network='testnet',
        txn=psbt_bytes,
        inputs=inputs,
        change=[]  # Change output details if any
    )

    print("Signatures received:")
    for i, sig_info in enumerate(signatures):
        print(f"  Input {i}: {hexlify(sig_info['sig']).decode()}")

except JadeError as e:
    print(f"Error: {e}")

finally:
    jade.disconnect()
```

---

### Example 3: Registering Multisig Wallet

```python
# 2-of-3 multisig wallet
multisig_name = "My Multisig"
variant = "wsh(sortedmulti(k))"  # Native segwit, sorted keys
threshold = 2

# Co-signer xpubs (at same derivation level)
signer_details = [
    {
        'fingerprint': unhexlify('11223344'),
        'derivation': [2147483696, 2147483649, 2147483648],  # m/48'/1'/0'
        'xpub': 'tpub...'
    },
    {
        'fingerprint': unhexlify('55667788'),
        'derivation': [2147483696, 2147483649, 2147483648],
        'xpub': 'tpub...'
    },
    # Jade's key will be third signer
]

# Jade's path for this multisig
jade_path = [2147483696, 2147483649, 2147483648]

# Register on Jade
result = jade.register_multisig(
    network='testnet',
    multisig_name=multisig_name,
    variant=variant,
    threshold=threshold,
    signers=signer_details,
    master_blinding_key=None  # For Liquid only
)

print(f"Registered multisig: {result}")
```

---

## Testing

### Unit Tests (Python)

**Location**: `test_jade.py`

**Run all tests**:
```bash
# Setup Python environment
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt

# Connect Jade via USB
# Run tests
python test_jade.py

# Specific test
python test_jade.py TestJade.test_sign_tx
```

**Test Structure**:
```python
class TestJade(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        # Connect to Jade once
        cls.jade = JadeAPI.create_serial('/dev/ttyUSB0')
        cls.jade.connect()

    def test_get_version(self):
        info = self.jade.get_version_info()
        self.assertIn('JADE_VERSION', info)

    def test_set_mnemonic(self):
        mnemonic = "abandon abandon ... art"
        result = self.jade.set_mnemonic(mnemonic)
        self.assertTrue(result)
```

### Integration Tests

Test with real hardware:
```bash
# Flash test firmware
idf.py -p /dev/ttyUSB0 flash

# Run integration tests
python test_jade.py --full-suite
```

### QEMU Emulator Testing

**Build QEMU image**:
```bash
docker build -t jade-qemu -f Dockerfile.qemu .
docker run --rm -p 30121:30121 -it jade-qemu
```

**Connect Python client**:
```python
jade = JadeAPI.create_tcp(host='localhost', port=30121)
jade.connect()
# ... run tests ...
```

### Debugging

**Serial Logging**:
```c
// In code
#include "jade_log.h"

JADE_LOGI("Info message: %d", value);
JADE_LOGW("Warning: %s", message);
JADE_LOGE("Error code: %d", error);
```

**View logs**:
```bash
idf.py monitor
```

**GDB Debugging**:
```bash
# Start OpenOCD
openocd -f board/esp32-wrover-kit-3.3v.cfg

# In another terminal
xtensa-esp32-elf-gdb build/jade.elf
(gdb) target remote :3333
(gdb) monitor reset halt
(gdb) break main
(gdb) continue
```

---

## Advanced Topics

### 1. Secure Boot

For production devices, enable secure boot:

```bash
# Configure
idf.py menuconfig
# Security features → Enable secure boot v2

# Generate signing key (KEEP SECURE!)
espsecure.py generate_signing_key --version 2 secure_boot_signing_key.pem

# Build with secure boot
idf.py build

# Flash (can only be done ONCE per device)
idf.py flash
```

### 2. Flash Encryption

Encrypt firmware on device:
```bash
# menuconfig → Security → Enable flash encryption
# Build and flash once
# Device will encrypt flash on first boot
```

### 3. Custom Hardware Support

To add support for new hardware:

1. Create config file: `configs/sdkconfig_display_mynewboard.defaults`
2. Define board pins: Add to `main/display_hw.c`
3. Add display init: Implement in `display_hw_init()`
4. Test and iterate

---

## Common Development Tasks

### Change Default PIN
```c
// main/storage.c - For development only!
#define DEBUG_DEFAULT_PIN "123456"  // NOT for production
```

### Skip PIN Entry
```bash
# menuconfig
# Component config → Jade config → Skip PIN authentication (UNSAFE)
```

### Enable More Logging
```bash
# menuconfig
# Component config → Log output → Default log verbosity → Debug
```

### Reduce Binary Size
```bash
# menuconfig
# Compiler options → Optimization → Optimize for size (-Os)
# Component config → Jade config → Disable unused features
```

---

## Resources

### Documentation
- Jade Protocol: `docs/` directory
- ESP-IDF Docs: https://docs.espressif.com/projects/esp-idf/en/v5.4/
- libwally: https://github.com/ElementsProject/libwally-core

### Community
- Blockstream Telegram: https://t.me/blockstream_jade
- GitHub Issues: https://github.com/Blockstream/Jade/issues
- Community forum: https://community.blockstream.com

### Tools
- PSBT Viewer: https://psbt.io
- QR Code Generator: `tools/` directory
- Firmware Tools: `jade_ota.py`, `update_jade_fw.py`

---

## Conclusion

This guide covers the essentials of Jade firmware development. Key takeaways:

1. **C Firmware**: Real-time embedded system on ESP32, handles crypto and UI
2. **Python Client**: Host-side API for communication and testing
3. **CBOR-RPC**: Structured message protocol over Serial/BLE/TCP
4. **Modular Design**: Components, handlers, and UI activities
5. **Security First**: Secure boot, encryption, isolated keychain

**Next Steps**:
- Explore `test_jade.py` for real-world usage examples
- Read handler implementations in `main/process/`
- Try QEMU emulator for safe experimentation
- Join community for support

Happy hacking! 🚀
