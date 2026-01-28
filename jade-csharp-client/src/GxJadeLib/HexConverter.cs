namespace GxJadeLib;

/// <summary>
/// Utility class for converting between byte arrays and hexadecimal strings.
/// Required for GeneXus compatibility as it cannot directly handle byte arrays.
/// </summary>
public static class HexConverter
{
    /// <summary>
    /// Converts a byte array to a lowercase hexadecimal string.
    /// </summary>
    /// <param name="bytes">The byte array to convert.</param>
    /// <returns>A lowercase hexadecimal string, or empty string if input is null.</returns>
    public static string ToHex(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Converts a hexadecimal string to a byte array.
    /// </summary>
    /// <param name="hex">The hexadecimal string to convert.</param>
    /// <returns>A byte array, or empty array if input is null or empty.</returns>
    /// <exception cref="FormatException">Thrown if the hex string is invalid.</exception>
    public static byte[] FromHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Array.Empty<byte>();

        // Remove any "0x" prefix
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        // Remove any whitespace
        hex = hex.Replace(" ", "").Replace("-", "");

        if (hex.Length % 2 != 0)
            throw new FormatException("Hex string must have an even number of characters");

        return Convert.FromHexString(hex);
    }

    /// <summary>
    /// Validates that a string is a valid hexadecimal representation.
    /// </summary>
    /// <param name="hex">The string to validate.</param>
    /// <returns>True if the string is valid hex, false otherwise.</returns>
    public static bool IsValidHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return true; // Empty is considered valid

        // Remove any "0x" prefix
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        // Remove any whitespace
        hex = hex.Replace(" ", "").Replace("-", "");

        if (hex.Length % 2 != 0)
            return false;

        foreach (char c in hex)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }
}
