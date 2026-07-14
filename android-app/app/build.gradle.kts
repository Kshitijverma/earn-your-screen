plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    // Consumes app/google-services.json. See note below — that file must be
    // supplied manually; it is intentionally NOT committed to the repo.
    id("com.google.gms.google-services")
}

android {
    namespace = "com.typedpond.companion"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.typedpond.companion"
        minSdk = 26
        targetSdk = 34
        versionCode = 1
        versionName = "1.0"

        // Shared HMAC secret, sourced from a Gradle property so the real value
        // never has to live in version control. Falls back to the placeholder.
        // Must byte-for-byte match the C# service's TypedPond:HmacSecret.
        val hmacSecret = (project.findProperty("typedpondHmacSecret") as String?)
            ?: "CHANGE-THIS-SECRET"
        buildConfigField("String", "DEFAULT_HMAC_SECRET", "\"$hmacSecret\"")

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
        debug {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        buildConfig = true
        viewBinding = true
    }
}

dependencies {
    // --- AndroidX / UI ---
    implementation("androidx.core:core-ktx:1.12.0")
    implementation("androidx.appcompat:appcompat:1.6.1")
    implementation("com.google.android.material:material:1.11.0")
    implementation("androidx.activity:activity-ktx:1.8.2")

    // --- Coroutines ---
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")
    // await() bridge for Firebase Task<T>
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-play-services:1.7.3")

    // --- Health Connect ---
    implementation("androidx.health.connect:connect-client:1.1.0-alpha07")

    // --- WorkManager (periodic background sync) ---
    implementation("androidx.work:work-runtime-ktx:2.9.0")

    // --- Networking (laptop push) ---
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // --- Firebase (fallback path) ---
    implementation(platform("com.google.firebase:firebase-bom:32.7.0"))
    implementation("com.google.firebase:firebase-database-ktx")
    implementation("com.google.firebase:firebase-auth-ktx")

    // --- Test ---
    testImplementation("junit:junit:4.13.2")
    androidTestImplementation("androidx.test.ext:junit:1.1.5")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.5.1")
}
