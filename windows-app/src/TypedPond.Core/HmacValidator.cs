using System.Security.Cryptography;
using System.Text;

namespace TypedPond.Core;

/// <summary>
/// Validates HMAC-SHA256 signatures on local HTTP step updates.
/// The signed payload is "{steps}:{date}".
/// </summary>
public static class HmacValidator
{
    /// <summary>
    /// Computes HMAC-SHA256 over "{steps}:{date}" with the shared secret and
    /// compares it to the provided HMAC (hex-encoded) in constant time.
    /// </summary>
    /// <param name="steps">Step count as a string, exactly as sent.</param>
    /// <param name="date">Date string, e.g. "2026-07-14".</param>
    /// <param name="providedHmac">Hex-encoded HMAC from the request.</param>
    /// <param name="secret">Shared secret.</param>
    /// <returns>True if the HMAC is valid.</returns>
    public static bool ValidateHmac(string steps, string date, string providedHmac, string secret)
    {
        if (string.IsNullOrEmpty(providedHmac) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        byte[] key = Encoding.UTF8.GetBytes(secret);
        byte[] message = Encoding.UTF8.GetBytes($"{steps}:{date}");

        byte[] expected = HMACSHA256.HashData(key, message);

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(providedHmac);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
