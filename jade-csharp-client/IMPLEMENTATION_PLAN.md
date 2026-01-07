# Implementation Plan

This document outlines the phased implementation approach for the Jade C# Client Library.

## Phase 1: Core Infrastructure (Foundation)

**Goal:** Establish the basic communication layer and project structure.

### Tasks

1. **Project Setup**
   - Create .NET 6.0 class library project
   - Configure NuGet dependencies (PeterO.Cbor, System.IO.Ports)
   - Set up unit test project
   - Configure CI/CD (optional)

2. **Transport Layer**
   - [x] `IJadeTransport` interface
     ```csharp
     public interface IJadeTransport
     {
         Task ConnectAsync(CancellationToken ct = default);
         Task DisconnectAsync();
         Task<byte[]> ReadAsync(CancellationToken ct = default);
         Task WriteAsync(byte[] data, CancellationToken ct = default);
         bool IsConnected { get; }
     }
     ```
   - [x] `SerialTransport` implementation using System.IO.Ports
   - [x] Unit tests with mock transport

3. **CBOR Serialization**
   - [x] `CborSerializer` helper class
   - [x] Request/Response model classes
   - [x] Error handling and parsing

4. **Basic RPC Layer**
   - [x] `JadeRpc` class for low-level RPC calls
   - [x] Request ID generation
   - [x] Timeout handling
   - [x] Error response parsing

### Deliverables
- Working serial connection to Jade
- Ability to send/receive raw CBOR messages
- `get_version_info` working end-to-end

### Estimated Effort: 2-3 days

---

## Phase 2: Authentication & Session Management

**Goal:** Implement user authentication flow including pinserver proxy.

### Tasks

1. **HTTP Request Proxy**
   - [ ] `IHttpProxy` interface for HTTP requests
   - [ ] Default implementation using HttpClient
   - [ ] Configurable for custom implementations

2. **Authentication Flow**
   - [ ] `auth_user` method implementation
   - [ ] Handle `http_request` responses
   - [ ] Pinserver communication loop
   - [ ] Session state management

3. **Session Management**
   - [ ] `logout` method
   - [ ] Track authentication state
   - [ ] Auto-reconnect handling

4. **Custom Pinserver Support**
   - [ ] `update_pinserver` method
   - [ ] `reset_pinserver` method

### Deliverables
- Complete authentication flow
- Device unlock with PIN
- Pinserver configuration

### Estimated Effort: 2-3 days

---

## Phase 3: Key Derivation & Address Generation

**Goal:** Implement key derivation and address generation methods.

### Tasks

1. **Path Utilities**
   - [ ] BIP32 path parser ("m/84'/0'/0'" -> uint[])
   - [ ] Hardened derivation helpers
   - [ ] Path validation

2. **Key Methods**
   - [ ] `get_xpub` - Get extended public key
   - [ ] `get_receive_address` - Get receive address
   - [ ] `get_identity_pubkey` - Get identity public key

3. **Address Variants**
   - [ ] Support for P2PKH (`pkh(k)`)
   - [ ] Support for P2WPKH (`wpkh(k)`)
   - [ ] Support for P2SH-P2WPKH (`sh(wpkh(k))`)

### Deliverables
- Full xpub retrieval
- Address generation and display
- BIP32 path handling

### Estimated Effort: 1-2 days

---

## Phase 4: Transaction Signing

**Goal:** Implement PSBT and legacy transaction signing.

### Tasks

1. **PSBT Signing**
   - [ ] `sign_psbt` method
   - [ ] Handle multi-input transactions
   - [ ] Progress reporting for large transactions

2. **Legacy Transaction Signing**
   - [ ] `sign_tx` method
   - [ ] Input-by-input signing flow
   - [ ] Input data handling (`tx_input` messages)

3. **Change Detection**
   - [ ] Change output identification
   - [ ] Path validation for change

4. **Anti-Exfil (Optional)**
   - [ ] Host commitment generation
   - [ ] Signature verification

### Deliverables
- Complete PSBT signing
- Legacy transaction support
- Secure signing with change verification

### Estimated Effort: 3-4 days

---

## Phase 5: Message Signing

**Goal:** Implement message signing functionality.

### Tasks

1. **Message Signing**
   - [ ] `sign_message` method
   - [ ] Support for different message formats
   - [ ] Signature encoding (base64, hex)

2. **Identity Operations**
   - [ ] `sign_identity` method
   - [ ] `get_identity_shared_key` method

### Deliverables
- Message signing capability
- Identity key operations

### Estimated Effort: 1 day

---

## Phase 6: Multisig & Descriptors

**Goal:** Implement multisig wallet management.

### Tasks

1. **Multisig Registration**
   - [ ] `register_multisig` method
   - [ ] Signer structure handling
   - [ ] Threshold configuration

2. **Multisig Queries**
   - [ ] `get_registered_multisigs` method
   - [ ] `get_registered_multisig` method (single)

3. **Descriptor Wallets**
   - [ ] `register_descriptor` method
   - [ ] `get_registered_descriptors` method
   - [ ] `get_registered_descriptor` method

### Deliverables
- Full multisig support
- Descriptor wallet management

### Estimated Effort: 2 days

---

## Phase 7: Bluetooth LE Support (Optional)

**Goal:** Add Bluetooth connectivity for wireless operation.

### Tasks

1. **BLE Transport**
   - [ ] `BleTransport` implementation
   - [ ] Device discovery/scanning
   - [ ] Connection management
   - [ ] MTU negotiation

2. **Platform Abstraction**
   - [ ] Windows BLE (Windows.Devices.Bluetooth)
   - [ ] Cross-platform option (Plugin.BLE)

### Deliverables
- Wireless Jade connectivity
- Device discovery

### Estimated Effort: 3-4 days

---

## Phase 8: High-Level API & Polish

**Goal:** Create a user-friendly high-level API.

### Tasks

1. **JadeClient Facade**
   - [ ] Simple, intuitive API surface
   - [ ] Automatic session management
   - [ ] Error handling and retries

2. **Documentation**
   - [ ] XML documentation comments
   - [ ] API reference generation
   - [ ] Usage examples

3. **NuGet Package**
   - [ ] Package configuration
   - [ ] README and icon
   - [ ] Publish to NuGet.org

### Deliverables
- Production-ready library
- Published NuGet package
- Complete documentation

### Estimated Effort: 2 days

---

## Testing Strategy

### Unit Tests
- Mock transport for protocol testing
- CBOR serialization verification
- Error handling validation

### Integration Tests
- Real device connection (manual)
- Full workflow tests
- Edge case handling

### Test Device Setup
- M5Stack with Jade firmware (debug mode)
- Test wallet with testnet coins

---

## Dependencies Summary

```xml
<ItemGroup>
    <!-- Required -->
    <PackageReference Include="PeterO.Cbor" Version="4.5.2" />
    <PackageReference Include="System.IO.Ports" Version="8.0.0" />

    <!-- Optional: BLE Support -->
    <PackageReference Include="Plugin.BLE" Version="3.0.0" />

    <!-- Testing -->
    <PackageReference Include="xunit" Version="2.6.0" />
    <PackageReference Include="Moq" Version="4.20.0" />
</ItemGroup>
```

---

## GeneXus Integration Notes

For integration with GeneXus .NET applications:

1. **Assembly Reference**
   - Add compiled DLL as external assembly
   - Or reference via NuGet if published

2. **External Object Definition**
   - Create External Object in GeneXus
   - Map JadeClient methods

3. **Async Handling**
   - GeneXus .NET supports async/await
   - Use `Task.Result` for synchronous contexts if needed

4. **Error Handling**
   - JadeException maps to GeneXus error handling
   - Use try/catch in procedures

---

## Milestones

| Milestone | Phases | Target |
|-----------|--------|--------|
| MVP | 1-4 | Basic signing capability |
| Feature Complete | 1-6 | All RPC methods |
| Production Ready | 1-8 | Published package |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| CBOR compatibility issues | Use well-tested PeterO.Cbor library |
| Serial port access on different OS | Test on Windows, macOS, Linux |
| BLE complexity | Make BLE optional, serial-first approach |
| Pinserver changes | Abstract HTTP proxy, allow custom implementation |
