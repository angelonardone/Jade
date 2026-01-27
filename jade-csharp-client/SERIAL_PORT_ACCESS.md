# Serial Port Access for JadeClient

This guide explains how to access serial ports when running JadeClient applications on different operating systems.

## Cross-Platform Port Detection

The JadeClient library automatically detects the operating system and searches for Jade devices using OS-specific patterns:

| OS | Detection Patterns | Example Ports |
|----|-------------------|---------------|
| Windows | All COM ports (except COM1) | `COM3`, `COM4` |
| macOS | usbserial, usbmodem, wchusbserial, SLAB_USB | `/dev/cu.usbserial-XXXXX` |
| Linux | ttyUSB, ttyACM | `/dev/ttyUSB0`, `/dev/ttyACM0` |

### Using the API

```csharp
using JadeClient.Transport;

// Get current platform
var platform = SerialTransport.GetCurrentPlatform();
Console.WriteLine($"Running on: {platform}");

// Auto-detect Jade device
string? port = SerialTransport.FindJadePort();
if (port != null)
{
    using var transport = new SerialTransport(port);
    await transport.ConnectAsync();
    // ... use the device
}

// Or list all candidate ports
string[] jadePorts = SerialTransport.DiscoverJadePorts();
foreach (var p in jadePorts)
    Console.WriteLine($"  - {p}");

// Get OS-specific help text
Console.WriteLine(SerialTransport.GetPortNamingHelp());
```

---

## Linux: Permission Denied Error

On Linux, serial ports are owned by the `dialout` group. If you see:

```
Error: Failed to connect to serial port '/dev/ttyACM0'
Inner: Access to the port '/dev/ttyACM0' is denied.
```

You have two options:

### Option 1: Add User to dialout Group (Recommended)

Requires sudo access (one-time setup):

```bash
sudo usermod -aG dialout $USER
```

Then **logout and login** (or reboot) for the change to take effect.

Verify with:
```bash
groups
# Should include 'dialout'
```

### Option 2: Run via Docker (No sudo required)

Use Docker to run your application with device access:

```bash
# Run HsmTest sample
docker run --rm -it \
  -v /home/angelo/Code/Jade/jade-csharp-client:/app \
  -w /app/samples/HsmTest \
  --device=/dev/ttyACM0 \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run

# Run BasicUsage sample
docker run --rm -it \
  -v /home/angelo/Code/Jade/jade-csharp-client:/app \
  -w /app/samples/BasicUsage \
  --device=/dev/ttyACM0 \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run

# Run HSMbridge
docker run --rm -it \
  -v /home/angelo/Code/Jade/jade-csharp-client:/app \
  -w /app/HSMbridge \
  --device=/dev/ttyACM0 \
  -p 5000:5000 \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run
```

**Note**: Replace `/dev/ttyACM0` with your actual device path. Use `ls /dev/ttyACM* /dev/ttyUSB*` to find it.

---

## Windows: COM Port Access

On Windows, serial ports are generally accessible without special permissions.

### Find Your Device

1. Open **Device Manager**
2. Expand **Ports (COM & LPT)**
3. Look for "USB Serial" or "CH9102" - note the COM port number

### Run the Application

```powershell
cd jade-csharp-client\samples\HsmTest
dotnet run

# Or specify port explicitly
dotnet run COM3
```

---

## macOS: USB Serial Access

On macOS, USB serial devices are usually accessible without special permissions.

### Find Your Device

```bash
ls /dev/cu.usbserial* /dev/cu.wchusbserial* /dev/cu.SLAB*
```

### Run the Application

```bash
cd jade-csharp-client/samples/HsmTest
dotnet run

# Or specify port explicitly
dotnet run /dev/cu.usbserial-59010065821
```

---

## WSL2: USB Passthrough

When using Windows Subsystem for Linux (WSL2), USB devices must be explicitly attached.

### Prerequisites

Install usbipd-win on Windows:
```powershell
winget install usbipd
```

### Attach USB to WSL

In **Windows PowerShell (as Administrator)**:

```powershell
# List USB devices
usbipd list

# Bind device (one-time)
usbipd bind --busid <BUSID>

# Attach to WSL (required after each reboot/reconnect)
usbipd attach --wsl --busid <BUSID>
```

### Run in WSL

Once attached, the device appears in WSL:

```bash
ls /dev/ttyACM*
# /dev/ttyACM0

# Run via Docker (recommended if not in dialout group)
docker run --rm -it \
  -v $(pwd):/app \
  -w /app/samples/HsmTest \
  --device=/dev/ttyACM0 \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run
```

See [USB_PASSTHROUGH_WSL.md](../USB_PASSTHROUGH_WSL.md) for detailed instructions.

---

## Troubleshooting

### "No Jade device found"

1. Check device is connected: `ls /dev/tty*` (Linux/macOS) or Device Manager (Windows)
2. Try unplugging and replugging the USB cable
3. On WSL2, ensure USB is attached: `usbipd attach --wsl --busid <BUSID>`

### "Access denied" on Linux

- Add yourself to dialout group, OR
- Run via Docker with `--device` flag

### "Port is busy"

Another application is using the port. Close any:
- Serial monitors (Arduino IDE, screen, minicom)
- Other JadeClient instances
- Jade firmware flash tools

### Device path changed

USB serial device paths can change. Always use auto-detection or verify the path:

```bash
# Linux
ls /dev/ttyACM* /dev/ttyUSB*

# macOS
ls /dev/cu.*

# Windows (PowerShell)
Get-WmiObject Win32_SerialPort | Select-Object DeviceID, Description
```

---

## Command-Line Arguments

All samples accept an optional port name argument:

```bash
# Auto-detect (default)
dotnet run

# Specify port explicitly
dotnet run /dev/ttyACM0      # Linux
dotnet run /dev/cu.usbserial-XXXXX  # macOS
dotnet run COM3              # Windows
```
