# Testing Jade C# Client Without Physical Hardware

This guide explains how to test the Jade C# client library without a physical Jade device using the QEMU emulator.

## Overview

The Jade project provides two main ways to test without hardware:

1. **QEMU Emulator** - Full ESP32 firmware emulation with TCP networking
2. **libjade** - Native in-process software emulator (Python only)

For C# client testing, we use the **QEMU emulator** which exposes a TCP port that the C# client can connect to.

## Prerequisites

- Docker installed and running
- .NET 8.0 SDK
- Git (to clone the Jade repository)

## Quick Start

### 1. Build the QEMU Docker Image

From the Jade repository root:

```bash
# Build the QEMU emulator image
docker build -t jade-qemu -f Dockerfile.qemu \
  --build-arg SDK_CONFIG=configs/sdkconfig_qemu.defaults .
```

This builds the Jade firmware for QEMU and creates a Docker image that runs the emulator.

### 2. Run the QEMU Emulator

```bash
# Start the QEMU emulator (TCP port 30121)
docker run --rm -p 30121:30121 jade-qemu
```

The emulator will start and listen on port 30121 for connections.

### 3. Connect with C# Client

```csharp
using JadeClient.Transport;
using JadeClient.Protocol;

// Connect to QEMU emulator
using var transport = new TcpTransport("localhost", 30121);
using var rpc = new JadeRpc(transport);

await rpc.ConnectAsync();

// Get device info
var version = await rpc.GetVersionInfoAsync();
Console.WriteLine($"Firmware: {version.JadeVersion}");
Console.WriteLine($"Board: {version.BoardType}");
```

Or use the provided sample:

```bash
cd jade-csharp-client/samples/QemuTest
dotnet run
```

## TcpTransport Usage

The `TcpTransport` class provides TCP connectivity to QEMU or remote Jade devices.

### Basic Usage

```csharp
using JadeClient.Transport;

// Connect to local QEMU
using var transport = new TcpTransport("localhost", 30121);

// Or use the factory method
using var transport = TcpTransport.CreateForQemu();

// Or parse a connection string (compatible with Python client format)
using var transport = TcpTransport.FromConnectionString("tcp:localhost:30121");
```

### Connection String Formats

The `FromConnectionString` method supports these formats:

- `host:port` - e.g., `localhost:30121`
- `tcp:host:port` - e.g., `tcp:192.168.1.100:30121`
- `host` - Uses default port 30121

## QEMU CI Mode

The QEMU emulator runs in **CI (Continuous Integration) mode** which:

- Auto-accepts all user prompts
- Enables debug message handlers
- Runs without user interaction

This is ideal for automated testing but means some flows (like PIN entry) behave differently than on real hardware.

## Advanced QEMU Options

### With Web Display

Build with web display support to see the emulated screen:

```bash
# First, check if webdisplay config exists
ls configs/sdkconfig_qemu*webdisplay*.defaults

# If it exists, build with it:
docker build -t jade-qemu-web -f Dockerfile.qemu \
  --build-arg SDK_CONFIG=configs/sdkconfig_qemu_psram_webdisplay.defaults .

# Run with both ports
docker run --rm -p 30121:30121 -p 30122:30122 jade-qemu-web
```

Then open `http://localhost:30122` to see the display.

### Using Pre-built Image

If available, you can pull the pre-built image:

```bash
docker pull blockstream/verde
docker tag blockstream/verde jade-qemu
```

### Manual QEMU Setup (Without Docker)

If you have ESP-IDF and QEMU installed locally:

```bash
# Copy config
cp configs/sdkconfig_qemu.defaults sdkconfig.defaults

# Build firmware
idf.py build

# Create flash image and run
./main/qemu/make-flash-img.sh
./main/qemu/qemu_run.sh
```

## Testing Workflow

### Unit Tests (No QEMU Required)

The C# client includes unit tests with mocked transports:

```bash
cd jade-csharp-client
dotnet test
```

### Integration Tests (QEMU Required)

1. Start QEMU in one terminal:
   ```bash
   docker run --rm -p 30121:30121 jade-qemu
   ```

2. Run the QemuTest sample in another terminal:
   ```bash
   cd jade-csharp-client/samples/QemuTest
   dotnet run
   ```

### Python Test Suite

For comprehensive testing, use the Python test suite:

```bash
# From Jade repository root
python test_jade.py --qemu --serialport=tcp:localhost:30121
```

## Troubleshooting

### Connection Refused

- Ensure QEMU Docker container is running
- Check port 30121 is not blocked by firewall
- Verify the port mapping: `docker ps` should show `0.0.0.0:30121->30121/tcp`

### Connection Timeout

- QEMU may take a few seconds to initialize after starting
- Try increasing the connection timeout:
  ```csharp
  var transport = new TcpTransport("localhost", 30121, connectTimeoutMs: 30000);
  ```

### Docker Build Fails

- Ensure you have enough disk space (build requires ~10GB)
- Check Docker daemon is running: `docker info`
- Try pulling the base image first: `docker pull blockstream/verde`

### CBOR Parse Errors

- Ensure you're connecting to the QEMU TCP port (30121), not the web display port (30122)
- The web display port sends image data, not CBOR

## Architecture

```
┌─────────────────┐     TCP      ┌─────────────────┐
│                 │   Port 30121 │                 │
│  C# Client      │◄────────────►│  QEMU Docker    │
│  (TcpTransport) │              │  (ESP32 + Jade) │
│                 │              │                 │
└─────────────────┘              └─────────────────┘
```

The QEMU emulator:
1. Emulates the ESP32 microcontroller
2. Runs the actual Jade firmware
3. Exposes a TCP server on port 30121
4. Speaks the same CBOR-RPC protocol as real hardware

## Comparison: Real Device vs QEMU

| Feature | Real Device | QEMU Emulator |
|---------|-------------|---------------|
| Connection | Serial/USB or BLE | TCP |
| User prompts | Manual interaction | Auto-accepted (CI mode) |
| PIN server | Required for unlock | Bypassed in CI mode |
| Performance | Real-time | Slower (emulated) |
| Crypto | Hardware accelerated | Software emulated |

## See Also

- [main/qemu/README.md](../main/qemu/README.md) - QEMU-specific documentation
- [libjade/README.md](../libjade/README.md) - libjade documentation (Python)
- [DEVELOPMENT_GUIDE.md](../DEVELOPMENT_GUIDE.md) - General development guide
