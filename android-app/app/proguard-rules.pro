# TypedPond companion app ProGuard / R8 rules.

# ---------------------------------------------------------------------------
# HMAC secret handling
# ---------------------------------------------------------------------------
# The shared HMAC secret is injected at build time into BuildConfig
# (BuildConfig.DEFAULT_HMAC_SECRET). R8 will constant-fold and keep it as a
# string literal in the DEX regardless of obfuscation, so obfuscation does NOT
# hide it from anyone who decompiles the APK. This is acceptable for a
# single-user utility on a device the owner controls, but do NOT treat the
# baked-in secret as a real security boundary. For anything stronger, ship the
# secret via AppPrefs (entered once on the device) instead of BuildConfig.
# We keep BuildConfig so the field name survives if referenced reflectively.
-keep class com.typedpond.companion.BuildConfig { *; }

# ---------------------------------------------------------------------------
# Firebase (RTDB + Auth)
# ---------------------------------------------------------------------------
# RTDB deserializes into model classes reflectively; keep them and their
# members. This app only writes primitives, but keep the rule as a safety net
# in case value objects are added later.
-keep class com.typedpond.companion.** { *; }
-keepattributes Signature
-keepattributes *Annotation*
-keepattributes EnclosingMethod
-keepattributes InnerClasses

# Firebase / Google Play services generally ship their own consumer rules,
# but keep annotations they rely on for reflection.
-keep class com.google.firebase.** { *; }
-keep class com.google.android.gms.** { *; }
-dontwarn com.google.firebase.**
-dontwarn com.google.android.gms.**

# ---------------------------------------------------------------------------
# OkHttp (has its own consumer rules; suppress benign warnings)
# ---------------------------------------------------------------------------
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn org.conscrypt.**

# ---------------------------------------------------------------------------
# Health Connect client
# ---------------------------------------------------------------------------
-dontwarn androidx.health.connect.**

# Kotlin coroutines
-dontwarn kotlinx.coroutines.**
