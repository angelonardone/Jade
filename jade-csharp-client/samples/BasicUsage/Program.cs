using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.Protocol;
using JadeClient.Transport;

Console.WriteLine("JadeClient C# Library - Device Test");
Console.WriteLine("=====================================");
Console.WriteLine();

// List available serial ports
var ports = SerialTransport.GetAvailablePorts();
Console.WriteLine("Available serial ports:");
foreach (var port in ports)
{
    Console.WriteLine($"  - {port}");
}
Console.WriteLine();

// Connect to the Jade device
string portName = "/dev/cu.usbserial-59010065821";
Console.WriteLine($"Connecting to Jade on {portName}...");

try
{
    using var transport = new SerialTransport(portName);
    using var rpc = new JadeRpc(transport);

    await rpc.ConnectAsync();
    Console.WriteLine("Connected!");

    // Get version info - the basic Phase 1 deliverable
    Console.WriteLine("\nFetching device info...");
    VersionInfo version = await rpc.GetVersionInfoAsync();

    Console.WriteLine("\nJade Device Information:");
    Console.WriteLine($"  Firmware Version : {version.JadeVersion}");
    Console.WriteLine($"  Board Type       : {version.BoardType}");
    Console.WriteLine($"  Configuration    : {version.Config}");
    Console.WriteLine($"  Features         : {version.Features}");
    Console.WriteLine($"  MAC Address      : {version.EfuseMac}");
    Console.WriteLine($"  State            : {version.State}");
    Console.WriteLine($"  Networks         : {version.Networks}");
    Console.WriteLine($"  Has PIN          : {version.HasPin}");
    Console.WriteLine($"  Has Wallet       : {version.HasWallet}");
    Console.WriteLine($"  Is Unlocked      : {version.IsUnlocked}");

    // Add some entropy (optional)
    Console.WriteLine("\nAdding entropy to device RNG...");
    var entropy = new byte[32];
    Random.Shared.NextBytes(entropy);
    var entropyResult = await rpc.AddEntropyAsync(entropy);
    Console.WriteLine($"Add entropy result: {entropyResult}");

    await rpc.DisconnectAsync();
    Console.WriteLine("\nDisconnected from Jade.");
    Console.WriteLine("\nPhase 1 Protocol Test: SUCCESS!");
}
catch (JadeConnectionException ex)
{
    Console.WriteLine($"\nConnection error: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
}
catch (JadeRpcException ex)
{
    Console.WriteLine($"\nRPC error {ex.ErrorCode}: {ex.Message}");
    if (ex.IsDeviceLocked)
        Console.WriteLine("  -> Device is locked. Authentication required.");
    if (ex.IsUserCancelled)
        Console.WriteLine("  -> User cancelled the operation.");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"\nTimeout: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
}
