using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.Protocol;
using JadeClient.Transport;

Console.WriteLine("JadeClient C# Library - QEMU Emulator Test");
Console.WriteLine("===========================================");
Console.WriteLine();

// Default connection settings for QEMU
string host = "localhost";
int port = TcpTransport.DefaultQemuPort;

// Parse command line arguments
if (args.Length > 0)
{
    // Support formats: "host:port", "tcp:host:port", or just "host"
    var arg = args[0];
    if (arg.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        arg = arg.Substring(4);

    var parts = arg.Split(':');
    host = parts[0];
    if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
        port = parsedPort;
}

Console.WriteLine($"Connecting to QEMU emulator at {host}:{port}...");
Console.WriteLine();
Console.WriteLine("NOTE: Make sure QEMU is running with:");
Console.WriteLine("  docker run --rm -p 30121:30121 jade-qemu");
Console.WriteLine();

try
{
    using var transport = new TcpTransport(host, port);
    using var rpc = new JadeRpc(transport);

    // Connect with a timeout
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await rpc.ConnectAsync(cts.Token);
    Console.WriteLine("Connected to QEMU emulator!");

    // Drain any pending data
    rpc.Drain();

    // Get version info
    Console.WriteLine("\nFetching device info...");
    VersionInfo version = await rpc.GetVersionInfoAsync();

    Console.WriteLine("\nJade QEMU Emulator Information:");
    Console.WriteLine($"  Firmware Version : {version.JadeVersion}");
    Console.WriteLine($"  Board Type       : {version.BoardType}");
    Console.WriteLine($"  Configuration    : {version.Config}");
    Console.WriteLine($"  Features         : {version.Features}");
    Console.WriteLine($"  State            : {version.State}");
    Console.WriteLine($"  Networks         : {version.Networks}");

    // Add entropy
    Console.WriteLine("\nAdding entropy to device RNG...");
    var entropy = new byte[32];
    Random.Shared.NextBytes(entropy);
    var entropyResult = await rpc.AddEntropyAsync(entropy);
    Console.WriteLine($"Add entropy result: {entropyResult}");

    // In QEMU CI mode, the device auto-accepts prompts
    // We can test wallet operations without user interaction
    Console.WriteLine("\n--- QEMU CI Mode Tests ---");
    Console.WriteLine("(QEMU runs in CI mode - auto-accepts all prompts)");

    // The QEMU emulator in CI mode should allow us to perform operations
    // that would normally require user confirmation

    // Get updated version info to check state
    version = await rpc.GetVersionInfoAsync();
    Console.WriteLine($"\nDevice state: {version.State}");
    Console.WriteLine($"Is Unlocked : {version.IsUnlocked}");

    if (version.IsUnlocked)
    {
        // Device is unlocked - we can test key derivation
        Console.WriteLine("\n--- Key Derivation Tests ---");

        const uint HARDENED = 0x80000000;

        // Get root xpub
        var rootXpub = await rpc.GetXpubAsync("mainnet", Array.Empty<uint>());
        Console.WriteLine($"\nRoot XPub (m/): {rootXpub}");

        // BIP84 path
        uint[] bip84Path = { 84 + HARDENED, 0 + HARDENED, 0 + HARDENED };
        var bip84Xpub = await rpc.GetXpubAsync("mainnet", bip84Path);
        Console.WriteLine($"BIP84 XPub (m/84'/0'/0'): {bip84Xpub}");
    }
    else
    {
        Console.WriteLine("\nDevice is locked. In CI mode, it may need initialization.");
        Console.WriteLine("For full testing, run the Python test suite against QEMU:");
        Console.WriteLine("  python test_jade.py --qemu --serialport=tcp:localhost:30121");
    }

    await rpc.DisconnectAsync();
    Console.WriteLine("\nDisconnected from QEMU emulator.");
    Console.WriteLine("\nTest Complete!");
}
catch (JadeConnectionException ex)
{
    Console.WriteLine($"\nConnection error: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    Console.WriteLine("\nMake sure QEMU emulator is running:");
    Console.WriteLine("  docker run --rm -p 30121:30121 jade-qemu");
}
catch (JadeRpcException ex)
{
    Console.WriteLine($"\nRPC error {ex.ErrorCode}: {ex.Message}");
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nConnection timed out. Make sure QEMU is running and accessible.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
}
