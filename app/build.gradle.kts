plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    buildFeatures {
        buildConfig = true
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    namespace = "pl.sael.browser"
    compileSdk = 35

    defaultConfig {
        applicationId = "pl.sael.browser"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
        fun escapedBuildConfig(value: String) = value.replace("\\", "\\\\").replace("\"", "\\\"")
        val backendUrl = providers.gradleProperty("SAEL_BACKEND_URL")
            .orElse(providers.environmentVariable("SAEL_BACKEND_URL"))
            .getOrElse("https://api.xn--ypay-99a.pl")
        val fallbackBackendUrl = providers.gradleProperty("SAEL_BACKEND_FALLBACK_URL")
            .orElse(providers.environmentVariable("SAEL_BACKEND_FALLBACK_URL"))
            .getOrElse("https://api.alvsal.pl")
        buildConfigField("String", "SAEL_BACKEND_URL", "\"${escapedBuildConfig(backendUrl)}\"")
        buildConfigField(
            "String",
            "SAEL_BACKEND_FALLBACK_URL",
            "\"${escapedBuildConfig(fallbackBackendUrl)}\""
        )
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("org.jsoup:jsoup:1.18.3")
    implementation("com.google.code.gson:gson:2.11.0")
    testImplementation("junit:junit:4.13.2")
}
