# Jade RPC Reference - C# Client Implementation Status

This document lists all RPC methods supported by the Jade hardware wallet firmware and tracks implementation status in the C# client library.

## Legend

| Status | Meaning |
|--------|---------|
| ✅ | Implemented |
| 🚧 | Partially implemented |
| ❌ | Not yet implemented |

---

## Summary

| Category | Implemented | Total | Progress |
|----------|-------------|-------|----------|
| Authentication & Session | 5 | 7 | 71% |
| Key Derivation | 2 | 2 | 100% |
| Address Generation | 1 | 1 | 100% |
| Message Signing | 0 | 2 | 0% |
| Transaction Signing | 0 | 6 | 0% |
| Multisig Management | 0 | 4 | 0% |
| Descriptor Management | 0 | 3 | 0% |
| Liquid Network | 0 | 6 | 0% |
| Identity (SLIP-0013/0017) | 0 | 3 | 0% |
| BIP85 Entropy | 0 | 4 | 0% |
| OTP (TOTP/HOTP) | 0 | 2 | 0% |
| Hardware Attestation | 0 | 2 | 0% |
| OTA Firmware Updates | 0 | 4 | 0% |
| Utility/Protocol | 0 | 3 | 0% |
| **Total** | **8** | **49** | **16%** |

---

## Authentication & Session Management

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `get_version_info` | ✅ | `GetVersionInfoAsync()` | Get firmware version, board type, state, networks, PIN status |
| `add_entropy` | ✅ | `AddEntropyAsync(byte[])` | Add random bytes to device RNG |
| `auth_user` | ✅ | `AuthUserAsync(IPinServerHandler, string)` | Authenticate via PIN server (Blind Oracle protocol) |
| `logout` | ✅ | `LogoutAsync()` | Lock the device wallet |
| `update_pinserver` | ✅ | `UpdatePinServerAsync(...)` / `ResetPinServerAsync()` | Configure or reset PIN server settings |
| `set_epoch` | ❌ | - | Set the current timestamp on device |
| `set_mnemonic` | ❌ | - | Import or generate BIP39 mnemonic seed |

---

## Key Derivation

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `get_xpub` | ✅ | `GetXpubAsync(string, uint[])` | Get extended public key for BIP32 derivation path |
| `get_xpubs` | ❌ | - | Get multiple xpubs in a single call (batch) |

---

## Address Generation

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `get_receive_address` | ✅ | `GetReceiveAddressAsync(string, uint[], string)` | Generate address with on-device verification display |

**Supported address variants:**
- `pkh(k)` - Legacy P2PKH (BIP44)
- `sh(wpkh(k))` - Nested SegWit P2SH-P2WPKH (BIP49)
- `wpkh(k)` - Native SegWit P2WPKH (BIP84)
- `tr(k)` - Taproot P2TR (BIP86)

---

## Message Signing

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `sign_message` | ❌ | - | Sign arbitrary text message with BIP32 path |
| `sign_message_file` | ❌ | - | Sign message from file format (Specter compatible) |

**Implementation notes:**
- Supports anti-exfil protocol for enhanced security
- Message displayed on device for user verification
- Returns signature in Bitcoin message format

---

## Transaction Signing

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `sign_tx` | ❌ | - | Sign Bitcoin transaction (legacy format) |
| `sign_psbt` | ❌ | - | Sign Partially Signed Bitcoin Transaction |
| `sign_liquid_tx` | ❌ | - | Sign Liquid sidechain confidential transaction |
| `tx_input` | ❌ | - | Stream transaction input data (multi-message protocol) |
| `get_signature` | ❌ | - | Retrieve signature during anti-exfil protocol |
| `get_extended_data` | ❌ | - | Fetch continuation data for large PSBT responses |

**Implementation notes:**
- `sign_tx` uses streaming protocol: send tx info, then `tx_input` for each input
- `sign_psbt` is the recommended modern approach
- Anti-exfil protocol protects against compromised RNG
- Liquid transactions require additional blinding data

---

## Multisig Wallet Management

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `register_multisig` | ❌ | - | Register/import a multisig wallet descriptor |
| `get_registered_multisigs` | ❌ | - | List all registered multisig wallets |
| `get_registered_multisig` | ❌ | - | Get details of a specific registered multisig |
| `register_multisig_file` | ❌ | - | Register multisig from standard file format |

**Implementation notes:**
- Multisig registration persists on device
- Required before signing multisig transactions
- Supports various multisig script types (P2SH, P2WSH, P2SH-P2WSH)

---

## Output Descriptor Management

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `register_descriptor` | ❌ | - | Register output descriptor wallet |
| `get_registered_descriptors` | ❌ | - | List all registered descriptor wallets |
| `get_registered_descriptor` | ❌ | - | Get details of a specific descriptor |

**Implementation notes:**
- Descriptors provide more flexible wallet definitions
- Supports miniscript policies
- Required for complex spending conditions

---

## Liquid Network (Confidential Transactions)

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `get_master_blinding_key` | ❌ | - | Export SLIP-077 master blinding key |
| `get_blinding_key` | ❌ | - | Get public blinding key for output script |
| `get_shared_nonce` | ❌ | - | Get shared secret for output unblinding |
| `get_blinding_factor` | ❌ | - | Get deterministic blinding factors (abf, vbf) |
| `get_commitments` | ❌ | - | Generate blinding factors and output commitments |
| `sign_liquid_tx` | ❌ | - | Sign Liquid confidential transaction |

**Implementation notes:**
- Liquid is a Bitcoin sidechain with confidential transactions
- Blinding hides amounts and asset types
- Requires coordination between blinding and signing steps

---

## Identity & Authentication (SLIP-0013/0017)

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `get_identity_pubkey` | ❌ | - | Derive SLIP-0013 or SLIP-0017 public key |
| `get_identity_shared_key` | ❌ | - | ECDH shared key derivation (SLIP-0017) |
| `sign_identity` | ❌ | - | Sign identity challenge (SLIP-0013) |

**Implementation notes:**
- SLIP-0013: Authentication using HD wallet
- SLIP-0017: ECDH for encryption/key agreement
- Can be used for SSH, GPG, and other identity protocols

---

## BIP85 Entropy Derivation

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `get_bip85_pubkey` | ❌ | - | Get RSA public key derived via BIP85 |
| `sign_bip85_digests` | ❌ | - | Sign digests with BIP85-derived RSA key |
| `get_bip85_bip39_entropy` | ❌ | - | Derive child BIP39 mnemonic (debug builds) |
| `get_bip85_rsa_entropy` | ❌ | - | Get RSA key entropy via BIP85 (debug builds) |

**Implementation notes:**
- BIP85 allows deriving child seeds from master seed
- Can generate new wallets, RSA keys, etc.
- Some methods only available in debug firmware builds

---

## OTP (One-Time Passwords)

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `register_otp` | ❌ | - | Register TOTP/HOTP secret |
| `get_otp_code` | ❌ | - | Generate OTP code for 2FA |

**Implementation notes:**
- Jade can function as hardware 2FA token
- Supports both TOTP (time-based) and HOTP (counter-based)
- OTP secrets stored securely on device

---

## Hardware Attestation

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `register_attestation` | ❌ | - | Register attestation data (factory provisioning) |
| `sign_attestation` | ❌ | - | Sign challenge to prove device authenticity |

**Implementation notes:**
- Only available on ESP32-S3 production units
- Uses hardware RSA key for attestation
- Verifies device is genuine Blockstream Jade

---

## OTA Firmware Updates

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `ota` | ❌ | - | Start OTA firmware update |
| `ota_delta` | ❌ | - | Start delta firmware update (smaller download) |
| `ota_data` | ❌ | - | Upload firmware data chunk (streaming) |
| `ota_complete` | ❌ | - | Finalize OTA update |

**Implementation notes:**
- Firmware is uploaded in chunks via `ota_data`
- Delta updates only send changed portions
- Device validates signature before applying

---

## Utility & Protocol Methods

| Method | Status | C# Method | Description |
|--------|--------|-----------|-------------|
| `cancel` | ❌ | - | Cancel ongoing operation |
| `pin` | ❌ | - | Internal PIN entry message (protocol phase) |
| `http_request` | 🚧 | (internal) | HTTP request proxy (used internally by `auth_user`) |

**Implementation notes:**
- `http_request` is handled internally during authentication
- `cancel` can abort long-running operations
- `pin` is part of the authentication handshake

---

## Debug Methods (DEBUG Builds Only)

These methods are only available in debug firmware builds and are not intended for production use.

| Method | Status | Description |
|--------|--------|-------------|
| `debug_selfcheck` | ❌ | Run device self-check tests |
| `debug_clean_reset` | ❌ | Factory reset device |
| `debug_set_mnemonic` | ❌ | Set mnemonic (debug variant) |
| `debug_handshake` | ❌ | Debug handshake test |
| `debug_scan_qr` | ❌ | Scan QR code (debug) |
| `debug_capture_image_data` | ❌ | Capture camera image (ESP32-S3 with camera) |

---

## Implementation Priority Recommendations

### Phase 3: Core Wallet Operations (High Priority)
1. `sign_message` - Message signing is commonly needed
2. `sign_psbt` - Modern transaction signing standard
3. `sign_tx` + `tx_input` + `get_signature` - Legacy transaction signing
4. `get_xpubs` - Batch xpub retrieval for efficiency

### Phase 4: Multisig Support
1. `register_multisig` - Register multisig wallets
2. `get_registered_multisigs` - List registered wallets
3. `get_registered_multisig` - Get wallet details
4. `register_multisig_file` - File-based registration

### Phase 5: Advanced Features
1. Output descriptor methods
2. Identity methods (SLIP-0013/0017)
3. OTP methods
4. BIP85 methods

### Phase 6: Liquid Network
1. All Liquid blinding methods
2. `sign_liquid_tx`

### Phase 7: Device Management
1. OTA update methods
2. Hardware attestation
3. `set_mnemonic` / `set_epoch`

---

## Protocol Notes

### Anti-Exfil Protocol
Some signing methods support the anti-exfil protocol which protects against a compromised device RNG leaking private key material through signatures. When enabled:
1. Host provides commitment to randomness
2. Device returns signature with host randomness incorporated
3. Host can verify the device used the provided randomness

### Streaming Protocol
Large data (transactions, firmware) uses a streaming protocol:
1. Initial call sets up the operation
2. Multiple `*_data` or `*_input` calls send chunks
3. Final call completes the operation

### HTTP Request Proxy
The `auth_user` method uses an HTTP request/response pattern where:
1. Device returns `http_request` with URLs and data
2. Host makes HTTP request to PIN server
3. Host sends response back via the `on_reply` method
4. Loop continues until authentication completes

---

## References

- [Jade Firmware Source](https://github.com/Blockstream/Jade)
- [Jade Python Client (jadepy)](https://github.com/Blockstream/Jade/tree/master/jadepy)
- [BIP32 - HD Wallets](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki)
- [BIP44 - Multi-Account Hierarchy](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki)
- [BIP84 - Native SegWit](https://github.com/bitcoin/bips/blob/master/bip-0084.mediawiki)
- [BIP85 - Deterministic Entropy](https://github.com/bitcoin/bips/blob/master/bip-0085.mediawiki)
- [SLIP-0013 - Authentication](https://github.com/satoshilabs/slips/blob/master/slip-0013.md)
- [SLIP-0017 - ECDH](https://github.com/satoshilabs/slips/blob/master/slip-0017.md)
- [SLIP-0077 - Master Blinding Key](https://github.com/satoshilabs/slips/blob/master/slip-0077.md)
- [PSBT - BIP174](https://github.com/bitcoin/bips/blob/master/bip-0174.mediawiki)
