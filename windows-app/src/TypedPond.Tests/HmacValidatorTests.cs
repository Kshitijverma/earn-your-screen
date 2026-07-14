using System.Security.Cryptography;
using System.Text;
using TypedPond.Core;
using Xunit;

namespace TypedPond.Tests;

public class HmacValidatorTests
{
    private const string Secret = "super-secret-shared-key";
    private const string Steps = "12345";
    private const string Date = "2026-07-14";

    /// <summary>Computes a known-good hex-encoded HMAC-SHA256 for "{steps}:{date}".</summary>
    private static string ComputeHmac(string steps, string date, string secret)
    {
        byte[] key = Encoding.UTF8.GetBytes(secret);
        byte[] message = Encoding.UTF8.GetBytes($"{steps}:{date}");
        byte[] hash = HMACSHA256.HashData(key, message);
        return Convert.ToHexString(hash); // uppercase hex
    }

    [Fact]
    public void ValidHmac_ReturnsTrue()
    {
        string hmac = ComputeHmac(Steps, Date, Secret);

        Assert.True(HmacValidator.ValidateHmac(Steps, Date, hmac, Secret));
    }

    [Fact]
    public void WrongSecret_ReturnsFalse()
    {
        // HMAC computed with the correct secret, validated against a different one.
        string hmac = ComputeHmac(Steps, Date, Secret);

        Assert.False(HmacValidator.ValidateHmac(Steps, Date, hmac, "different-secret"));
    }

    [Fact]
    public void TamperedSteps_ReturnsFalse()
    {
        string hmac = ComputeHmac(Steps, Date, Secret);

        // Same HMAC, but the steps value has been altered.
        Assert.False(HmacValidator.ValidateHmac("99999", Date, hmac, Secret));
    }

    [Fact]
    public void TamperedDate_ReturnsFalse()
    {
        string hmac = ComputeHmac(Steps, Date, Secret);

        // Same HMAC, but the date has been altered.
        Assert.False(HmacValidator.ValidateHmac(Steps, "2026-07-15", hmac, Secret));
    }

    [Fact]
    public void EmptyProvidedHmac_ReturnsFalse()
    {
        Assert.False(HmacValidator.ValidateHmac(Steps, Date, string.Empty, Secret));
    }

    [Fact]
    public void EmptySecret_ReturnsFalse()
    {
        string hmac = ComputeHmac(Steps, Date, Secret);

        Assert.False(HmacValidator.ValidateHmac(Steps, Date, hmac, string.Empty));
    }

    [Theory]
    [InlineData("xyz")]        // non-hex characters
    [InlineData("abc")]        // odd length (not a whole number of bytes)
    [InlineData("zz00")]       // invalid hex digits
    [InlineData("12g4")]       // one invalid digit
    public void MalformedHex_ReturnsFalse(string malformedHmac)
    {
        Assert.False(HmacValidator.ValidateHmac(Steps, Date, malformedHmac, Secret));
    }

    [Fact]
    public void ValidHmac_LowercaseHex_ReturnsTrue()
    {
        // Convert.FromHexString accepts lowercase hex.
        string hmac = ComputeHmac(Steps, Date, Secret).ToLowerInvariant();

        Assert.True(HmacValidator.ValidateHmac(Steps, Date, hmac, Secret));
    }

    [Fact]
    public void ValidHmac_UppercaseHex_ReturnsTrue()
    {
        // Convert.FromHexString accepts uppercase hex.
        string hmac = ComputeHmac(Steps, Date, Secret).ToUpperInvariant();

        Assert.True(HmacValidator.ValidateHmac(Steps, Date, hmac, Secret));
    }

    [Fact]
    public void ValidHmac_MixedCaseHex_ReturnsTrue()
    {
        // Case must not affect the decoded byte value, so mixed case still validates.
        string upper = ComputeHmac(Steps, Date, Secret);
        var sb = new StringBuilder(upper.Length);
        for (int i = 0; i < upper.Length; i++)
        {
            sb.Append(i % 2 == 0 ? char.ToLowerInvariant(upper[i]) : char.ToUpperInvariant(upper[i]));
        }

        Assert.True(HmacValidator.ValidateHmac(Steps, Date, sb.ToString(), Secret));
    }

    [Fact]
    public void CorrectLengthButWrongBytes_ReturnsFalse()
    {
        // A valid 32-byte (64 hex char) value that is not the expected HMAC.
        string wrong = new string('a', 64);

        Assert.False(HmacValidator.ValidateHmac(Steps, Date, wrong, Secret));
    }
}
