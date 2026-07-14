package com.typedpond.companion

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Produces HMAC-SHA256 signatures for step updates.
 *
 * MUST stay byte-for-byte compatible with the C# validator
 * (TypedPond.Core.HmacValidator), which does:
 *
 *     key     = Encoding.UTF8.GetBytes(secret)
 *     message = Encoding.UTF8.GetBytes($"{steps}:{date}")
 *     hash    = HMACSHA256.HashData(key, message)   // 32 bytes
 *     // request carries hash hex-encoded; C# decodes with
 *     // Convert.FromHexString which is case-insensitive.
 *
 * We therefore:
 *   - format the message exactly as "steps:date" (no spaces),
 *   - render `steps` as a plain base-10 integer string (matches C#'s
 *     int.ToString()),
 *   - use UTF-8 for both key and message,
 *   - emit lowercase hex (accepted by Convert.FromHexString).
 */
object HmacSigner {

    private const val ALGORITHM = "HmacSHA256"
    private val HEX_CHARS = "0123456789abcdef".toCharArray()

    /**
     * @param steps  step count; serialized as a base-10 integer string.
     * @param date   date string, e.g. "2026-07-14".
     * @param secret shared secret (same as the service's HmacSecret).
     * @return lowercase hex-encoded HMAC-SHA256 of "steps:date".
     */
    fun sign(steps: Long, date: String, secret: String): String {
        val message = "$steps:$date"
        val mac = Mac.getInstance(ALGORITHM)
        mac.init(SecretKeySpec(secret.toByteArray(Charsets.UTF_8), ALGORITHM))
        val digest = mac.doFinal(message.toByteArray(Charsets.UTF_8))
        return toHex(digest)
    }

    private fun toHex(bytes: ByteArray): String {
        val out = CharArray(bytes.size * 2)
        for (i in bytes.indices) {
            val v = bytes[i].toInt() and 0xFF
            out[i * 2] = HEX_CHARS[v ushr 4]
            out[i * 2 + 1] = HEX_CHARS[v and 0x0F]
        }
        return String(out)
    }
}
