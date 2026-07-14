package com.typedpond.companion

import android.content.Context
import android.content.SharedPreferences

/**
 * Thin SharedPreferences wrapper for the small amount of local state the
 * companion app needs to persist across launches and background worker runs.
 */
class AppPrefs(context: Context) {

    private val prefs: SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    /**
     * Optional manual override for the laptop address ("host" or "host:port").
     * Used when NSD discovery fails. Null/blank means "not set".
     */
    var manualLaptopIp: String?
        get() = prefs.getString(KEY_MANUAL_LAPTOP_IP, null)?.takeIf { it.isNotBlank() }
        set(value) = prefs.edit().putString(KEY_MANUAL_LAPTOP_IP, value?.trim()).apply()

    /**
     * Shared HMAC secret. Defaults to the value baked into BuildConfig so the
     * app works out of the box, but can be overridden on-device.
     */
    var hmacSecret: String
        get() = prefs.getString(KEY_HMAC_SECRET, null)
            ?.takeIf { it.isNotBlank() }
            ?: BuildConfig.DEFAULT_HMAC_SECRET
        set(value) = prefs.edit().putString(KEY_HMAC_SECRET, value).apply()

    /** Firebase UID of the signed-in user (fallback push path + RTDB rules). */
    var firebaseUid: String
        get() = prefs.getString(KEY_FIREBASE_UID, "") ?: ""
        set(value) = prefs.edit().putString(KEY_FIREBASE_UID, value).apply()

    /** Human-readable result of the most recent sync attempt. */
    var lastSyncStatus: String
        get() = prefs.getString(KEY_LAST_SYNC_STATUS, "Never synced") ?: "Never synced"
        set(value) = prefs.edit().putString(KEY_LAST_SYNC_STATUS, value).apply()

    /** Step count that was last successfully synced. */
    var lastSyncSteps: Long
        get() = prefs.getLong(KEY_LAST_SYNC_STEPS, 0L)
        set(value) = prefs.edit().putLong(KEY_LAST_SYNC_STEPS, value).apply()

    companion object {
        private const val PREFS_NAME = "typedpond_prefs"
        private const val KEY_MANUAL_LAPTOP_IP = "manual_laptop_ip"
        private const val KEY_HMAC_SECRET = "hmac_secret"
        private const val KEY_FIREBASE_UID = "firebase_uid"
        private const val KEY_LAST_SYNC_STATUS = "last_sync_status"
        private const val KEY_LAST_SYNC_STEPS = "last_sync_steps"
    }
}
