# Jade Development Quick Start

Get up and running with Jade development in 30 minutes.

## Prerequisites Checklist

- [ ] macOS, Linux, or WSL2
- [ ] ESP-IDF v5.4 installed at `~/esp/esp-idf`
- [ ] cmake and ninja installed
- [ ] Python 3.10+
- [ ] M5Stack or compatible ESP32 device
- [ ] USB cable

## 5-Minute Setup

### 1. Environment Setup

```bash
# Source ESP-IDF (add to ~/.bashrc or ~/.zshrc)
. $HOME/esp/esp-idf/export.sh

# Verify tools
which idf.py  # Should show path in .espressif
cmake --version  # Should be 3.16+
```

### 2. Clone & Configure

```bash
cd ~/projects  # Or your preferred directory
git clone --recursive https://github.com/Blockstream/Jade.git
cd Jade

# Update submodules
git submodule update --init --recursive

# Configure for your hardware (M5Stack Gray/Black example)
cp configs/sdkconfig_display_m5blackgray.defaults sdkconfig.defaults
```

### 3. Build & Flash

```bash
# Connect your device via USB

# Build and flash in one command
idf.py -p /dev/ttyUSB0 flash monitor

# On macOS, port might be /dev/cu.usbserial-*
# Find with: ls /dev/cu.*

# Exit monitor: Ctrl+]
```

**Done!** Your device should now boot Jade firmware.

---

## Hello World: Your First Custom Feature

Let's add a simple "ping" RPC method that responds with "pong".

### Step 1: Create Handler (3 minutes)

Create `main/process/ping.c`:

```c
#include "../process.h"
#include "../utils/cbor_rpc.h"

void ping_process(void* process_ptr)
{
    jade_process_t* process = process_ptr;

    // Build response
    uint8_t buffer[128];
    CborEncoder root_encoder, container;

    cbor_encoder_init(&root_encoder, buffer, sizeof(buffer), 0);
    rpc_init_cbor(&container, process->ctx.id, process->ctx.id_len);

    // Add "pong" message
    add_string_to_map(&container, "message", "pong");
    add_uint_to_map(&container, "timestamp", (uint64_t)time(NULL));

    cbor_encoder_close_container(&root_encoder, &container);
    size_t written = cbor_encoder_get_buffer_size(&root_encoder, buffer);

    // Send response
    jade_process_reply(process, buffer, written, SOURCE_INTERNAL);
}
```

### Step 2: Register Handler (2 minutes)

Edit `main/process.c`:

```c
// Near top of file, with other extern declarations
extern void ping_process(void* process_ptr);

// In jade_process() function, find the method routing section
// Add before the final 'else' clause:

} else if (rpc_is_method(&ctx, "ping")) {
    make_rpc_call(&ctx, ping_process);
```

### Step 3: Add to Build (1 minute)

Edit `main/CMakeLists.txt`, add to SRCS list:

```cmake
idf_component_register(
    SRCS "main.c"
         "process.c"
         "process/ping.c"  # Add this line
         # ... rest of files
```

### Step 4: Build & Test (4 minutes)

```bash
# Rebuild
idf.py build

# Flash
idf.py -p /dev/ttyUSB0 flash monitor
```

### Step 5: Test with Python (2 minutes)

Create `test_ping.py`:

```python
from jadepy import JadeAPI

jade = JadeAPI.create_serial('/dev/ttyUSB0')
jade.connect()

# Call our new method
result = jade.make_rpc_call('ping', {})
print(f"Response: {result}")
# Output: {'message': 'pong', 'timestamp': 1234567890}

jade.disconnect()
```

Run it:
```bash
python test_ping.py
```

**Congratulations!** You've added your first RPC method.

---

## Common Development Tasks

### Task 1: Add Logging

```c
#include "jade_log.h"

void my_function(int value)
{
    JADE_LOGI("Function called with value: %d", value);

    if (value < 0) {
        JADE_LOGW("Negative value: %d", value);
        return;
    }

    JADE_LOGD("Processing value: %d", value);
}
```

View logs:
```bash
idf.py monitor
```

### Task 2: Show Message on Screen

```c
#include "gui.h"

void show_hello_world(void)
{
    // Clear screen
    gui_set_color(TFT_BLACK);
    fill_rect(0, 0, DISPLAY_WIDTH, DISPLAY_HEIGHT);

    // Show title
    gui_set_color(TFT_JADE);
    gui_text_center(40, "Hello World!", TFT_FONT_LARGE);

    // Show message
    gui_set_color(TFT_WHITE);
    gui_text_center(80, "This is Jade", TFT_FONT);

    // Update display
    display_flush();

    // Wait 3 seconds
    vTaskDelay(pdMS_TO_TICKS(3000));
}
```

### Task 3: Get User Confirmation

```c
#include "ui.h"

void my_protected_operation(void)
{
    bool confirmed = await_yesno_activity(
        "Do you want to proceed with this operation?"
    );

    if (confirmed) {
        JADE_LOGI("User confirmed");
        // Do operation
    } else {
        JADE_LOGI("User declined");
        return;
    }
}
```

### Task 4: Store Configuration

```c
#include "storage.h"

void save_my_config(void)
{
    const char* namespace = "myapp";

    // Save string
    storage_set_string(namespace, "username", "alice");

    // Save number
    storage_set_u32(namespace, "version", 1);

    // Save binary data
    uint8_t data[] = {0x01, 0x02, 0x03};
    storage_set_encrypted_blob(namespace, "secret", data, sizeof(data));
}

void load_my_config(void)
{
    const char* namespace = "myapp";

    // Load string
    char username[32];
    storage_get_string(namespace, "username", username, sizeof(username));
    JADE_LOGI("Username: %s", username);

    // Load number
    uint32_t version = 0;
    storage_get_u32(namespace, "version", &version);
    JADE_LOGI("Version: %u", version);

    // Load binary
    uint8_t* data = NULL;
    size_t data_len = 0;
    if (storage_get_encrypted_blob(namespace, "secret", &data, &data_len)) {
        JADE_LOGI("Loaded %zu bytes", data_len);
        free(data);
    }
}
```

### Task 5: Python Client Script

```python
#!/usr/bin/env python3
from jadepy import JadeAPI
import sys

def main():
    # Connect
    jade = JadeAPI.create_serial('/dev/ttyUSB0')

    try:
        jade.connect()
        print("Connected to Jade")

        # Get version
        info = jade.get_version_info()
        print(f"Firmware: {info['JADE_VERSION']}")
        print(f"Board: {info['BOARD_TYPE']}")

        # Unlock (use actual PIN in production!)
        jade.auth_user(network='testnet')
        print("Authenticated")

        # Get xpub
        xpub = jade.get_xpub(
            network='testnet',
            path=[0x80000000 + 84, 0x80000000 + 1, 0x80000000 + 0]
        )
        print(f"xpub: {xpub}")

    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)

    finally:
        jade.disconnect()

if __name__ == '__main__':
    main()
```

---

## Development Workflow

### Iterative Development Loop

```bash
# 1. Make changes to code
vim main/process/my_feature.c

# 2. Build (faster than full rebuild)
idf.py build

# 3. Flash only app partition (much faster)
idf.py app-flash

# 4. Monitor
idf.py monitor

# Or all in one:
idf.py app-flash monitor
```

### Reset Without Reflashing

While monitoring (Ctrl+T then Ctrl+R):
```
--- idf_monitor on /dev/ttyUSB0 115200 ---
Ctrl+] to exit, Ctrl+T Ctrl+R to reset
```

### Clean Build

When things get weird:
```bash
rm -rf build sdkconfig
idf.py build
```

---

## Debugging Tips

### Enable Debug Logging

```bash
idf.py menuconfig
# Component config → Log output → Default log verbosity → Debug
# Save and rebuild
```

### Serial Debugging

```c
// In your code
#include "jade_log.h"

JADE_LOGD("Variable value: %d", my_var);
JADE_LOGD("Pointer: %p", my_ptr);
JADE_LOGD("String: %s", my_string);

// Hex dump
for (int i = 0; i < len; i++) {
    printf("%02x ", data[i]);
}
printf("\n");
```

### Python Debugging

```python
import logging

# Enable jade debug output
logging.basicConfig(level=logging.DEBUG)
logger = logging.getLogger('jadepy')
logger.setLevel(logging.DEBUG)

jade = JadeAPI.create_serial('/dev/ttyUSB0')
# Now you'll see all CBOR messages
```

### GDB Debugging (Advanced)

```bash
# Terminal 1: Start OpenOCD
openocd -f board/esp32-wrover-kit-3.3v.cfg

# Terminal 2: Start GDB
xtensa-esp32-elf-gdb build/jade.elf
(gdb) target remote :3333
(gdb) monitor reset halt
(gdb) break main
(gdb) continue
```

---

## Project Structure Quick Reference

```
Jade/
├── main/
│   ├── main.c              # Entry point
│   ├── process.c           # Message router
│   ├── process/            # RPC handlers
│   │   ├── sign_tx.c       # Transaction signing
│   │   ├── get_xpub.c      # Key export
│   │   └── ...
│   ├── ui/                 # UI screens
│   │   ├── dashboard.c     # Main menu
│   │   ├── pin.c           # PIN entry
│   │   └── ...
│   ├── gui.c               # GUI framework
│   ├── display.c           # Display driver
│   ├── keychain.c          # Key management
│   ├── wallet.c            # Bitcoin logic
│   └── storage.c           # Persistent storage
│
├── components/             # Libraries
│   ├── libwally-core/      # Bitcoin crypto
│   ├── esp32_bc-ur/        # QR encoding
│   └── ...
│
├── jadepy/                 # Python client
│   └── jade.py             # Main API
│
├── configs/                # Hardware configs
│   └── sdkconfig_*.defaults
│
└── test_jade.py            # Test suite
```

---

## Next Steps

Now that you're set up:

1. **Read DEVELOPMENT_GUIDE.md** - Deep dive into architecture
2. **Read API_REFERENCE.md** - Complete API documentation
3. **Explore `main/process/`** - See how different features are implemented
4. **Try test_jade.py** - Run the test suite
5. **Join the Community** - https://t.me/blockstream_jade

---

## Troubleshooting

### "Port not found"
```bash
# List available ports
ls /dev/tty* | grep -i usb

# On macOS
ls /dev/cu.*

# Give yourself permissions (Linux)
sudo usermod -a -G dialout $USER
# Then logout/login
```

### "idf.py not found"
```bash
# ESP-IDF not sourced
. $HOME/esp/esp-idf/export.sh

# Add to ~/.bashrc or ~/.zshrc for persistence
```

### "Build fails with component errors"
```bash
# Update submodules
git submodule update --init --recursive

# Clean build
rm -rf build sdkconfig
idf.py build
```

### "Device constantly reboots"
```bash
# Check monitor for panic messages
idf.py monitor

# Common causes:
# - Stack overflow (increase stack size in menuconfig)
# - Null pointer dereference
# - Memory corruption
```

### "Python can't connect"
```python
# Wrong port
jade = JadeAPI.create_serial('/dev/ttyUSB0')  # Try /dev/ttyUSB1, etc.

# Baud rate mismatch
jade = JadeAPI.create_serial('/dev/ttyUSB0', baud=115200)

# Port in use by monitor
# Exit monitor (Ctrl+]) before running Python
```

---

## Quick Command Reference

```bash
# Build
idf.py build

# Flash
idf.py -p /dev/ttyUSB0 flash

# Monitor
idf.py -p /dev/ttyUSB0 monitor

# Flash + Monitor
idf.py -p /dev/ttyUSB0 flash monitor

# Fast flash (app only)
idf.py -p /dev/ttyUSB0 app-flash

# Clean
idf.py fullclean

# Menuconfig
idf.py menuconfig

# Size analysis
idf.py size

# Component size
idf.py size-components
```

---

## Useful Menuconfig Options

```
idf.py menuconfig

# Increase stack size
Component config → FreeRTOS → Main task stack size: 8192

# Enable debug
Component config → Log output → Default log verbosity: Debug

# Skip PIN (development only!)
Component config → Jade config → Skip PIN authentication: Yes

# Camera support
Component config → Jade config → Camera support: Yes

# BLE support
Component config → Bluetooth → Bluetooth: Yes
```

---

## Resources

- **Main Guide**: DEVELOPMENT_GUIDE.md
- **API Reference**: API_REFERENCE.md
- **Official Docs**: https://github.com/Blockstream/Jade
- **ESP-IDF Docs**: https://docs.espressif.com/projects/esp-idf/en/v5.4/
- **Community**: https://t.me/blockstream_jade

---

Happy coding! 🚀
