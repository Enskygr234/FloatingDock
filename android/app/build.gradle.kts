plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.winterdeveloper.floatingdock"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.winterdeveloper.floatingdock"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
    }

    // Imza anahtari repoda DEGIL: GitHub Secrets'tan gelir (FD_KEYSTORE_B64, FD_KEYSTORE_PASSWORD).
    // Yoksa Android'in gecici hata ayiklama anahtari kullanilir (o zaman her yeni APK icin eskisini kaldirmak gerekir).
    val ks = System.getenv("FD_KEYSTORE_FILE")?.let { file(it) }?.takeIf { it.exists() }
    signingConfigs {
        if (ks != null) create("fd") {
            storeFile = ks
            storePassword = System.getenv("FD_KEYSTORE_PASSWORD")
            keyAlias = System.getenv("FD_KEY_ALIAS") ?: "floatingdock"
            keyPassword = System.getenv("FD_KEYSTORE_PASSWORD")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (ks != null) signingConfigs.getByName("fd") else signingConfigs.getByName("debug")
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
        compose = true
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-core")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.savedstate:savedstate-ktx:1.2.1")
}
