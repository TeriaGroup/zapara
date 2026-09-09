import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("com.google.devtools.ksp")
}

android {
    namespace = "ru.bgtu_voenmeh.zapara"
    compileSdk = 34

    // Release key lives OUTSIDE the repo (local.properties, gitignored).
    // Keystore: %USERPROFILE%\.keystores\zapara-release.jks (alias zapara). Back it up!
    val keystoreProps = Properties().apply {
        val f = rootProject.file("local.properties")
        if (f.exists()) f.inputStream().use { load(it) }
    }

    defaultConfig {
        applicationId = "ru.zapara.app"
        minSdk = 26
        targetSdk = 34
        versionCode = 24
        versionName = "2.0.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        val apiBase = keystoreProps.getProperty("zapara.apiBaseUrl") ?: ""
        buildConfigField("String", "API_BASE_URL", "\"${apiBase.replace("\\", "\\\\").replace("\"", "\\\"")}\"")
    }
    signingConfigs {
        create("release") {
            val sf = keystoreProps.getProperty("zapara.storeFile")
            if (!sf.isNullOrBlank()) {
                storeFile = file(sf)
                storePassword = keystoreProps.getProperty("zapara.storePassword")
                keyAlias = keystoreProps.getProperty("zapara.keyAlias")
                keyPassword = keystoreProps.getProperty("zapara.keyPassword")
            }
        }
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
            // Unsigned (instead of failing) when local.properties has no key — e.g. fresh checkout.
            if (!keystoreProps.getProperty("zapara.storeFile").isNullOrBlank()) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }
    // github = self-update from GitHub releases; rustore = store build (updates via RuStore only).
    flavorDimensions += "dist"
    productFlavors {
        create("github") {
            dimension = "dist"
            buildConfigField("boolean", "SELF_UPDATE", "true")
        }
        create("rustore") {
            dimension = "dist"
            buildConfigField("boolean", "SELF_UPDATE", "false")
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
        buildConfig = true
    }
    sourceSets.getByName("androidTest").assets.srcDir("$projectDir/schemas")
}

ksp { arg("room.schemaLocation", "$projectDir/schemas") }

// Schema generation must finish before AndroidTest assets are snapshotted.
tasks.configureEach {
    if (name.matches(Regex("merge.*AndroidTestAssets"))) {
        dependsOn(name.replace("merge", "ksp").replace("AndroidTestAssets", "Kotlin"))
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.06.00")
    implementation(composeBom)
    androidTestImplementation(composeBom)

    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.7.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.7.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.7.0")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.navigation:navigation-compose:2.7.7")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")

    // Room persistence (entities/DAO compile in A1, repositories land in A2)
    implementation("androidx.room:room-runtime:2.6.1")
    implementation("androidx.room:room-ktx:2.6.1")
    ksp("androidx.room:room-compiler:2.6.1")
    implementation("androidx.security:security-crypto:1.0.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.8.1")
    androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    androidTestImplementation("androidx.test:core:1.6.1")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.room:room-testing:2.6.1")
    androidTestImplementation("androidx.test.uiautomator:uiautomator:2.3.0")
    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
