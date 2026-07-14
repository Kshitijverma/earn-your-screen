package com.typedpond.companion

import android.util.Log
import com.google.firebase.auth.FirebaseAuth
import com.google.firebase.auth.ktx.auth
import com.google.firebase.database.FirebaseDatabase
import com.google.firebase.database.ktx.database
import com.google.firebase.ktx.Firebase
import kotlinx.coroutines.tasks.await

/**
 * Fallback data path: writes step data straight to the Firebase Realtime
 * Database, matching the schema the Windows service reads:
 *
 *   /users/{uid}/steps/{yyyy-MM-dd} = <steps as number>
 *   /config/stepGoal                = <int>
 *
 * RTDB rules require an authenticated user whose uid matches the path, so
 * [signIn] must succeed before [pushSteps].
 */
class FirebaseRepository {

    private val auth: FirebaseAuth by lazy { Firebase.auth }
    private val database: FirebaseDatabase by lazy { Firebase.database }

    /** Currently signed-in uid, or null. */
    val currentUid: String?
        get() = auth.currentUser?.uid

    /**
     * Signs in with email/password.
     * @return true on success, false on any auth failure.
     */
    suspend fun signIn(email: String, password: String): Boolean {
        return try {
            val result = auth.signInWithEmailAndPassword(email, password).await()
            result.user != null
        } catch (e: Exception) {
            Log.w(TAG, "Firebase sign-in failed: ${e.message}")
            false
        }
    }

    /**
     * Writes today's step count to /users/{uid}/steps/{date}.
     * Stored as a Long so it deserializes as a JSON number on the C# side.
     *
     * @throws Exception if the write fails (caller decides retry vs. give up).
     */
    suspend fun pushSteps(uid: String, date: String, steps: Long) {
        database.reference
            .child("users")
            .child(uid)
            .child("steps")
            .child(date)
            .setValue(steps)
            .await()
        Log.d(TAG, "Firebase push OK: users/$uid/steps/$date = $steps")
    }

    /**
     * Reads the remotely configured step goal from /config/stepGoal.
     * @return the goal, or null if missing / unreadable.
     */
    suspend fun getStepGoal(): Int? {
        return try {
            val snapshot = database.reference
                .child("config")
                .child("stepGoal")
                .get()
                .await()
            // Value may come back as Long (RTDB stores integers as Long).
            when (val value = snapshot.value) {
                is Long -> value.toInt()
                is Int -> value
                is String -> value.toIntOrNull()
                else -> null
            }
        } catch (e: Exception) {
            Log.w(TAG, "Failed to read step goal: ${e.message}")
            null
        }
    }

    companion object {
        private const val TAG = "FirebaseRepository"
    }
}
