using System;
using System.Threading;
using System.Threading.Tasks;
using JadeClient.Protocol;
using JadeClient.Transport;
using JadeClient.Models;
using JadeClient.PinServer;

namespace HsmTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("JadeClient C# Library - HSM Mode Test");
        Console.WriteLine("======================================\n");

        // Find Jade device
        var ports = SerialTransport.GetAvailablePorts();
        string? jadePort = null;
        foreach (var port in ports)
        {
            if (port.Contains("usbserial") || port.Contains("USB") || port.Contains("ACM"))
            {
                jadePort = port;
                break;
            }
        }

        if (jadePort == null)
        {
            Console.WriteLine("No Jade device found. Please connect your Jade and try again.");
            return;
        }

        Console.WriteLine($"Found Jade on {jadePort}");

        using var transport = new SerialTransport(jadePort);
        using var rpc = new JadeRpc(transport);

        try
        {
            Console.WriteLine("Connecting to Jade...");
            await transport.ConnectAsync();
            Console.WriteLine("Connected!\n");

            // Step 1: Get device info
            Console.WriteLine("--- Step 1: Device Info ---");
            var info = await rpc.GetVersionInfoAsync();
            Console.WriteLine($"  Firmware: {info.JadeVersion}");
            Console.WriteLine($"  State: {info.State}");
            Console.WriteLine($"  Has PIN: {info.HasPin}");

            // Step 2: Unlock the device
            Console.WriteLine("\n--- Step 2: Unlock Device ---");
            if (info.State == JadeState.Locked || info.State == JadeState.Temp)
            {
                Console.WriteLine("Device is locked. Starting authentication...");
                Console.WriteLine("Please enter your PIN on the Jade device.\n");

                var pinServer = new RemotePinServerHandler();
                var authResult = await rpc.AuthUserAsync(pinServer, "mainnet");
                if (!authResult)
                {
                    Console.WriteLine("Authentication failed!");
                    return;
                }
                Console.WriteLine("Authentication successful!");
            }
            else if (info.State == JadeState.Ready)
            {
                Console.WriteLine("Device is already unlocked.");
            }
            else if (info.State == JadeState.Uninit)
            {
                Console.WriteLine("Device is not initialized. Please set up your Jade first.");
                return;
            }

            // Step 3: Check HSM status and wait for activation
            Console.WriteLine("\n--- Step 3: HSM Mode Activation ---");
            var hsmInfo = await rpc.HsmGetInfoAsync();

            if (!hsmInfo.Active)
            {
                Console.WriteLine("HSM mode is NOT active.");
                Console.WriteLine("\n*** ACTION REQUIRED ***");
                Console.WriteLine("Please activate HSM mode on your Jade device:");
                Console.WriteLine("  1. Press the button on Jade to go to the menu");
                Console.WriteLine("  2. Select 'Session'");
                Console.WriteLine("  3. Select 'HSM Mode'");
                Console.WriteLine("  4. Confirm activation\n");
                Console.WriteLine("Waiting for HSM mode activation (timeout: 2 minutes)...");

                // Poll for HSM activation
                var startTime = DateTime.UtcNow;
                var timeout = TimeSpan.FromMinutes(2);
                int dots = 0;

                while (!hsmInfo.Active && (DateTime.UtcNow - startTime) < timeout)
                {
                    await Task.Delay(2000); // Check every 2 seconds
                    hsmInfo = await rpc.HsmGetInfoAsync();

                    Console.Write(".");
                    dots++;
                    if (dots % 30 == 0)
                        Console.WriteLine();
                }

                Console.WriteLine();

                if (!hsmInfo.Active)
                {
                    Console.WriteLine("\nTimeout waiting for HSM mode activation.");
                    Console.WriteLine("Please run this test again after activating HSM mode.");
                    return;
                }
            }

            Console.WriteLine("\nHSM mode is ACTIVE!");

            // Run HSM tests in a loop
            bool continueTests = true;
            int runNumber = 1;

            while (continueTests)
            {
                if (runNumber > 1)
                {
                    Console.WriteLine($"\n\n{'='.ToString().PadRight(50, '=')}");
                    Console.WriteLine($"HSM Test Run #{runNumber}");
                    Console.WriteLine($"{'='.ToString().PadRight(50, '=')}");
                }

                await RunHsmTests(rpc, runNumber);

                // Ask if user wants to lock or run again
                Console.WriteLine("\n--- HSM Lock/Continue ---");
                Console.Write("Do you want to lock HSM mode? (y=lock and exit, n=run tests again): ");
                var input = Console.ReadLine()?.Trim().ToLower();

                if (input == "y" || input == "yes")
                {
                    var lockResult = await rpc.HsmLockAsync();
                    Console.WriteLine($"  HSM Lock result: {(lockResult ? "SUCCESS" : "FAILED")}");

                    var lockedInfo = await rpc.HsmGetInfoAsync();
                    Console.WriteLine($"  HSM Active after lock: {lockedInfo.Active}");
                    continueTests = false;
                }
                else
                {
                    Console.WriteLine("  Running HSM tests again...");
                    runNumber++;
                }
            }

            Console.WriteLine("\n======================================");
            Console.WriteLine("HSM Test Complete!");
            Console.WriteLine("======================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
        }
        finally
        {
            await transport.DisconnectAsync();
            Console.WriteLine("\nDisconnected from Jade.");
        }
    }

    static async Task RunHsmTests(JadeRpc rpc, int runNumber)
    {
        // Step 4: Display HSM info
        Console.WriteLine("\n--- Step 4: HSM Information ---");
        var hsmInfo = await rpc.HsmGetInfoAsync();
        Console.WriteLine($"  Networks: {string.Join(", ", hsmInfo.Networks ?? Array.Empty<string>())}");
        Console.WriteLine($"  Mainnet Path: {hsmInfo.MainnetRootPath}");
        Console.WriteLine($"  Testnet Path: {hsmInfo.TestnetRootPath}");
        if (hsmInfo.MainnetRootPubkey != null)
            Console.WriteLine($"  Mainnet Root Pubkey: {ToHex(hsmInfo.MainnetRootPubkey)}");
        if (hsmInfo.TestnetRootPubkey != null)
            Console.WriteLine($"  Testnet Root Pubkey: {ToHex(hsmInfo.TestnetRootPubkey)}");
        Console.WriteLine($"  Operations Count: {hsmInfo.OperationsCount}");
        Console.WriteLine($"  Auto-Lock Timeout: {(hsmInfo.AutoLockTimeout == 0 ? "Disabled" : $"{hsmInfo.AutoLockTimeout}s")}");
        if (hsmInfo.AutoLockRemaining > 0)
            Console.WriteLine($"  Auto-Lock Remaining: {hsmInfo.AutoLockRemaining}s");

        // Step 5: Test HSM Get XPub
        Console.WriteLine("\n--- Step 5: HSM XPub ---");
        var xpubResult = await rpc.HsmGetXpubAsync("mainnet");
        Console.WriteLine($"  Path: {xpubResult.Path}");
        Console.WriteLine($"  XPub: {xpubResult.Xpub}");

        // Step 6: Test HSM Get Pubkey
        Console.WriteLine("\n--- Step 6: HSM Public Keys ---");
        for (uint i = 0; i < 3; i++)
        {
            var pubkeyResult = await rpc.HsmGetPubkeyAsync("mainnet", i);
            Console.WriteLine($"  Index {i}:");
            Console.WriteLine($"    Path: {pubkeyResult.Path}");
            Console.WriteLine($"    Pubkey: {ToHex(pubkeyResult.Pubkey)}");
        }

        // Step 7: Test HSM Signing (Schnorr)
        Console.WriteLine("\n--- Step 7: HSM Schnorr Signing ---");
        byte[] testHash = new byte[32];
        Random.Shared.NextBytes(testHash);
        Console.WriteLine($"  Test hash: {ToHex(testHash)}");

        var schnorrResult = await rpc.HsmSignAsync("mainnet", 0, testHash, "schnorr");
        Console.WriteLine($"  Algorithm: {schnorrResult.Algorithm}");
        Console.WriteLine($"  Signature: {ToHex(schnorrResult.Signature)}");
        Console.WriteLine($"  Pubkey: {ToHex(schnorrResult.Pubkey)}");
        Console.WriteLine($"  Signature length: {schnorrResult.Signature.Length} bytes (expected: 64 for Schnorr)");

        // Step 8: Test HSM Signing (ECDSA)
        Console.WriteLine("\n--- Step 8: HSM ECDSA Signing ---");
        var ecdsaResult = await rpc.HsmSignAsync("mainnet", 0, testHash, "ecdsa");
        Console.WriteLine($"  Algorithm: {ecdsaResult.Algorithm}");
        Console.WriteLine($"  Signature (DER): {ToHex(ecdsaResult.Signature)}");
        Console.WriteLine($"  Pubkey: {ToHex(ecdsaResult.Pubkey)}");
        Console.WriteLine($"  Signature length: {ecdsaResult.Signature.Length} bytes (DER encoded)");

        // Step 9: Test HSM ECDH
        Console.WriteLine("\n--- Step 9: HSM ECDH ---");
        // Use pubkey at index 1 as "their" pubkey
        var theirPubkeyResult = await rpc.HsmGetPubkeyAsync("mainnet", 1);
        Console.WriteLine($"  Our key: index 0");
        Console.WriteLine($"  Their key: index 1, pubkey {ToHex(theirPubkeyResult.Pubkey)}");

        var sharedSecret = await rpc.HsmEcdhAsync("mainnet", 0, theirPubkeyResult.Pubkey);
        Console.WriteLine($"  Shared secret: {ToHex(sharedSecret)}");
        Console.WriteLine($"  Secret length: {sharedSecret.Length} bytes (expected: 32)");

        // Verify ECDH is symmetric
        var sharedSecret2 = await rpc.HsmEcdhAsync("mainnet", 1, schnorrResult.Pubkey); // Use index 0's pubkey
        Console.WriteLine($"  Reverse ECDH: {ToHex(sharedSecret2)}");
        Console.WriteLine($"  ECDH symmetric: {(sharedSecret.SequenceEqual(sharedSecret2) ? "PASS" : "FAIL")}");

        // Step 10: Test HSM Encryption/Decryption
        Console.WriteLine("\n--- Step 10: HSM ECIES Encryption ---");
        string originalMessage = "Hello, HSM! This is a test of ECIES encryption.";
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(originalMessage);
        Console.WriteLine($"  Original message: \"{originalMessage}\"");
        Console.WriteLine($"  Plaintext ({plaintext.Length} bytes): {ToHex(plaintext)}");

        // Encrypt to self
        Console.WriteLine("\n  Encrypting to self (index 0)...");
        var encryptResult = await rpc.HsmEncryptAsync("mainnet", 0, plaintext);
        Console.WriteLine($"    Ciphertext ({encryptResult.Ciphertext.Length} bytes): {ToHex(encryptResult.Ciphertext)}");
        Console.WriteLine($"    Nonce ({encryptResult.Nonce.Length} bytes): {ToHex(encryptResult.Nonce)}");
        Console.WriteLine($"    Tag ({encryptResult.Tag.Length} bytes): {ToHex(encryptResult.Tag)}");
        Console.WriteLine($"    Ephemeral pubkey ({encryptResult.EphemeralPubkey.Length} bytes): {ToHex(encryptResult.EphemeralPubkey)}");

        // Decrypt
        Console.WriteLine("\n  Decrypting...");
        var decryptedBytes = await rpc.HsmDecryptAsync(
            "mainnet", 0,
            encryptResult.Ciphertext,
            encryptResult.Nonce,
            encryptResult.Tag,
            encryptResult.EphemeralPubkey);

        string decryptedMessage = System.Text.Encoding.UTF8.GetString(decryptedBytes);
        Console.WriteLine($"    Decrypted ({decryptedBytes.Length} bytes): {ToHex(decryptedBytes)}");
        Console.WriteLine($"    Decrypted message: \"{decryptedMessage}\"");
        Console.WriteLine($"    Encryption/Decryption test: {(decryptedMessage == originalMessage ? "PASS" : "FAIL")}");

        // Step 11: Test encryption with AAD
        Console.WriteLine("\n--- Step 11: HSM ECIES with AAD ---");
        byte[] aad = System.Text.Encoding.UTF8.GetBytes("additional-authenticated-data");
        Console.WriteLine($"  AAD: {System.Text.Encoding.UTF8.GetString(aad)}");

        var encryptWithAad = await rpc.HsmEncryptAsync("mainnet", 0, plaintext, null, aad);
        Console.WriteLine($"  Encrypted with AAD");

        var decryptWithAad = await rpc.HsmDecryptAsync(
            "mainnet", 0,
            encryptWithAad.Ciphertext,
            encryptWithAad.Nonce,
            encryptWithAad.Tag,
            encryptWithAad.EphemeralPubkey,
            aad);

        string decryptedWithAad = System.Text.Encoding.UTF8.GetString(decryptWithAad);
        Console.WriteLine($"  Decrypted with AAD: \"{decryptedWithAad}\"");
        Console.WriteLine($"  AAD test: {(decryptedWithAad == originalMessage ? "PASS" : "FAIL")}");

        // Step 12: Test encryption to another key
        Console.WriteLine("\n--- Step 12: HSM ECIES to Another Key ---");
        var recipientPubkey = await rpc.HsmGetPubkeyAsync("mainnet", 2);
        Console.WriteLine($"  Encrypting from index 0 to index 2's pubkey...");
        Console.WriteLine($"  Recipient pubkey: {ToHex(recipientPubkey.Pubkey)}");

        var encryptToOther = await rpc.HsmEncryptAsync("mainnet", 0, plaintext, recipientPubkey.Pubkey);
        Console.WriteLine($"  Encrypted to recipient");

        // Decrypt with recipient's key (index 2)
        var decryptByRecipient = await rpc.HsmDecryptAsync(
            "mainnet", 2,
            encryptToOther.Ciphertext,
            encryptToOther.Nonce,
            encryptToOther.Tag,
            encryptToOther.EphemeralPubkey);

        string decryptedByRecipient = System.Text.Encoding.UTF8.GetString(decryptByRecipient);
        Console.WriteLine($"  Decrypted by recipient: \"{decryptedByRecipient}\"");
        Console.WriteLine($"  Cross-key encryption test: {(decryptedByRecipient == originalMessage ? "PASS" : "FAIL")}");

        // Step 13: Test testnet keys
        Console.WriteLine("\n--- Step 13: Testnet Keys ---");
        var testnetXpub = await rpc.HsmGetXpubAsync("testnet");
        Console.WriteLine($"  Testnet XPub path: {testnetXpub.Path}");
        Console.WriteLine($"  Testnet XPub: {testnetXpub.Xpub}");

        var testnetPubkey = await rpc.HsmGetPubkeyAsync("testnet", 0);
        Console.WriteLine($"  Testnet pubkey at index 0: {ToHex(testnetPubkey.Pubkey)}");

        // Step 14: Check final operations count
        Console.WriteLine("\n--- Step 14: Final HSM Status ---");
        var finalInfo = await rpc.HsmGetInfoAsync();
        Console.WriteLine($"  Total operations: {finalInfo.OperationsCount}");
        if (finalInfo.AutoLockRemaining > 0)
            Console.WriteLine($"  Auto-lock remaining: {finalInfo.AutoLockRemaining}s");

        // Step 15: CRITICAL SECURITY TEST - Verify seed isolation
        Console.WriteLine("\n--- Step 15: Seed Isolation Test ---");
        Console.WriteLine("Testing that wallet seed is NOT accessible in HSM mode...");

        // Try to call a wallet function that requires the seed
        // This should FAIL if the seed was properly wiped after HSM activation
        const uint HARDENED = 0x80000000;
        uint[] walletPath = { 84 + HARDENED, 0 + HARDENED, 0 + HARDENED };

        try
        {
            Console.WriteLine("  Attempting to get wallet xpub (m/84'/0'/0')...");
            var walletXpub = await rpc.GetXpubAsync("mainnet", walletPath);
            // If we get here, the seed was NOT wiped - SECURITY FAILURE!
            Console.WriteLine($"  WARNING: Wallet xpub returned: {walletXpub}");
            Console.WriteLine("  *** SECURITY FAILURE: Seed is still accessible in HSM mode! ***");
            Console.WriteLine("  *** The keychain should have been cleared after HSM activation ***");
        }
        catch (Exception ex)
        {
            // Expected behavior - the wallet operation should fail
            Console.WriteLine($"  Expected error: {ex.Message}");
            Console.WriteLine("  PASS: Wallet seed is NOT accessible in HSM mode");
            Console.WriteLine("  The keychain was properly cleared after HSM activation");
        }

        // Also verify HSM operations still work
        Console.WriteLine("\n  Verifying HSM operations still work...");
        try
        {
            var testPubkey = await rpc.HsmGetPubkeyAsync("mainnet", 99);
            Console.WriteLine($"  HSM pubkey at index 99: {ToHex(testPubkey.Pubkey)}");
            Console.WriteLine("  PASS: HSM operations work correctly");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: HSM operation failed: {ex.Message}");
        }

        Console.WriteLine($"\n--- Run #{runNumber} Complete ---");
    }

    static string ToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
