using HSMbridge;
using HSMbridge.Services;
using JadeClient.Protocol;
using JadeClient.Transport;
using JadeClient.PinServer;
using JadeClient.Models;
using Microsoft.OpenApi.Models;

Console.WriteLine("HSMbridge - Jade HSM REST API Bridge");
Console.WriteLine("=====================================\n");

// Load configuration
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration.GetSection(HSMbridgeOptions.SectionName).Get<HSMbridgeOptions>() ?? new HSMbridgeOptions();

Console.WriteLine($"Configuration:");
Console.WriteLine($"  Port: {config.Port}");
Console.WriteLine($"  Network: {config.Network}");
Console.WriteLine($"  Swagger: {(config.EnableSwagger ? "Enabled" : "Disabled")}");
Console.WriteLine($"  HSM Activation Timeout: {config.HsmActivationTimeoutSeconds}s\n");

// Find Jade device
Console.WriteLine("Searching for Jade device...");
Console.WriteLine($"  Platform: {SerialTransport.GetCurrentPlatform()}\n");

string? jadePort = config.SerialPort;
SerialTransport? transport = null;
JadeRpc? rpc = null;
VersionInfo? info = null;

if (!string.IsNullOrEmpty(jadePort))
{
    // User specified a port, try it directly
    Console.WriteLine($"Using configured port: {jadePort}");
    transport = new SerialTransport(jadePort);
    rpc = new JadeRpc(transport);
}
else
{
    // Use library method to probe all ports and find the actual Jade device
    var probeResult = await SerialTransport.FindAndVerifyJadeAsync(
        probeTimeout: TimeSpan.FromSeconds(5),
        progress: new Progress<string>(msg => Console.WriteLine($"  {msg}")));

    if (probeResult == null)
    {
        Console.WriteLine("\nERROR: No Jade device found on any port.");
        Console.WriteLine("Please connect your Jade device via USB and try again.\n");
        Console.WriteLine($"{SerialTransport.GetPortNamingHelp()}");
        Environment.Exit(1);
        return; // Unreachable but satisfies compiler null analysis
    }

    jadePort = probeResult.PortName;
    transport = probeResult.Transport;
    info = probeResult.VersionInfo; // Already have device info from probing
    rpc = new JadeRpc(transport);
    Console.WriteLine();
}

try
{
    // Connect if not already connected (when port was specified via config)
    if (!transport!.IsConnected)
    {
        Console.WriteLine($"Connecting to Jade on {jadePort}...");
        await transport.ConnectAsync();
    }
    Console.WriteLine($"Connected to Jade on {jadePort}!\n");

    // Get device info (only if we don't already have it from probing)
    if (info == null)
    {
        Console.WriteLine("Getting device info...");
        info = await rpc!.GetVersionInfoAsync();
    }
    Console.WriteLine($"Device info:");
    Console.WriteLine($"  Firmware: {info.JadeVersion}");
    Console.WriteLine($"  State: {info.State}");
    Console.WriteLine($"  Has PIN: {info.HasPin}\n");

    // Unlock the device if needed
    if (info.State == JadeState.Locked || info.State == JadeState.Temp)
    {
        Console.WriteLine("Device is locked. Starting authentication...");
        Console.WriteLine("Please enter your PIN on the Jade device.\n");

        var pinServer = new RemotePinServerHandler();
        var authResult = await rpc.AuthUserAsync(pinServer, config.Network);
        if (!authResult)
        {
            Console.WriteLine("ERROR: Authentication failed!");
            Environment.Exit(1);
        }
        Console.WriteLine("Authentication successful!\n");
    }
    else if (info.State == JadeState.Ready)
    {
        Console.WriteLine("Device is already unlocked.\n");
    }
    else if (info.State == JadeState.Uninit)
    {
        Console.WriteLine("ERROR: Device is not initialized.");
        Console.WriteLine("Please set up your Jade first.");
        Environment.Exit(1);
    }

    // Check HSM status and wait for activation
    Console.WriteLine("Checking HSM status...");
    var hsmInfo = await rpc.HsmGetInfoAsync();

    if (!hsmInfo.Active)
    {
        Console.WriteLine("HSM mode is NOT active.\n");
        Console.WriteLine("*** ACTION REQUIRED ***");
        Console.WriteLine("Please activate HSM mode on your Jade device:");
        Console.WriteLine("  1. Press the button on Jade to go to the menu");
        Console.WriteLine("  2. Select 'Session'");
        Console.WriteLine("  3. Select 'HSM Mode'");
        Console.WriteLine("  4. Confirm activation\n");
        Console.WriteLine($"Waiting for HSM mode activation (timeout: {config.HsmActivationTimeoutSeconds}s)...");

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(config.HsmActivationTimeoutSeconds);
        int dots = 0;

        while (!hsmInfo.Active && (DateTime.UtcNow - startTime) < timeout)
        {
            await Task.Delay(2000);
            hsmInfo = await rpc.HsmGetInfoAsync();

            Console.Write(".");
            dots++;
            if (dots % 30 == 0)
                Console.WriteLine();
        }

        Console.WriteLine();

        if (!hsmInfo.Active)
        {
            Console.WriteLine("\nERROR: Timeout waiting for HSM mode activation.");
            Console.WriteLine("Please run HSMbridge again after activating HSM mode.");
            Environment.Exit(1);
        }
    }

    Console.WriteLine("\nHSM mode is ACTIVE!");
    Console.WriteLine($"  Networks: {string.Join(", ", hsmInfo.Networks)}");
    Console.WriteLine($"  Mainnet Path: {hsmInfo.MainnetRootPath}");
    Console.WriteLine($"  Testnet Path: {hsmInfo.TestnetRootPath}");
    Console.WriteLine($"  Operations Count: {hsmInfo.OperationsCount}\n");

    // Create and configure the HSM service
    var hsmService = new JadeHsmService(rpc);
    hsmService.SetDeviceVersion(info.JadeVersion);
    hsmService.SetHsmActive(true);

    // Configure ASP.NET Core services
    builder.Services.AddSingleton<IJadeHsmService>(hsmService);
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    if (config.EnableSwagger)
    {
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "HSMbridge API",
                Version = "v1",
                Description = "REST API for Jade HSM operations"
            });
        });
    }

    // Configure Kestrel
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(config.Port);
    });

    var app = builder.Build();

    if (config.EnableSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "HSMbridge API v1");
        });
    }

    app.MapControllers();

    Console.WriteLine("=====================================");
    Console.WriteLine($"HSMbridge REST API is now running!");
    Console.WriteLine($"  Base URL: http://localhost:{config.Port}");
    if (config.EnableSwagger)
        Console.WriteLine($"  Swagger UI: http://localhost:{config.Port}/swagger");
    Console.WriteLine("=====================================\n");
    Console.WriteLine("Available endpoints:");
    Console.WriteLine($"  GET  /health                          - Health check");
    Console.WriteLine($"  GET  /api/hsm/info                    - HSM status");
    Console.WriteLine($"  GET  /api/hsm/pubkey/{{network}}/{{index}} - Get public key");
    Console.WriteLine($"  GET  /api/hsm/xpub/{{network}}           - Get extended public key");
    Console.WriteLine($"  POST /api/hsm/sign                    - Sign a hash");
    Console.WriteLine($"  POST /api/hsm/ecdh                    - Compute ECDH shared secret");
    Console.WriteLine($"  POST /api/hsm/encrypt                 - ECIES encryption");
    Console.WriteLine($"  POST /api/hsm/decrypt                 - ECIES decryption");
    Console.WriteLine($"  POST /api/hsm/lock                    - Lock HSM mode");
    Console.WriteLine("\nPress Ctrl+C to stop the server.\n");

    // Handle graceful shutdown
    var lifetime = app.Lifetime;
    lifetime.ApplicationStopping.Register(() =>
    {
        Console.WriteLine("\nShutting down...");
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"\nERROR: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
}
finally
{
    Console.WriteLine("\nDisconnecting from Jade...");
    await transport.DisconnectAsync();
    rpc.Dispose();
    transport.Dispose();
    Console.WriteLine("Goodbye!");
}
