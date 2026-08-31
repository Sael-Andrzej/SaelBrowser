plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

val releaseStoreFile = providers.environmentVariable("SAEL_RELEASE_STORE_FILE").orNull
val releaseStorePassword = providers.environmentVariable("SAEL_RELEASE_STORE_PASSWORD").orNull
val releaseKeyAlias = providers.environmentVariable("SAEL_RELEASE_KEY_ALIAS").orNull
val releaseKeyPassword = providers.environmentVariable("SAEL_RELEASE_KEY_PASSWORD").orNull
val releaseSigningConfigured = listOf(
    releaseStoreFile,
    releaseStorePassword,
    releaseKeyAlias,
    releaseKeyPassword
).all { !it.isNullOrBlank() }

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
    compileSdk = 36

    defaultConfig {
        applicationId = "pl.sael.browser"
        minSdk = 26
        targetSdk = 36
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

    signingConfigs {
        if (releaseSigningConfigured) {
            create("release") {
                storeFile = file(requireNotNull(releaseStoreFile))
                storePassword = releaseStorePassword
                keyAlias = releaseKeyAlias
                keyPassword = releaseKeyPassword
                enableV1Signing = true
                enableV2Signing = true
            }
        }
    }

    buildTypes {
        getByName("release") {
            if (releaseSigningConfigured) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }
}

tasks.configureEach {
    if (name in setOf("packageRelease", "bundleRelease", "assembleRelease")) {
        doFirst {
            check(releaseSigningConfigured) {
                "Release signing is not configured. Set all SAEL_RELEASE_* environment variables."
            }
        }
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
