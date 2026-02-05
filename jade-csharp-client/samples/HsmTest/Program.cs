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

        // Check for QEMU/TCP connection
        bool useQemu = args.Length > 0 && (args[0] == "--qemu" || args[0].StartsWith("tcp:"));
        string tcpHost = "localhost";
        int tcpPort = TcpTransport.DefaultQemuPort;

        if (args.Length > 0 && args[0].StartsWith("tcp:"))
        {
            // Parse tcp:host:port format
            var parts = args[0].Substring(4).Split(':');
            tcpHost = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out int p))
                tcpPort = p;
            useQemu = true;
        }

        IJadeTransport transport;

        if (useQemu)
        {
            Console.WriteLine($"Connecting to QEMU emulator at {tcpHost}:{tcpPort}...\n");
            transport = new TcpTransport(tcpHost, tcpPort);
        }
        else
        {
            // Detect platform and find Jade device
            var platform = SerialTransport.GetCurrentPlatform();
            Console.WriteLine($"Platform: {platform}\n");

            // Auto-detect or use command line argument
            string? jadePort = args.Length > 0 ? args[0] : SerialTransport.FindJadePort();

            if (jadePort == null)
            {
                Console.WriteLine("No Jade device found. Please connect your Jade and try again.\n");

                var jadePorts = SerialTransport.DiscoverJadePorts();
                if (jadePorts.Length > 0)
                {
                    Console.WriteLine("Detected USB-serial ports:");
                    foreach (var port in jadePorts)
                        Console.WriteLine($"  - {port}");
                }

                Console.WriteLine($"\n{SerialTransport.GetPortNamingHelp()}");
                Console.WriteLine("\nUsage: HsmTest [port_name]");
                Console.WriteLine("       HsmTest --qemu");
                Console.WriteLine("       HsmTest tcp:localhost:30121");
                return;
            }

            Console.WriteLine($"Found Jade on {jadePort}");
            transport = new SerialTransport(jadePort);
        }

        using var _ = transport;
        using var rpc = new JadeRpc(transport);

        try
        {
            Console.WriteLine("Connecting...");
            await transport.ConnectAsync();
            Console.WriteLine("Connected!\n");

            // Drain any pending data
            transport.Drain();

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

        // Step 16: Test BIE1 ECIES Encryption (NBitcoin compatible)
        Console.WriteLine("\n--- Step 16: BIE1 ECIES Encryption (NBitcoin compatible) ---");
        string bie1Message = "Hello, BIE1! Testing NBitcoin-compatible encryption.";
        byte[] bie1Plaintext = System.Text.Encoding.UTF8.GetBytes(bie1Message);
        Console.WriteLine($"  Original message: \"{bie1Message}\"");

        // Encrypt using BIE1 format
        Console.WriteLine("\n  Encrypting with BIE1 format to self (index 0)...");
        var bie1Encrypted = await rpc.HsmEncryptBie1Async("mainnet", 0, bie1Plaintext);
        Console.WriteLine($"  BIE1 blob ({bie1Encrypted.Length} bytes): {ToHex(bie1Encrypted)}");
        Console.WriteLine($"  BIE1 blob (Base64): {Convert.ToBase64String(bie1Encrypted)}");

        // Verify BIE1 magic
        string magic = System.Text.Encoding.ASCII.GetString(bie1Encrypted, 0, 4);
        Console.WriteLine($"  Magic bytes: \"{magic}\" (expected: \"BIE1\")");
        Console.WriteLine($"  Magic check: {(magic == "BIE1" ? "PASS" : "FAIL")}");

        // Decrypt BIE1
        Console.WriteLine("\n  Decrypting BIE1 format...");
        var bie1Decrypted = await rpc.HsmDecryptBie1Async("mainnet", 0, bie1Encrypted);
        string bie1DecryptedMessage = System.Text.Encoding.UTF8.GetString(bie1Decrypted);
        Console.WriteLine($"  Decrypted message: \"{bie1DecryptedMessage}\"");
        Console.WriteLine($"  BIE1 encryption/decryption test: {(bie1DecryptedMessage == bie1Message ? "PASS" : "FAIL")}");

        // Step 17: Test BIE1 encryption to another key
        Console.WriteLine("\n--- Step 17: BIE1 Encryption to Another Key ---");
        var bie1RecipientPubkey = await rpc.HsmGetPubkeyAsync("mainnet", 1);
        Console.WriteLine($"  Encrypting to index 1's pubkey: {ToHex(bie1RecipientPubkey.Pubkey)}");

        var bie1EncryptedToOther = await rpc.HsmEncryptBie1Async("mainnet", 0, bie1Plaintext, bie1RecipientPubkey.Pubkey);
        Console.WriteLine($"  Encrypted BIE1 blob ({bie1EncryptedToOther.Length} bytes)");

        // Decrypt with recipient's key
        var bie1DecryptedByRecipient = await rpc.HsmDecryptBie1Async("mainnet", 1, bie1EncryptedToOther);
        string bie1DecryptedByRecipientMsg = System.Text.Encoding.UTF8.GetString(bie1DecryptedByRecipient);
        Console.WriteLine($"  Decrypted by recipient: \"{bie1DecryptedByRecipientMsg}\"");
        Console.WriteLine($"  Cross-key BIE1 test: {(bie1DecryptedByRecipientMsg == bie1Message ? "PASS" : "FAIL")}");

        // Step 18: Test Compact ECDSA Signing (NBitcoin compatible)
        Console.WriteLine("\n--- Step 18: Compact ECDSA Signing (NBitcoin compatible) ---");
        byte[] compactTestHash = new byte[32];
        Random.Shared.NextBytes(compactTestHash);
        Console.WriteLine($"  Test hash: {ToHex(compactTestHash)}");

        var compactSig = await rpc.HsmSignCompactAsync("mainnet", 0, compactTestHash);
        Console.WriteLine($"  Compact signature ({compactSig.Length} bytes): {ToHex(compactSig)}");
        Console.WriteLine($"  Signature length check: {(compactSig.Length == 65 ? "PASS" : "FAIL")} (expected: 65)");

        // Verify recovery byte format
        int recoveryByte = compactSig[0];
        int recid = (recoveryByte - 27) & 3;
        bool compressed = ((recoveryByte - 27) & 4) != 0;
        Console.WriteLine($"  Recovery byte: 0x{recoveryByte:X2} (recid={recid}, compressed={compressed})");
        Console.WriteLine($"  Compressed flag check: {(compressed ? "PASS" : "FAIL")} (expected: true)");

        // Step 19: Test with NBitcoin Known Test Vectors
        Console.WriteLine("\n--- Step 19: NBitcoin Known Test Vectors ---");
        Console.WriteLine("  NOTE: These tests require the SAME mnemonic as NBitcoin test vectors.");
        Console.WriteLine("  If using a different mnemonic, these will fail but the format should be correct.\n");

        // Test vector for decrypt
        string testVectorEncrypted = "QklFMQKPZJ1HCXabA1//qTgLXe+yKU6ODjymsxAB+dKkA8jOxFbU2duFKNQfF6gmLcY6dNB4A9GZXYcVBogFNz/8EtRSSESz26oHp27qtgvH3hn8mo68suj2nG42Oqkvl280DrA3Niqm2XXsiOMcni8JOLk0";
        string expectedPlaintext = "my name is Distributd Crpyptography";

        Console.WriteLine("  Decrypt test vector:");
        Console.WriteLine($"    Encrypted (Base64): {testVectorEncrypted.Substring(0, 40)}...");
        Console.WriteLine($"    Expected plaintext: \"{expectedPlaintext}\"");

        try
        {
            var testVectorBytes = Convert.FromBase64String(testVectorEncrypted);
            var decryptedTestVector = await rpc.HsmDecryptBie1Async("mainnet", 0, testVectorBytes);
            string decryptedTestVectorStr = System.Text.Encoding.UTF8.GetString(decryptedTestVector);
            Console.WriteLine($"    Actual plaintext: \"{decryptedTestVectorStr}\"");
            Console.WriteLine($"    Test vector decrypt: {(decryptedTestVectorStr == expectedPlaintext ? "PASS" : "FAIL (different mnemonic?)")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Decrypt failed: {ex.Message}");
            Console.WriteLine($"    This is expected if using a different mnemonic than the test vectors.");
        }

        // Test vector for sign
        string testVectorHash = "e5e100401f2c1c6b3b6be4886746c59f60a5c79f9f31fa42b702b36a74855162";
        string expectedSig = "1f3a0e6eafb0f32fded14c06602de6f8a5eee381e5d955011ad3553dc459748e23c6d05917c619eec416f350c6da28d2e9273103fbe57a319227c4e84ecc1d76";

        Console.WriteLine($"\n  Sign test vector:");
        Console.WriteLine($"    Hash: {testVectorHash}");
        Console.WriteLine($"    Expected signature: {expectedSig.Substring(0, 40)}...");

        byte[] testVectorHashBytes = FromHex(testVectorHash);
        var testVectorSig = await rpc.HsmSignCompactAsync("mainnet", 0, testVectorHashBytes);
        string actualSig = ToHex(testVectorSig);
        Console.WriteLine($"    Actual signature: {actualSig.Substring(0, 40)}...");
        Console.WriteLine($"    Test vector sign: {(actualSig == expectedSig ? "PASS" : "FAIL (different mnemonic?)")}");

        // Test vector for pubkey
        string expectedPubkey = "032c4a5797a6772ceee16775eccdea5625ffc9bd9f3ad71d3a427167ee494f40c0";

        Console.WriteLine($"\n  GetPubKey test vector:");
        Console.WriteLine($"    Expected pubkey at index 0: {expectedPubkey}");

        var testVectorPubkey = await rpc.HsmGetPubkeyAsync("mainnet", 0);
        string actualPubkey = ToHex(testVectorPubkey.Pubkey);
        Console.WriteLine($"    Actual pubkey at index 0: {actualPubkey}");
        Console.WriteLine($"    Test vector pubkey: {(actualPubkey == expectedPubkey ? "PASS" : "FAIL (different mnemonic?)")}");

        Console.WriteLine($"\n--- Run #{runNumber} Complete ---");
    }

    static byte[] FromHex(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    static string ToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
