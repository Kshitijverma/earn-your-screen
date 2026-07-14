package com.typedpond.companion

import android.content.Context
import android.util.Log
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.time.LocalDate
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.concurrent.TimeUnit

/**
 * Periodic background job that reads today's step count from Health Connect
 * and relays it to the Windows laptop.
 *
 * Push strategy (matches the design's hybrid data path):
 *   1. Primary  — POST to the laptop over local WiFi (discovered via NSD, or a
 *      manual IP from [AppPrefs]).
 *   2. Fallback — write to Firebase RTDB when the laptop cannot be reached
 *      (e.g. the phone is on cellular / away from home).
 *
 * The step count is signed with an HMAC that the laptop validates, so a device
 * on the LAN cannot spoof step data.
 */
class StepSyncWorker(
    appContext: Context,
    params: WorkerParameters
) : CoroutineWorker(appContext, params) {

    private val prefs = AppPrefs(appContext)
    private val health = HealthConnectManager(appContext)
    private val discovery = LaptopDiscovery(appContext)
    private val pusher = StepPusher()
    private val firebase = FirebaseRepository()

    override suspend fun doWork(): Result {
        if (!health.isAvailable()) {
            prefs.lastSyncStatus = "Health Connect unavailable"
            return Result.success()
        }

        if (!health.hasStepPermission()) {
            prefs.lastSyncStatus = "Step permission not granted"
            // Nothing we can do until the user grants the permission in-app.
            return Result.success()
        }

        val steps = try {
            health.readTodaySteps()
        } catch (e: Exception) {
            Log.w(TAG, "Failed to read steps: ${e.message}")
            prefs.lastSyncStatus = "Could not read steps"
            return Result.retry()
        }

        // Local (LAN) push uses the phone's LOCAL date: the laptop is normally
        // co-located, stores steps under this date, and reads "today" by its own
        // local date. The HMAC is computed over this same local date.
        val localDate = LocalDate.now(ZoneId.systemDefault()).format(DATE_FORMAT)
        val hmac = HmacSigner.sign(steps, localDate, prefs.hmacSecret)

        // Firebase (fallback) push uses the UTC date so the RTDB key always
        // matches what the Windows service reads with DateTime.UtcNow — even when
        // the phone is away from home in a different time zone than the laptop.
        val utcDate = LocalDate.now(ZoneOffset.UTC).format(DATE_FORMAT)

        // 1. Try the laptop directly (LAN). Prefer a discovered address, fall
        //    back to the manually configured one.
        val laptopAddress = discovery.discoverLaptopAddress() ?: prefs.manualLaptopIp
        if (laptopAddress != null) {
            val pushed = pusher.pushToLaptop(laptopAddress, steps, localDate, hmac)
            if (pushed) {
                prefs.lastSyncSteps = steps
                prefs.lastSyncStatus = "Synced to laptop ($steps steps)"
                Log.d(TAG, "Synced $steps steps to laptop at $laptopAddress")
                return Result.success()
            }
        }

        // 2. Fallback: Firebase. Requires a signed-in user whose uid matches
        //    the RTDB path (sign-in happens in MainActivity / on launch).
        val uid = firebase.currentUid ?: prefs.firebaseUid.takeIf { it.isNotBlank() }
        if (uid == null) {
            prefs.lastSyncStatus = "Laptop unreachable, not signed in to Firebase"
            return Result.retry()
        }

        return try {
            firebase.pushSteps(uid, utcDate, steps)
            prefs.lastSyncSteps = steps
            prefs.lastSyncStatus = "Synced to cloud ($steps steps)"
            Log.d(TAG, "Synced $steps steps to Firebase for uid=$uid")
            Result.success()
        } catch (e: Exception) {
            Log.w(TAG, "Firebase push failed: ${e.message}")
            prefs.lastSyncStatus = "Sync failed (retrying)"
            Result.retry()
        }
    }

    companion object {
        private const val TAG = "StepSyncWorker"
        private const val WORK_NAME = "typedpond-step-sync"
        private val DATE_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd")

        /**
         * Enqueues the periodic sync (every 15 minutes — the WorkManager
         * minimum). Uses KEEP so re-scheduling on each launch does not reset an
         * already-running schedule.
         */
        fun schedule(context: Context) {
            val request = PeriodicWorkRequestBuilder<StepSyncWorker>(15, TimeUnit.MINUTES)
                .build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                WORK_NAME,
                ExistingPeriodicWorkPolicy.KEEP,
                request
            )
        }

        /** Runs a one-off sync immediately (used by the "Sync now" button). */
        fun syncNow(context: Context) {
            val request = androidx.work.OneTimeWorkRequestBuilder<StepSyncWorker>().build()
            WorkManager.getInstance(context).enqueue(request)
        }
    }
}
