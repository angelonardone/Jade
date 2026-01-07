# Jade Development Documentation Index

Welcome to the Jade firmware development documentation! This index will help you find the right documentation for your needs.

## Documentation Files

### 🔐 [BLIND_ORACLE_PIN_SERVER.md](./BLIND_ORACLE_PIN_SERVER.md)
**Deep dive into the Blind Oracle PIN server system**

- What is a Blind Oracle and how it works
- When the PIN server is used (and when it's not)
- Complete cryptographic protocol explanation
- Detailed code walkthrough with examples
- Security analysis
- Running your own PIN server

**Best for**: Understanding PIN security, cryptographic protocols, self-hosting

---

### 🚀 [QUICKSTART_DEV.md](./QUICKSTART_DEV.md)
**Start here if you're new to Jade development**

- 5-minute environment setup
- Hello World example (add your first RPC method)
- Common development tasks with code samples
- Development workflow and debugging tips
- Troubleshooting guide

**Best for**: Getting started quickly, first-time setup

---

### 📘 [DEVELOPMENT_GUIDE.md](./DEVELOPMENT_GUIDE.md)
**Comprehensive guide to Jade architecture and development**

**Contents**:
- Architecture overview with detailed diagrams
- Complete project structure explanation
- In-depth module documentation
- Communication protocol (CBOR-RPC)
- Build system explanation
- Why C and Python are both used
- Advanced development workflows
- Testing strategies

**Best for**: Understanding the system deeply, developing complex features

**Key sections**:
- [Architecture Overview](./DEVELOPMENT_GUIDE.md#architecture-overview) - System design
- [Core Components & Modules](./DEVELOPMENT_GUIDE.md#core-components--modules) - Module details
- [Communication Protocol](./DEVELOPMENT_GUIDE.md#communication-protocol) - CBOR-RPC
- [Code Examples](./DEVELOPMENT_GUIDE.md#code-examples) - Real-world examples

---

### 📖 [API_REFERENCE.md](./API_REFERENCE.md)
**Quick reference for Jade APIs**

**Contents**:
- C Firmware API
  - Message/Process handling
  - Keychain functions
  - Storage functions
  - GUI functions
  - CBOR helpers
- Python Client API
  - Connection management
  - Authentication
  - Transaction signing
  - Wallet management
- Complete RPC method list
- Common code patterns
- Configuration macros

**Best for**: Quick lookups while coding, API documentation

---

### 📄 [README.md](./README.md)
**Official Jade project README**

- Project overview
- Hardware compatibility
- Building instructions
- Basic usage

**Best for**: First-time visitors, understanding what Jade is

---

## Documentation by Use Case

### I want to...

#### ...get started with Jade development
1. Read: [README.md](./README.md) - Understand the project
2. Follow: [QUICKSTART_DEV.md](./QUICKSTART_DEV.md) - Set up and build
3. Try: Hello World example in QUICKSTART_DEV.md

#### ...understand how Jade works internally
1. Read: [DEVELOPMENT_GUIDE.md](./DEVELOPMENT_GUIDE.md) - Architecture section
2. Explore: `main/` directory with the guide
3. Reference: [API_REFERENCE.md](./API_REFERENCE.md) - For specific APIs
4. Deep dive: [BLIND_ORACLE_PIN_SERVER.md](./BLIND_ORACLE_PIN_SERVER.md) - PIN security

#### ...add a new RPC method
1. Follow: QUICKSTART_DEV.md - Hello World example
2. Reference: API_REFERENCE.md - Handler pattern
3. Study: `main/process/` directory - Existing handlers

#### ...add a new UI screen
1. Read: DEVELOPMENT_GUIDE.md - User Interface System section
2. Reference: API_REFERENCE.md - GUI functions
3. Study: `main/ui/` directory - Existing screens

#### ...work with Bitcoin keys and transactions
1. Read: DEVELOPMENT_GUIDE.md - Keychain & Cryptography section
2. Reference: API_REFERENCE.md - Keychain functions
3. Study: `main/keychain.c` and `main/wallet.c`

#### ...integrate Jade with my application (Python)
1. Read: DEVELOPMENT_GUIDE.md - Python Client section
2. Reference: API_REFERENCE.md - Python Client API
3. Study: `jadepy/jade.py` and `test_jade.py`

#### ...test my changes
1. Read: DEVELOPMENT_GUIDE.md - Testing section
2. Study: `test_jade.py` - Test examples
3. Follow: QUICKSTART_DEV.md - Debugging tips

#### ...support new hardware
1. Read: DEVELOPMENT_GUIDE.md - Custom Hardware Support
2. Study: `configs/` directory - Existing configs
3. Reference: `main/display_hw.c` - Display driver

#### ...understand PIN security and the Blind Oracle
1. Read: [BLIND_ORACLE_PIN_SERVER.md](./BLIND_ORACLE_PIN_SERVER.md) - Complete guide
2. Study: `main/process/pinclient.c` - Client implementation
3. Explore: `pinserver/` directory - Server code
4. Test: Run your own PIN server

---

## Code Organization Map

### Where to find...

| What | Where | Documentation |
|------|-------|---------------|
| Entry point | `main/main.c` | DEVELOPMENT_GUIDE.md |
| Message routing | `main/process.c` | DEVELOPMENT_GUIDE.md, API_REFERENCE.md |
| RPC handlers | `main/process/*.c` | DEVELOPMENT_GUIDE.md |
| PIN server client | `main/process/pinclient.c` | BLIND_ORACLE_PIN_SERVER.md |
| PIN server (Python) | `pinserver/` | BLIND_ORACLE_PIN_SERVER.md |
| UI screens | `main/ui/*.c` | DEVELOPMENT_GUIDE.md |
| GUI framework | `main/gui.c` | API_REFERENCE.md |
| Display driver | `main/display.c`, `main/display_hw.c` | API_REFERENCE.md |
| Key management | `main/keychain.c` | DEVELOPMENT_GUIDE.md, API_REFERENCE.md |
| Bitcoin wallet | `main/wallet.c` | DEVELOPMENT_GUIDE.md |
| Storage | `main/storage.c` | API_REFERENCE.md |
| Python client | `jadepy/jade.py` | DEVELOPMENT_GUIDE.md, API_REFERENCE.md |
| Tests | `test_jade.py` | DEVELOPMENT_GUIDE.md |
| Build configs | `configs/*.defaults` | README.md |

---

## Learning Path

### Beginner Track

1. **Setup** (30 min)
   - Follow QUICKSTART_DEV.md
   - Build and flash firmware
   - Run monitor

2. **First Modification** (1 hour)
   - Add "ping" RPC method (QUICKSTART_DEV.md)
   - Test with Python
   - Add logging

3. **Explore** (2 hours)
   - Read DEVELOPMENT_GUIDE.md - Architecture
   - Browse `main/process/` handlers
   - Try existing RPC methods with Python

### Intermediate Track

4. **UI Development** (2 hours)
   - Read DEVELOPMENT_GUIDE.md - UI System
   - Add custom screen
   - Use GUI functions from API_REFERENCE.md

5. **Storage & State** (2 hours)
   - Read about Storage in DEVELOPMENT_GUIDE.md
   - Store and retrieve data
   - Understand NVS system

6. **Integration** (2 hours)
   - Write Python script using jadepy
   - Call multiple RPC methods
   - Handle errors

### Advanced Track

7. **Cryptography** (3 hours)
   - Read Keychain section in DEVELOPMENT_GUIDE.md
   - Work with BIP32 derivation
   - Understand signing flow

8. **Transaction Signing** (4 hours)
   - Study `main/process/sign_tx.c`
   - Understand PSBT parsing
   - Test with real transactions (testnet!)

9. **Custom Feature** (Variable)
   - Design your feature
   - Implement in C and Python
   - Write tests
   - Contribute back!

---

## Development Environment Summary

### Required Software

| Component | Version | Purpose |
|-----------|---------|---------|
| ESP-IDF | v5.4 | Build system & SDK |
| cmake | 3.16+ | Build configuration |
| ninja | Latest | Build execution |
| Python | 3.10+ | Tools & testing |
| xtensa-esp-elf-gcc | (via ESP-IDF) | Cross-compiler |

### Optional Tools

| Tool | Purpose |
|------|---------|
| OpenOCD | GDB debugging |
| QEMU | Emulator testing |
| esptool.py | Manual flashing |

### Hardware Support

**Fully Supported**:
- Blockstream Jade (official)
- M5Stack Gray/Black/Fire
- M5Stack Core2/Core S3
- TTGO T-Display/T-Display S3
- ESP32-CAM variants

See `configs/` directory for specific configurations.

---

## Quick Reference

### Build Commands
```bash
idf.py build                           # Build firmware
idf.py -p PORT flash                   # Flash to device
idf.py -p PORT monitor                 # Serial monitor
idf.py -p PORT flash monitor           # Flash + monitor
idf.py app-flash                       # Fast flash (app only)
idf.py menuconfig                      # Configuration menu
```

### Development Cycle
```bash
# 1. Edit code
vim main/my_file.c

# 2. Build
idf.py build

# 3. Flash app only (fast)
idf.py app-flash

# 4. Monitor
idf.py monitor
```

### Python Testing
```python
from jadepy import JadeAPI

jade = JadeAPI.create_serial('/dev/ttyUSB0')
jade.connect()

# Your code here
info = jade.get_version_info()

jade.disconnect()
```

---

## Common Questions

### Q: Should I use C or Python for development?
**A**:
- **C**: For firmware features (RPC handlers, UI, crypto)
- **Python**: For host applications, testing, integration

### Q: How do I add a new RPC method?
**A**: See QUICKSTART_DEV.md "Hello World" section or DEVELOPMENT_GUIDE.md "Adding a New RPC Method"

### Q: How does Jade communicate with host apps?
**A**: Via CBOR-RPC over Serial/BLE/TCP. See DEVELOPMENT_GUIDE.md "Communication Protocol"

### Q: Where are Bitcoin keys stored?
**A**: Encrypted in NVS (flash storage). See DEVELOPMENT_GUIDE.md "Storage System"

### Q: How does the PIN server work and is it always used?
**A**: It's a Blind Oracle that enforces 3-attempt limits without seeing your PIN. Always used when network is available and wallet exists. See BLIND_ORACLE_PIN_SERVER.md for complete details.

### Q: Can I debug with GDB?
**A**: Yes! See QUICKSTART_DEV.md "GDB Debugging"

### Q: How do I support new hardware?
**A**: Create new sdkconfig in `configs/`, modify `display_hw.c`. See DEVELOPMENT_GUIDE.md "Custom Hardware Support"

### Q: What's the difference between Jade v1 and v2?
**A**: v2 has attestation support and enhanced security features. Both use same codebase with different configs.

---

## External Resources

### Official
- **Jade GitHub**: https://github.com/Blockstream/Jade
- **Blockstream**: https://blockstream.com/jade/

### ESP32
- **ESP-IDF Docs**: https://docs.espressif.com/projects/esp-idf/en/v5.4/
- **ESP32 Datasheet**: Available from Espressif

### Bitcoin/Crypto
- **libwally**: https://github.com/ElementsProject/libwally-core
- **BIP32**: https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki
- **BIP39**: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki

### Community
- **Telegram**: https://t.me/blockstream_jade
- **Forum**: https://community.blockstream.com
- **Issues**: https://github.com/Blockstream/Jade/issues

---

## Contributing

Want to contribute to Jade?

1. **Small changes**: Open PR directly
2. **Large changes**: Open issue first to discuss
3. **Follow style**: Run `./format.sh` before committing
4. **Test**: Run `test_jade.py`
5. **Document**: Update docs if needed

See Jade's CONTRIBUTING.md for details.

---

## Getting Help

1. **Check docs**: Start with this index
2. **Read source**: Code is well-commented
3. **Search issues**: https://github.com/Blockstream/Jade/issues
4. **Ask community**: Telegram or forum
5. **Open issue**: If you found a bug

---

## Document Status

| Document | Last Updated | Status |
|----------|-------------|--------|
| QUICKSTART_DEV.md | 2025-11-13 | ✅ Complete |
| DEVELOPMENT_GUIDE.md | 2025-11-13 | ✅ Complete |
| API_REFERENCE.md | 2025-11-13 | ✅ Complete |
| BLIND_ORACLE_PIN_SERVER.md | 2025-11-13 | ✅ Complete |
| DOCS_INDEX.md | 2025-11-13 | ✅ Complete |

---

## Next Steps

1. Choose your learning path above
2. Start with QUICKSTART_DEV.md
3. Dive into DEVELOPMENT_GUIDE.md
4. Keep API_REFERENCE.md handy

**Happy hacking!** 🚀🔐
