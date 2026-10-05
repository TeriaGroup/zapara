# Android verify

JDK 17 и Android SDK задаются скриптом (не системным PATH): `local.properties` `sdk.dir`, либо `ANDROID_HOME`, либо `C:\Android\sdk`. JDK: `ZAPARA_JAVA_HOME`, `C:\Android\jdk17`, либо частный Temurin 17 из `%TEMP%\opencode\zapara-android-tools-*`. Эмулятор по умолчанию — AVD `zapara-api34`; если уже есть `emulator-5554` (или любой `emulator-*` в `device`), скрипт берёт его и **не останавливает**.

```powershell
powershell -NoProfile -File android\scripts\verify.ps1
powershell -NoProfile -File android\scripts\verify.ps1 -Out $env:TEMP\zapara-android\manual -Flavor github -KeepEmulator
powershell -NoProfile -File android\scripts\verify.ps1 -Avd Pixel_10 -KeepEmulator
```

Параметры: `-Avd` (по умолчанию `zapara-api34`), `-Out` (`%TEMP%\zapara-android\<yyyyMMdd-HHmmss>`), `-Flavor` (`github` / `rustore`), `-KeepEmulator`. Коды: `0` все тесты зелёные, `1` есть падения, `2` эмулятор не поднялся или сборка не собралась.

Отчёт: `<Out>\report.md` (таблица классов JVM и connected, кадры, logcat пакета). Чеклист §Д11: шаблон `parity-template.md` копируется в `<Out>\parity-android.md`. Кадры — `adb pull` из `files/frames` приложения (`Frames.capture`) плюс `/data/local/tmp/zapara-*`. Чужой эмулятор и чужие пакеты не трогаются; `pm grant` только `ru.zapara.app`.
