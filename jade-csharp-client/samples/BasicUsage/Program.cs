using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.PinServer;
using JadeClient.Protocol;
using JadeClient.Transport;

Console.WriteLine("JadeClient C# Library - Device Test");
Console.WriteLine("=====================================");
Console.WriteLine();

// Detect operating system and show platform info
var platform = SerialTransport.GetCurrentPlatform();
Console.WriteLine($"Platform: {platform}");
Console.WriteLine();

// List available serial ports
var allPorts = SerialTransport.GetAvailablePorts();
var jadePorts = SerialTransport.DiscoverJadePorts();

Console.WriteLine("All serial ports:");
foreach (var port in allPorts)
{
    bool isJadeCandidate = jadePorts.Contains(port);
    Console.WriteLine($"  - {port}{(isJadeCandidate ? " (USB-Serial)" : "")}");
}
Console.WriteLine();

// Auto-detect or use command line argument
string? portName = args.Length > 0 ? args[0] : SerialTransport.FindJadePort();

if (portName == null)
{
    Console.WriteLine("ERROR: No Jade device found.");
    Console.WriteLine();
    Console.WriteLine(SerialTransport.GetPortNamingHelp());
    Console.WriteLine();
    Console.WriteLine("Usage: BasicUsage [port_name]");
    Console.WriteLine("  Example (Windows): BasicUsage COM3");
    Console.WriteLine("  Example (macOS):   BasicUsage /dev/cu.usbserial-XXXXX");
    Console.WriteLine("  Example (Linux):   BasicUsage /dev/ttyUSB0");
    return;
}

Console.WriteLine($"Connecting to Jade on {portName}...");

try
{
    using var transport = new SerialTransport(portName);
    using var rpc = new JadeRpc(transport);

    await rpc.ConnectAsync();
    Console.WriteLine("Connected!");

    // Drain any pending data from previous interrupted sessions
    rpc.Drain();

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

    // Test authentication with PIN server (Phase 2A)
    if (version.HasPin)
    {
        Console.WriteLine("\n--- PIN Server Authentication Test ---");
        Console.WriteLine("Using Blockstream's remote PIN server (https://j8d.io)");
        Console.WriteLine("Please enter your PIN on the device when prompted...");

        using var pinServer = new RemotePinServerHandler();
        try
        {
            var authResult = await rpc.AuthUserAsync(pinServer, "mainnet");
            if (authResult)
            {
                Console.WriteLine("Authentication SUCCESS! Device is now unlocked.");

                // Get version info again to confirm unlocked state
                var updatedVersion = await rpc.GetVersionInfoAsync();
                Console.WriteLine($"  Device state: {updatedVersion.State}");
                Console.WriteLine($"  Is Unlocked : {updatedVersion.IsUnlocked}");

                // Get wallet information
                Console.WriteLine("\n--- Wallet Key Information ---");
                const uint HARDENED = 0x80000000;

                // Get root xpub (m/) - the master fingerprint is derived from this
                var rootXpub = await rpc.GetXpubAsync("mainnet", Array.Empty<uint>());
                var masterFingerprint = ExtractFingerprint(rootXpub);
                Console.WriteLine($"Master Fingerprint: {masterFingerprint}");
                Console.WriteLine($"Root XPub (m/): {rootXpub}");

                // Get xpubs for common derivation standards
                Console.WriteLine("\n--- Derivation Paths ---");

                // BIP44 - Legacy P2PKH (addresses starting with '1')
                uint[] bip44Path = { 44 + HARDENED, 0 + HARDENED, 0 + HARDENED };
                var bip44Xpub = await rpc.GetXpubAsync("mainnet", bip44Path);
                Console.WriteLine($"\nBIP44 Legacy P2PKH:");
                Console.WriteLine($"  Path: {FormatBip32Path(bip44Path)}");
                Console.WriteLine($"  XPub: {bip44Xpub}");

                // BIP49 - Nested SegWit P2SH-P2WPKH (addresses starting with '3')
                uint[] bip49Path = { 49 + HARDENED, 0 + HARDENED, 0 + HARDENED };
                var bip49Xpub = await rpc.GetXpubAsync("mainnet", bip49Path);
                Console.WriteLine($"\nBIP49 Nested SegWit (P2SH-P2WPKH):");
                Console.WriteLine($"  Path: {FormatBip32Path(bip49Path)}");
                Console.WriteLine($"  XPub: {bip49Xpub}");

                // BIP84 - Native SegWit P2WPKH (addresses starting with 'bc1q')
                uint[] bip84Path = { 84 + HARDENED, 0 + HARDENED, 0 + HARDENED };
                var bip84Xpub = await rpc.GetXpubAsync("mainnet", bip84Path);
                Console.WriteLine($"\nBIP84 Native SegWit (P2WPKH):");
                Console.WriteLine($"  Path: {FormatBip32Path(bip84Path)}");
                Console.WriteLine($"  XPub: {bip84Xpub}");

                // BIP86 - Taproot P2TR (addresses starting with 'bc1p')
                uint[] bip86Path = { 86 + HARDENED, 0 + HARDENED, 0 + HARDENED };
                var bip86Xpub = await rpc.GetXpubAsync("mainnet", bip86Path);
                Console.WriteLine($"\nBIP86 Taproot (P2TR):");
                Console.WriteLine($"  Path: {FormatBip32Path(bip86Path)}");
                Console.WriteLine($"  XPub: {bip86Xpub}");
                Console.WriteLine($"  Variant: tr(k)");

                // Get receive addresses (displayed on Jade screen for verification)
                Console.WriteLine("\n--- Receive Addresses (first address of each type) ---");
                Console.WriteLine("(Address will be shown on Jade screen for verification)");
/*
                // BIP84 Native SegWit address: m/84'/0'/0'/0/0
                uint[] bip84AddrPath = { 84 + HARDENED, 0 + HARDENED, 0 + HARDENED, 0, 0 };
                var segwitAddr = await rpc.GetReceiveAddressAsync("mainnet", bip84AddrPath, "wpkh(k)");
                Console.WriteLine($"\nBIP84 Native SegWit (first receive):");
                Console.WriteLine($"  Path: {FormatBip32Path(bip84AddrPath)}");
                Console.WriteLine($"  Address: {segwitAddr}");

                // BIP86 Taproot address: m/86'/0'/0'/0/0
                uint[] bip86AddrPath = { 86 + HARDENED, 0 + HARDENED, 0 + HARDENED, 0, 0 };
                var taprootAddr = await rpc.GetReceiveAddressAsync("mainnet", bip86AddrPath, "tr(k)");
                Console.WriteLine($"\nBIP86 Taproot (first receive):");
                Console.WriteLine($"  Path: {FormatBip32Path(bip86AddrPath)}");
                Console.WriteLine($"  Address: {taprootAddr}");
*/
                // Test HSM functionality
                Console.WriteLine("\n\n--- HSM Mode Test ---");
                Console.WriteLine("Testing HSM cryptographic operations...");

                // Check HSM status
                Console.WriteLine("\nChecking HSM status...");
                var hsmInfo = await rpc.HsmGetInfoAsync();
                Console.WriteLine($"  HSM Active: {hsmInfo.Active}");

                if (hsmInfo.Active)
                {
                    Console.WriteLine($"  Networks: {string.Join(", ", hsmInfo.Networks)}");
                    Console.WriteLine($"  Mainnet Path: {hsmInfo.MainnetRootPath}");
                    Console.WriteLine($"  Testnet Path: {hsmInfo.TestnetRootPath}");
                    if (hsmInfo.MainnetRootPubkey != null)
                        Console.WriteLine($"  Mainnet Root Pubkey: {BitConverter.ToString(hsmInfo.MainnetRootPubkey).Replace("-", "").ToLowerInvariant()}");
                    Console.WriteLine($"  Operations Count: {hsmInfo.OperationsCount}");
                    Console.WriteLine($"  Auto-Lock Timeout: {(hsmInfo.AutoLockTimeout == 0 ? "Disabled" : $"{hsmInfo.AutoLockTimeout}s")}");

                    // Test HSM signing
                    Console.WriteLine("\n--- HSM Signing Test ---");

                    // Get public key at index 0
                    var pubkeyResult = await rpc.HsmGetPubkeyAsync("mainnet", 0);
                    Console.WriteLine($"  HSM Pubkey at index 0:");
                    Console.WriteLine($"    Path: {pubkeyResult.Path}");
                    Console.WriteLine($"    Pubkey: {BitConverter.ToString(pubkeyResult.Pubkey).Replace("-", "").ToLowerInvariant()}");

                    // Sign a test message hash
                    byte[] testHash = new byte[32];
                    Random.Shared.NextBytes(testHash);
                    Console.WriteLine($"\n  Signing test hash: {BitConverter.ToString(testHash).Replace("-", "").ToLowerInvariant()}");

                    // Schnorr signature
                    var schnorrResult = await rpc.HsmSignAsync("mainnet", 0, testHash, "schnorr");
                    Console.WriteLine($"\n  Schnorr Signature ({schnorrResult.Algorithm}):");
                    Console.WriteLine($"    Signature: {BitConverter.ToString(schnorrResult.Signature).Replace("-", "").ToLowerInvariant()}");
                    Console.WriteLine($"    Pubkey: {BitConverter.ToString(schnorrResult.Pubkey).Replace("-", "").ToLowerInvariant()}");

                    // ECDSA signature
                    var ecdsaResult = await rpc.HsmSignAsync("mainnet", 0, testHash, "ecdsa");
                    Console.WriteLine($"\n  ECDSA Signature ({ecdsaResult.Algorithm}):");
                    Console.WriteLine($"    Signature (DER): {BitConverter.ToString(ecdsaResult.Signature).Replace("-", "").ToLowerInvariant()}");
                    Console.WriteLine($"    Pubkey: {BitConverter.ToString(ecdsaResult.Pubkey).Replace("-", "").ToLowerInvariant()}");

                    // Test ECIES encryption/decryption
                    Console.WriteLine("\n--- HSM Encryption Test ---");
                    string originalMessage = "Hello, HSM encryption test!";
                    byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(originalMessage);
                    Console.WriteLine($"  Original message: {originalMessage}");
                    Console.WriteLine($"  Plaintext bytes: {BitConverter.ToString(plaintext).Replace("-", "").ToLowerInvariant()}");

                    // Encrypt to self (using our own pubkey)
                    var encryptResult = await rpc.HsmEncryptAsync("mainnet", 0, plaintext);
                    Console.WriteLine($"\n  Encrypted:");
                    Console.WriteLine($"    Ciphertext: {BitConverter.ToString(encryptResult.Ciphertext).Replace("-", "").ToLowerInvariant()}");
                    Console.WriteLine($"    Nonce: {BitConverter.ToString(encryptResult.Nonce).Replace("-", "").ToLowerInvariant()}");
                    Console.WriteLine($"    Tag: {BitConverter.ToString(encryptResult.Tag).Replace("-", "").ToLowerInvariant()}");
                    Console.WriteLine($"    Ephemeral Pubkey: {BitConverter.ToString(encryptResult.EphemeralPubkey).Replace("-", "").ToLowerInvariant()}");

                    // Decrypt
                    var decryptedBytes = await rpc.HsmDecryptAsync(
                        "mainnet", 0,
                        encryptResult.Ciphertext,
                        encryptResult.Nonce,
                        encryptResult.Tag,
                        encryptResult.EphemeralPubkey);
                    string decryptedMessage = System.Text.Encoding.UTF8.GetString(decryptedBytes);
                    Console.WriteLine($"\n  Decrypted message: {decryptedMessage}");
                    Console.WriteLine($"  Encryption/Decryption test: {(decryptedMessage == originalMessage ? "PASSED" : "FAILED")}");

                    // Test ECDH
                    Console.WriteLine("\n--- HSM ECDH Test ---");
                    // Use our own pubkey at index 1 as "their" pubkey for testing
                    var theirPubkeyResult = await rpc.HsmGetPubkeyAsync("mainnet", 1);
                    Console.WriteLine($"  Their pubkey (index 1): {BitConverter.ToString(theirPubkeyResult.Pubkey).Replace("-", "").ToLowerInvariant()}");

                    var sharedSecret = await rpc.HsmEcdhAsync("mainnet", 0, theirPubkeyResult.Pubkey);
                    Console.WriteLine($"  Shared secret: {BitConverter.ToString(sharedSecret).Replace("-", "").ToLowerInvariant()}");

                    // Get xpub
                    Console.WriteLine("\n--- HSM XPub ---");
                    var hsmXpub = await rpc.HsmGetXpubAsync("mainnet");
                    Console.WriteLine($"  Path: {hsmXpub.Path}");
                    Console.WriteLine($"  XPub: {hsmXpub.Xpub}");

                    // Check updated HSM info
                    hsmInfo = await rpc.HsmGetInfoAsync();
                    Console.WriteLine($"\n  Total HSM operations: {hsmInfo.OperationsCount}");
                }
                else
                {
                    Console.WriteLine("  HSM mode is not active.");
                    Console.WriteLine("  To test HSM features, unlock HSM mode from the Jade device menu.");
                }

                // Logout to re-lock
                Console.WriteLine("\nLogging out (re-locking device)...");
                var logoutResult = await rpc.LogoutAsync();
                Console.WriteLine($"Logout result: {logoutResult}");
            }
            else
            {
                Console.WriteLine("Authentication FAILED.");
            }
        }
        catch (JadeRpcException ex)
        {
            Console.WriteLine($"Auth RPC error {ex.ErrorCode}: {ex.Message}");
            if (ex.IsUserCancelled)
                Console.WriteLine("  -> User cancelled PIN entry on device.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auth error: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("\nDevice has no PIN configured - skipping authentication test.");
        Console.WriteLine("Use Jade's mobile app to set up a PIN first.");
    }

    await rpc.DisconnectAsync();
    Console.WriteLine("\nDisconnected from Jade.");
    Console.WriteLine("\nTest Complete!");
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

// Helper function to format BIP32 path
static string FormatBip32Path(uint[] path)
{
    const uint HARDENED = 0x80000000;
    var parts = path.Select(p =>
    {
        bool isHardened = (p & HARDENED) != 0;
        uint index = p & ~HARDENED;
        return isHardened ? $"{index}'" : index.ToString();
    });
    return "m/" + string.Join("/", parts);
}

// Extract the master fingerprint from an xpub
// The fingerprint is bytes 5-8 of the decoded xpub (parent fingerprint field)
// For the root key (m/), this is 00000000, but we want the fingerprint OF this key
// which requires hashing the public key. For simplicity, we extract from a child xpub.
// However, for the root xpub, we decode and compute the fingerprint from the public key.
static string ExtractFingerprint(string xpub)
{
    try
    {
        // Base58Check decode the xpub
        var decoded = Base58CheckDecode(xpub);
        if (decoded.Length != 78)
            return "Invalid xpub length";

        // For the root xpub (depth=0), parent fingerprint is 00000000
        // The actual fingerprint is hash160(pubkey)[0:4]
        // pubkey is at bytes 45-78 (33 bytes for compressed pubkey)
        byte depth = decoded[4];
        if (depth == 0)
        {
            // Root key - compute fingerprint from public key
            var pubkey = decoded[45..78];
            var hash = Hash160(pubkey);
            return BitConverter.ToString(hash[..4]).Replace("-", "").ToUpperInvariant();
        }
        else
        {
            // Non-root key - parent fingerprint is at bytes 5-8
            return BitConverter.ToString(decoded[5..9]).Replace("-", "").ToUpperInvariant();
        }
    }
    catch
    {
        return "Error decoding xpub";
    }
}

// Base58Check decode
static byte[] Base58CheckDecode(string input)
{
    const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    var bi = System.Numerics.BigInteger.Zero;
    foreach (char c in input)
    {
        int digit = alphabet.IndexOf(c);
        if (digit < 0)
            throw new FormatException($"Invalid Base58 character: {c}");
        bi = bi * 58 + digit;
    }

    // Convert to bytes
    var bytes = bi.ToByteArray(isUnsigned: true, isBigEndian: true);

    // Count leading zeros in input
    int leadingZeros = input.TakeWhile(c => c == '1').Count();

    // Prepend zero bytes
    var result = new byte[leadingZeros + bytes.Length];
    bytes.CopyTo(result, leadingZeros);

    // Verify checksum (last 4 bytes)
    var payload = result[..^4];
    var checksum = result[^4..];
    var hash = DoubleSha256(payload);
    if (!hash[..4].SequenceEqual(checksum))
        throw new FormatException("Invalid checksum");

    return payload;
}

// Double SHA256
static byte[] DoubleSha256(byte[] data)
{
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var first = sha256.ComputeHash(data);
    return sha256.ComputeHash(first);
}

// HASH160 = RIPEMD160(SHA256(data))
static byte[] Hash160(byte[] data)
{
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var shaHash = sha256.ComputeHash(data);

    // .NET doesn't have built-in RIPEMD160, use a simple implementation
    return Ripemd160(shaHash);
}

// Simple RIPEMD160 implementation
static byte[] Ripemd160(byte[] data)
{
    uint[] h = { 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0 };

    // Padding
    int padLen = (64 - (data.Length + 9) % 64) % 64;
    byte[] padded = new byte[data.Length + 1 + padLen + 8];
    data.CopyTo(padded, 0);
    padded[data.Length] = 0x80;
    ulong bitLen = (ulong)data.Length * 8;
    BitConverter.GetBytes(bitLen).CopyTo(padded, padded.Length - 8);

    // Process blocks
    for (int i = 0; i < padded.Length; i += 64)
    {
        uint[] x = new uint[16];
        for (int j = 0; j < 16; j++)
            x[j] = BitConverter.ToUInt32(padded, i + j * 4);

        uint al = h[0], bl = h[1], cl = h[2], dl = h[3], el = h[4];
        uint ar = h[0], br = h[1], cr = h[2], dr = h[3], er = h[4];

        int[] rl = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                     7, 4, 13, 1, 10, 6, 15, 3, 12, 0, 9, 5, 2, 14, 11, 8,
                     3, 10, 14, 4, 9, 15, 8, 1, 2, 7, 0, 6, 13, 11, 5, 12,
                     1, 9, 11, 10, 0, 8, 12, 4, 13, 3, 7, 15, 14, 5, 6, 2,
                     4, 0, 5, 9, 7, 12, 2, 10, 14, 1, 3, 8, 11, 6, 15, 13 };
        int[] rr = { 5, 14, 7, 0, 9, 2, 11, 4, 13, 6, 15, 8, 1, 10, 3, 12,
                     6, 11, 3, 7, 0, 13, 5, 10, 14, 15, 8, 12, 4, 9, 1, 2,
                     15, 5, 1, 3, 7, 14, 6, 9, 11, 8, 12, 2, 10, 0, 4, 13,
                     8, 6, 4, 1, 3, 11, 15, 0, 5, 12, 2, 13, 9, 7, 10, 14,
                     12, 15, 10, 4, 1, 5, 8, 7, 6, 2, 13, 14, 0, 3, 9, 11 };
        int[] sl = { 11, 14, 15, 12, 5, 8, 7, 9, 11, 13, 14, 15, 6, 7, 9, 8,
                     7, 6, 8, 13, 11, 9, 7, 15, 7, 12, 15, 9, 11, 7, 13, 12,
                     11, 13, 6, 7, 14, 9, 13, 15, 14, 8, 13, 6, 5, 12, 7, 5,
                     11, 12, 14, 15, 14, 15, 9, 8, 9, 14, 5, 6, 8, 6, 5, 12,
                     9, 15, 5, 11, 6, 8, 13, 12, 5, 12, 13, 14, 11, 8, 5, 6 };
        int[] sr = { 8, 9, 9, 11, 13, 15, 15, 5, 7, 7, 8, 11, 14, 14, 12, 6,
                     9, 13, 15, 7, 12, 8, 9, 11, 7, 7, 12, 7, 6, 15, 13, 11,
                     9, 7, 15, 11, 8, 6, 6, 14, 12, 13, 5, 14, 13, 13, 7, 5,
                     15, 5, 8, 11, 14, 14, 6, 14, 6, 9, 12, 9, 12, 5, 15, 8,
                     8, 5, 12, 9, 12, 5, 14, 6, 8, 13, 6, 5, 15, 13, 11, 11 };

        uint Rotl(uint v, int n) => (v << n) | (v >> (32 - n));
        uint F(int j, uint a, uint b, uint c) => j < 16 ? a ^ b ^ c : j < 32 ? (a & b) | (~a & c) :
            j < 48 ? (a | ~b) ^ c : j < 64 ? (a & c) | (b & ~c) : a ^ (b | ~c);
        uint K(int j) => j < 16 ? 0u : j < 32 ? 0x5A827999u : j < 48 ? 0x6ED9EBA1u : j < 64 ? 0x8F1BBCDCu : 0xA953FD4Eu;
        uint Kr(int j) => j < 16 ? 0x50A28BE6u : j < 32 ? 0x5C4DD124u : j < 48 ? 0x6D703EF3u : j < 64 ? 0x7A6D76E9u : 0u;

        for (int j = 0; j < 80; j++)
        {
            uint t = Rotl(al + F(j, bl, cl, dl) + x[rl[j]] + K(j), sl[j]) + el;
            al = el; el = dl; dl = Rotl(cl, 10); cl = bl; bl = t;
            t = Rotl(ar + F(79 - j, br, cr, dr) + x[rr[j]] + Kr(j), sr[j]) + er;
            ar = er; er = dr; dr = Rotl(cr, 10); cr = br; br = t;
        }

        uint t2 = h[1] + cl + dr;
        h[1] = h[2] + dl + er; h[2] = h[3] + el + ar; h[3] = h[4] + al + br;
        h[4] = h[0] + bl + cr; h[0] = t2;
    }

    byte[] result = new byte[20];
    for (int i = 0; i < 5; i++)
        BitConverter.GetBytes(h[i]).CopyTo(result, i * 4);
    return result;
}
