namespace JadeClient.Utilities;

/// <summary>
/// Utilities for handling BIP32 derivation paths.
/// </summary>
public static class Bip32Path
{
    /// <summary>
    /// Flag for hardened derivation (0x80000000).
    /// </summary>
    public const uint HardenedFlag = 0x80000000;

    /// <summary>
    /// Parse a BIP32 path string into an array of uint32 values.
    /// </summary>
    /// <param name="path">Path string like "m/84'/0'/0'/0/0"</param>
    /// <returns>Array of path components with hardened flags applied</returns>
    /// <example>
    /// var path = Bip32Path.Parse("m/84'/0'/0'");
    /// // Returns: [0x80000054, 0x80000000, 0x80000000]
    /// </example>
    public static uint[] Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        // Remove leading "m/" if present
        var normalizedPath = path.Trim();
        if (normalizedPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase))
            normalizedPath = normalizedPath[2..];
        else if (normalizedPath.StartsWith("m", StringComparison.OrdinalIgnoreCase))
            normalizedPath = normalizedPath[1..];

        if (string.IsNullOrWhiteSpace(normalizedPath))
            return Array.Empty<uint>();

        var components = normalizedPath.Split('/');
        var result = new uint[components.Length];

        for (int i = 0; i < components.Length; i++)
        {
            var component = components[i].Trim();
            bool hardened = component.EndsWith("'") || component.EndsWith("h") || component.EndsWith("H");

            if (hardened)
                component = component[..^1];

            if (!uint.TryParse(component, out uint index))
                throw new ArgumentException($"Invalid path component: {components[i]}", nameof(path));

            if (index >= HardenedFlag)
                throw new ArgumentException($"Path component too large: {index}", nameof(path));

            result[i] = hardened ? (index | HardenedFlag) : index;
        }

        return result;
    }

    /// <summary>
    /// Convert a path array back to string format.
    /// </summary>
    /// <param name="path">Array of path components</param>
    /// <param name="includeM">Whether to include "m/" prefix</param>
    /// <returns>Path string like "m/84'/0'/0'"</returns>
    public static string ToString(uint[] path, bool includeM = true)
    {
        if (path == null || path.Length == 0)
            return includeM ? "m" : "";

        var components = path.Select(p =>
        {
            bool hardened = (p & HardenedFlag) != 0;
            uint index = p & ~HardenedFlag;
            return hardened ? $"{index}'" : index.ToString();
        });

        var pathStr = string.Join("/", components);
        return includeM ? $"m/{pathStr}" : pathStr;
    }

    /// <summary>
    /// Create a standard BIP44 path.
    /// </summary>
    /// <param name="purpose">BIP purpose (44, 49, 84, 86)</param>
    /// <param name="coinType">Coin type (0 for Bitcoin mainnet, 1 for testnet)</param>
    /// <param name="account">Account index</param>
    /// <param name="change">Change index (0 for receive, 1 for change)</param>
    /// <param name="addressIndex">Address index</param>
    /// <returns>Full derivation path</returns>
    public static uint[] CreateBip44Path(uint purpose, uint coinType, uint account, uint change, uint addressIndex)
    {
        return new uint[]
        {
            purpose | HardenedFlag,
            coinType | HardenedFlag,
            account | HardenedFlag,
            change,
            addressIndex
        };
    }

    /// <summary>
    /// Create a BIP84 (native segwit) path for Bitcoin mainnet.
    /// </summary>
    public static uint[] Bip84Mainnet(uint account = 0, uint change = 0, uint addressIndex = 0)
        => CreateBip44Path(84, 0, account, change, addressIndex);

    /// <summary>
    /// Create a BIP84 (native segwit) path for Bitcoin testnet.
    /// </summary>
    public static uint[] Bip84Testnet(uint account = 0, uint change = 0, uint addressIndex = 0)
        => CreateBip44Path(84, 1, account, change, addressIndex);

    /// <summary>
    /// Create a BIP49 (nested segwit) path for Bitcoin mainnet.
    /// </summary>
    public static uint[] Bip49Mainnet(uint account = 0, uint change = 0, uint addressIndex = 0)
        => CreateBip44Path(49, 0, account, change, addressIndex);

    /// <summary>
    /// Create a BIP86 (taproot) path for Bitcoin mainnet.
    /// </summary>
    public static uint[] Bip86Mainnet(uint account = 0, uint change = 0, uint addressIndex = 0)
        => CreateBip44Path(86, 0, account, change, addressIndex);
}
