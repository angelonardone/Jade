using JadeClient.Utilities;
using Xunit;

namespace JadeClient.Tests;

public class Bip32PathTests
{
    [Theory]
    [InlineData("m/84'/0'/0'", new uint[] { 0x80000054, 0x80000000, 0x80000000 })]
    [InlineData("84'/0'/0'", new uint[] { 0x80000054, 0x80000000, 0x80000000 })]
    [InlineData("m/44'/0'/0'/0/0", new uint[] { 0x8000002C, 0x80000000, 0x80000000, 0, 0 })]
    [InlineData("m/49h/0h/0h", new uint[] { 0x80000031, 0x80000000, 0x80000000 })]
    [InlineData("m/0/1/2", new uint[] { 0, 1, 2 })]
    public void Parse_ValidPaths_ReturnsCorrectArray(string path, uint[] expected)
    {
        var result = Bip32Path.Parse(path);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyPath_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => Bip32Path.Parse(path));
    }

    [Theory]
    [InlineData("m/abc/0'/0'")]
    [InlineData("m/84'/invalid/0'")]
    public void Parse_InvalidComponent_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => Bip32Path.Parse(path));
    }

    [Fact]
    public void Parse_MOnly_ReturnsEmptyArray()
    {
        var result = Bip32Path.Parse("m");
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(new uint[] { 0x80000054, 0x80000000, 0x80000000 }, true, "m/84'/0'/0'")]
    [InlineData(new uint[] { 0x80000054, 0x80000000, 0x80000000 }, false, "84'/0'/0'")]
    [InlineData(new uint[] { 0, 1, 2 }, true, "m/0/1/2")]
    public void ToString_ValidPath_ReturnsCorrectString(uint[] path, bool includeM, string expected)
    {
        var result = Bip32Path.ToString(path, includeM);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Bip84Mainnet_DefaultAccount_ReturnsCorrectPath()
    {
        var path = Bip32Path.Bip84Mainnet(account: 0, change: 0, addressIndex: 0);

        Assert.Equal(5, path.Length);
        Assert.Equal(84u | Bip32Path.HardenedFlag, path[0]);  // 84'
        Assert.Equal(0u | Bip32Path.HardenedFlag, path[1]);   // 0' (mainnet)
        Assert.Equal(0u | Bip32Path.HardenedFlag, path[2]);   // 0' (account)
        Assert.Equal(0u, path[3]);                             // 0 (receive)
        Assert.Equal(0u, path[4]);                             // 0 (first address)
    }

    [Fact]
    public void Bip84Testnet_ReturnsCorrectCoinType()
    {
        var path = Bip32Path.Bip84Testnet();

        Assert.Equal(1u | Bip32Path.HardenedFlag, path[1]); // 1' (testnet)
    }

    [Fact]
    public void RoundTrip_ParseAndToString_ReturnsOriginal()
    {
        var original = "m/84'/0'/0'/0/5";
        var parsed = Bip32Path.Parse(original);
        var result = Bip32Path.ToString(parsed);

        Assert.Equal(original, result);
    }
}
