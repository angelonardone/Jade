# USB Passthrough for WSL Development

This guide explains how to pass USB devices from Windows to WSL2 for flashing the Jade firmware.

## Overview

- **Tool**: `usbipd-win` (USB/IP Device Sharing)
- **Device**: M5Stack Black (USB-Enhanced-SERIAL CH9102)
- **BUSID**: `1-8` (may vary - check with `usbipd list`)

## Important Notes

- When USB is attached to WSL, **Windows cannot access it**
- When USB is attached to Windows, **WSL cannot access it**
- The `bind` command is permanent (one-time setup)
- The `attach` command must be run after every reboot or USB reconnect

### USB Access Summary

| USB State | Windows Access | WSL Access | Device Path |
|-----------|----------------|------------|-------------|
| Normal / Detached | Yes (COM4) | No | - |
| Attached to WSL | No | Yes | `/dev/ttyACM0` |

## Commands Reference

All commands run in **Windows PowerShell (as Administrator)**:

### First-Time Setup (One-Time)

```powershell
# Install usbipd (if not already installed)
winget install usbipd

# Bind the device (permanent, survives reboots)
usbipd bind --busid 1-8
```

### Daily Usage

#### To use USB in WSL (for flashing firmware):

```powershell
# Attach to WSL
usbipd attach --wsl --busid 1-8
```

Then in WSL, the device appears at `/dev/ttyACM0`

#### To return USB to Windows:

```powershell
# Detach from WSL (returns to Windows)
usbipd detach --busid 1-8
```

Or simply unplug and replug the USB cable.

### Check Status

```powershell
# List all USB devices and their state
usbipd list
```

Output example:
```
BUSID  VID:PID    DEVICE                                    STATE
1-8    1a86:55d4  USB-Enhanced-SERIAL CH9102 (COM4)         Attached
```

States:
- `Not shared` - Not bound, Windows only
- `Shared` - Bound but not attached, Windows can use
- `Attached` - In use by WSL, Windows cannot access

## Quick Reference Card

| Task | Command (PowerShell Admin) |
|------|---------------------------|
| List devices | `usbipd list` |
| Send to WSL | `usbipd attach --wsl --busid 1-8` |
| Return to Windows | `usbipd detach --busid 1-8` |

## Building and Flashing (WSL)

Once USB is attached to WSL:

```bash
cd ~/Code/Jade

# Build firmware
docker run --rm -v /home/angelo/Code/Jade:/jade -w /jade jade_builder \
  bash -c ". /root/esp/esp-idf/export.sh && idf.py build"

# Flash firmware
docker run --rm -v /home/angelo/Code/Jade:/jade -w /jade --device=/dev/ttyACM0 jade_builder \
  bash -c ". /root/esp/esp-idf/export.sh && idf.py -p /dev/ttyACM0 flash"

# Flash and monitor (see serial output)
docker run --rm -it -v /home/angelo/Code/Jade:/jade -w /jade --device=/dev/ttyACM0 jade_builder \
  bash -c ". /root/esp/esp-idf/export.sh && idf.py -p /dev/ttyACM0 flash monitor"
```

To exit monitor: `Ctrl+]`

## Troubleshooting

### "Device not found" in WSL
```bash
ls /dev/ttyACM* /dev/ttyUSB*
```
If nothing shows, re-run: `usbipd attach --wsl --busid 1-8`

### BUSID changed
The BUSID may change if you use a different USB port. Run `usbipd list` to find the new BUSID.

### Permission denied in WSL
The device is owned by `root:dialout`. Using Docker with `--device` flag handles this automatically.

### Device constantly reboots after flashing
Check monitor output for panic messages:
```bash
docker run --rm -it -v /home/angelo/Code/Jade:/jade -w /jade --device=/dev/ttyACM0 jade_builder \
  bash -c ". /root/esp/esp-idf/export.sh && idf.py -p /dev/ttyACM0 monitor"
```
