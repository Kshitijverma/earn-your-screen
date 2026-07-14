package com.typedpond.companion

import android.content.Context
import androidx.health.connect.client.HealthConnectClient
import androidx.health.connect.client.aggregate.AggregationResult
import androidx.health.connect.client.permission.HealthPermission
import androidx.health.connect.client.records.StepsRecord
import androidx.health.connect.client.request.AggregateRequest
import androidx.health.connect.client.time.TimeRangeFilter
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Wraps [HealthConnectClient] for the one thing this app needs: reading the
 * user's total step count for the current local day.
 */
class HealthConnectManager(private val context: Context) {

    /** Permissions requested by the app (READ_STEPS only). */
    val permissions: Set<String> = setOf(
        HealthPermission.getReadPermission(StepsRecord::class)
    )

    /**
     * Availability status of Health Connect on this device. One of
     * [HealthConnectClient.SDK_AVAILABLE],
     * [HealthConnectClient.SDK_UNAVAILABLE], or
     * [HealthConnectClient.SDK_UNAVAILABLE_PROVIDER_UPDATE_REQUIRED].
     */
    fun availability(): Int = HealthConnectClient.getSdkStatus(context)

    /** True if the Health Connect SDK is available and usable. */
    fun isAvailable(): Boolean = availability() == HealthConnectClient.SDK_AVAILABLE

    /**
     * The underlying client. Only valid when [isAvailable] is true; callers
     * must gate on availability first.
     */
    val client: HealthConnectClient by lazy { HealthConnectClient.getOrCreate(context) }

    /** True if READ_STEPS has already been granted. */
    suspend fun hasStepPermission(): Boolean {
        if (!isAvailable()) return false
        val granted = client.permissionController.getGrantedPermissions()
        return granted.containsAll(permissions)
    }

    /**
     * Aggregates today's total step count (local midnight -> now).
     *
     * Returns 0 when there is no data or the permission has not been granted,
     * so callers get a well-defined value rather than an exception.
     */
    suspend fun readTodaySteps(): Long {
        if (!isAvailable() || !hasStepPermission()) {
            return 0L
        }

        val zone = ZoneId.systemDefault()
        val startOfDay: LocalDateTime = LocalDate.now(zone).atStartOfDay()
        val now: LocalDateTime = LocalDateTime.now(zone)

        val response: AggregationResult = client.aggregate(
            AggregateRequest(
                metrics = setOf(StepsRecord.COUNT_TOTAL),
                timeRangeFilter = TimeRangeFilter.between(startOfDay, now)
            )
        )

        // COUNT_TOTAL is null when no StepsRecord exists in the window.
        return response[StepsRecord.COUNT_TOTAL] ?: 0L
    }
}
