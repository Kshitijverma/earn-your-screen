package com.typedpond.companion

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Discovers the TypedPond service ("_typedpond._tcp.") advertised by the
 * Windows laptop on the local network via NSD (mDNS/Bonjour under the hood).
 *
 * Returns "host:port" for the first service resolved, or null if nothing is
 * found before the timeout. The caller then falls back to the manual IP.
 */
class LaptopDiscovery(context: Context) {

    private val nsdManager: NsdManager =
        context.applicationContext.getSystemService(Context.NSD_SERVICE) as NsdManager

    /**
     * @param timeoutMs how long to wait for discovery + resolution.
     * @return "host:port" of the first resolved service, or null.
     */
    suspend fun discoverLaptopAddress(timeoutMs: Long = 3000): String? {
        val result = CompletableDeferred<String?>()

        // Serialize resolve calls: NsdManager.resolveService can only handle one
        // outstanding request at a time on older platforms.
        var resolveInFlight = false

        val resolveListener = object : NsdManager.ResolveListener {
            override fun onResolveFailed(serviceInfo: NsdServiceInfo?, errorCode: Int) {
                Log.w(TAG, "Resolve failed for ${serviceInfo?.serviceName}: $errorCode")
                resolveInFlight = false
            }

            override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                @Suppress("DEPRECATION")
                val host = serviceInfo.host?.hostAddress
                val port = serviceInfo.port
                Log.d(TAG, "Resolved ${serviceInfo.serviceName} -> $host:$port")
                if (host != null && port > 0 && !result.isCompleted) {
                    result.complete("$host:$port")
                }
                resolveInFlight = false
            }
        }

        val discoveryListener = object : NsdManager.DiscoveryListener {
            override fun onStartDiscoveryFailed(serviceType: String?, errorCode: Int) {
                Log.w(TAG, "Start discovery failed: $errorCode")
                if (!result.isCompleted) result.complete(null)
            }

            override fun onStopDiscoveryFailed(serviceType: String?, errorCode: Int) {
                Log.w(TAG, "Stop discovery failed: $errorCode")
            }

            override fun onDiscoveryStarted(serviceType: String?) {
                Log.d(TAG, "Discovery started for $serviceType")
            }

            override fun onDiscoveryStopped(serviceType: String?) {
                Log.d(TAG, "Discovery stopped for $serviceType")
            }

            override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                Log.d(TAG, "Service found: ${serviceInfo.serviceName}")
                if (!resolveInFlight && !result.isCompleted) {
                    resolveInFlight = true
                    @Suppress("DEPRECATION")
                    nsdManager.resolveService(serviceInfo, resolveListener)
                }
            }

            override fun onServiceLost(serviceInfo: NsdServiceInfo) {
                Log.d(TAG, "Service lost: ${serviceInfo.serviceName}")
            }
        }

        return try {
            nsdManager.discoverServices(
                SERVICE_TYPE,
                NsdManager.PROTOCOL_DNS_SD,
                discoveryListener
            )
            withTimeoutOrNull(timeoutMs) { result.await() }
        } catch (e: TimeoutCancellationException) {
            null
        } catch (e: Exception) {
            Log.w(TAG, "Discovery error: ${e.message}")
            null
        } finally {
            runCatching { nsdManager.stopServiceDiscovery(discoveryListener) }
        }
    }

    companion object {
        private const val TAG = "LaptopDiscovery"
        // Trailing dot is intentional; NSD service types are fully qualified.
        private const val SERVICE_TYPE = "_typedpond._tcp."
    }
}
