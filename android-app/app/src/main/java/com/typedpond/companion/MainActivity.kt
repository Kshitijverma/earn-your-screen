package com.typedpond.companion

import android.os.Bundle
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.health.connect.client.PermissionController
import androidx.lifecycle.lifecycleScope
import com.typedpond.companion.databinding.ActivityMainBinding
import kotlinx.coroutines.launch

/**
 * Single-screen control panel for the companion app. Shows today's step count,
 * the remote goal, and the last sync result; lets the user grant Health Connect
 * access, sign in to Firebase, override the laptop IP, and trigger a sync.
 *
 * Routine data relay happens in [StepSyncWorker]; this screen is for setup and
 * at-a-glance status only.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var prefs: AppPrefs
    private lateinit var health: HealthConnectManager
    private val firebase = FirebaseRepository()

    // Health Connect uses a dedicated permission-request contract.
    private val requestPermissions =
        registerForActivityResult(PermissionController.createRequestPermissionResultContract()) { granted ->
            if (granted.containsAll(health.permissions)) {
                Toast.makeText(this, "Step access granted", Toast.LENGTH_SHORT).show()
                StepSyncWorker.schedule(this)
                refresh()
            } else {
                Toast.makeText(this, "Step access denied", Toast.LENGTH_LONG).show()
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        prefs = AppPrefs(this)
        health = HealthConnectManager(this)

        binding.manualIpInput.setText(prefs.manualLaptopIp ?: "")

        binding.grantButton.setOnClickListener { onGrantClicked() }
        binding.signInButton.setOnClickListener { showSignInDialog() }
        binding.syncNowButton.setOnClickListener {
            saveManualIp()
            StepSyncWorker.syncNow(this)
            Toast.makeText(this, "Sync started", Toast.LENGTH_SHORT).show()
        }
        binding.saveIpButton.setOnClickListener {
            saveManualIp()
            Toast.makeText(this, "Laptop IP saved", Toast.LENGTH_SHORT).show()
        }

        // If access is already granted, make sure the periodic sync is running.
        lifecycleScope.launch {
            if (health.isAvailable() && health.hasStepPermission()) {
                StepSyncWorker.schedule(this@MainActivity)
            }
        }
    }

    override fun onResume() {
        super.onResume()
        refresh()
    }

    private fun onGrantClicked() {
        when {
            !health.isAvailable() -> {
                AlertDialog.Builder(this)
                    .setTitle("Health Connect required")
                    .setMessage(
                        "Health Connect isn't available on this device. Install or update " +
                            "it from the Play Store, then make sure Huawei Health is set to " +
                            "sync steps into Health Connect."
                    )
                    .setPositiveButton("OK", null)
                    .show()
            }
            else -> requestPermissions.launch(health.permissions)
        }
    }

    private fun saveManualIp() {
        prefs.manualLaptopIp = binding.manualIpInput.text?.toString()?.trim()
    }

    /** Minimal email/password sign-in for the Firebase fallback path. */
    private fun showSignInDialog() {
        val container = layoutInflater.inflate(R.layout.dialog_signin, null)
        val emailInput = container.findViewById<android.widget.EditText>(R.id.emailInput)
        val passwordInput = container.findViewById<android.widget.EditText>(R.id.passwordInput)

        AlertDialog.Builder(this)
            .setTitle("Firebase sign-in")
            .setView(container)
            .setPositiveButton("Sign in") { _, _ ->
                val email = emailInput.text?.toString()?.trim().orEmpty()
                val password = passwordInput.text?.toString().orEmpty()
                signIn(email, password)
            }
            .setNegativeButton("Cancel", null)
            .show()
    }

    private fun signIn(email: String, password: String) {
        if (email.isEmpty() || password.isEmpty()) {
            Toast.makeText(this, "Enter email and password", Toast.LENGTH_SHORT).show()
            return
        }
        lifecycleScope.launch {
            val ok = firebase.signIn(email, password)
            if (ok) {
                prefs.firebaseUid = firebase.currentUid ?: ""
                Toast.makeText(this@MainActivity, "Signed in", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this@MainActivity, "Sign-in failed", Toast.LENGTH_LONG).show()
            }
            refresh()
        }
    }

    /** Reloads the on-screen status from Health Connect, Firebase, and prefs. */
    private fun refresh() {
        binding.lastSyncText.text = getString(R.string.last_sync_fmt, prefs.lastSyncStatus)
        binding.signInStatusText.text = if (firebase.currentUid != null || prefs.firebaseUid.isNotBlank()) {
            getString(R.string.signed_in)
        } else {
            getString(R.string.not_signed_in)
        }

        lifecycleScope.launch {
            val steps = if (health.isAvailable() && health.hasStepPermission()) {
                runCatching { health.readTodaySteps() }.getOrDefault(prefs.lastSyncSteps)
            } else {
                null
            }
            binding.stepsText.text = when (steps) {
                null -> getString(R.string.steps_unknown)
                else -> getString(R.string.steps_fmt, steps)
            }

            val goal = firebase.getStepGoal()
            binding.goalText.text = when (goal) {
                null -> getString(R.string.goal_unknown)
                else -> getString(R.string.goal_fmt, goal)
            }
        }
    }
}
