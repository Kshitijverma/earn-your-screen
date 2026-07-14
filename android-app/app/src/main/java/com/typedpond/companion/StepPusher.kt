package com.typedpond.companion

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.TimeUnit

/**
 * Pushes a signed step update to the Windows laptop over the local network.
 *
 * POST http://{baseUrl}/api/steps
 *   { "steps": <int>, "date": "<yyyy-MM-dd>", "hmac": "<hex>" }
 *
 * The body shape matches the C# StepUpdateRequest record
 * (record StepUpdateRequest(int Steps, string? Date, string? Hmac)).
 */
class StepPusher {

    private val client: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(3, TimeUnit.SECONDS)
        .readTimeout(3, TimeUnit.SECONDS)
        .writeTimeout(3, TimeUnit.SECONDS)
        .callTimeout(3, TimeUnit.SECONDS)
        .build()

    /**
     * @param baseUrl "host" or "host:port" (no scheme).
     * @return true only when the laptop responds with a 2xx status.
     */
    suspend fun pushToLaptop(
        baseUrl: String,
        steps: Long,
        date: String,
        hmac: String
    ): Boolean = withContext(Dispatchers.IO) {
        val json = JSONObject().apply {
            // steps is an int on the C# side; keep it integral.
            put("steps", steps)
            put("date", date)
            put("hmac", hmac)
        }.toString()

        val request = Request.Builder()
            .url("http://${baseUrl.trimEnd('/')}/api/steps")
            .post(json.toRequestBody(JSON_MEDIA_TYPE))
            .build()

        try {
            client.newCall(request).execute().use { response ->
                val ok = response.isSuccessful
                Log.d(TAG, "Laptop push to $baseUrl -> HTTP ${response.code} (ok=$ok)")
                ok
            }
        } catch (e: IOException) {
            // Timeout, unreachable host, connection refused, etc.
            Log.w(TAG, "Laptop push to $baseUrl failed: ${e.message}")
            false
        }
    }

    companion object {
        private const val TAG = "StepPusher"
        private val JSON_MEDIA_TYPE = "application/json; charset=utf-8".toMediaType()
    }
}
