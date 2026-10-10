# CI на собственном раннере

Workflow `.github/workflows/tests.yml` гоняет тесты на раннере организации с метками `[self-hosted, Linux, X64]`.
Windows-раннера нет: всё, что можно, собирается и проверяется на Linux.

## Когда запускается

- `push` в `develop` и `master`;
- `pull_request` в `develop` и `master`, но только из веток этого репозитория.
  Каждое задание проверяет `github.event.pull_request.head.repo.full_name == github.repository`,
  поэтому код из форков на раннере не выполняется. `pull_request_target` не используется.

Новый запуск для той же ветки или PR отменяет предыдущий (`concurrency`). Права токена: только `contents: read`.
Секреты в заданиях не используются. Пароль тестовой БД и `APP_KEY` создаются на время задания и маскируются в журнале.

В настройках репозитория (Settings → Actions → General) должно быть включено
«Require approval for all outside collaborators».

## Задания

| Задание | Что делает |
| --- | --- |
| `server` | PostgreSQL 16 в контейнере на `127.0.0.1:56543`, `dotnet test src/Zapara.Server.Tests` |
| `admin` | PostgreSQL 16 на `127.0.0.1:56432`, `composer install`, `php artisan test` в `admin-panel` (PanelTest сам вызывает `dotnet test`) |
| `web` | `npm ci`, `node --test` для `web/src/**/*.test.ts`, `npm run build` (tsc --noEmit и vite), `node --test` для `src/Zapara.Web.Tests/*.test.mjs`, `dotnet test src/Zapara.Web.Tests` |
| `desktop` | `Zapara.Client.Domain.Tests`, `Vograph.Timetable.Tests` и `Vograph.Desktop.Tests` с `-p:EnableWindowsTargeting=true` (Avalonia headless, без дисплея) |
| `android` | JDK 17 (Temurin), Android SDK в `$RUNNER_TEMP`, `./gradlew :app:testGithubDebugUnitTest --max-workers=2` в `android`; при падении — отчёты тестов артефактом на 7 дней |

Порты 56432 и 56543 зашиты в тестах как единственные разрешённые порты фикстуры. На раннере они должны быть свободны.

## Что пропускается на Linux

Фильтры записаны в `tests.yml` (`SERVER_TEST_FILTER`, `DESKTOP_TEST_FILTER`, `--test-skip-pattern`).

Только Windows:

- `Vograph.Desktop.Tests.AccountSessionVaultTests` и `AccountSessionVaultFailureTests`: настоящий DPAPI (`ProtectedData`) и права файлов Windows.
- `Vograph.Desktop.Tests.ChatMediaPlayerTests.Playback_cleanup_retries_a_locked_file_then_deletes_it`: блокировка файла в режиме общего доступа Windows.
- `Zapara.Server.Tests.IngestCliTests.Compiled_*`: запускают `$DOTNET_ROOT_X64/dotnet.exe`.
- `Zapara.Server.Tests.SocialMediaTests.Documents_keep_one_safe_extension`: ждёт, что `C:\temp\отчёт.pdf` обрежется до имени файла.
  На Linux `Path.GetFileName` обратную косую черту разделителем не считает. Сервер работает на Linux, так что это стоит поправить в `DocumentPolicy.CleanName`.

Уже падают на `develop`, к раннеру отношения не имеют (`POST /web-api/auth/register` отвечает 403 `csrf_invalid`):

- `Zapara.Server.Tests.WebSessionTests.ConcurrentRefreshIsAtomicAndOldTabCannotWriteAsNewUser`;
- `Zapara.Server.Tests.WebPushTests.BothSyncedTimesDriveDurableClaimsAndNativeRevocationRemovesSubscription`;
- `Zapara.Server.Tests.WebPushTests.DisablingAndUnsubscribingRemoveProtectedEndpointImmediately`;
- `Zapara.Server.Tests.WebPushTests.ExpiredProviderSubscriptionIsRemovedAndUnconfiguredTestNeverPretendsSuccess`.

Устаревшие проверки времён Blazor в `src/Zapara.Web.Tests` (ждут имя «Запара» и старый манифест service worker):

- `push displays only fixed neutral text even if payload contains private or malicious content`;
- `published host-prefixed manifest caches app assets at their actual routes`;
- `manifest cannot cache private or cross-origin URLs outside the app scope`.

Не запускаются вообще: `Zapara.Server.Admin.UiTests` (Playwright и браузеры), `Vograph.Desktop.UiVerify` (FlaUI, только Windows),
инструментальные тесты Android (`src/androidTest`, нужен эмулятор или устройство), рендеры экранов Android
(`src/test/.../design`, Roborazzi: идут только с `DESIGN_OUT`), `scripts/timetable/Verify-Milestone.ps1`.

### Android

У приложения два flavour (`github`, `rustore`), поэтому задачи `testDebugUnitTest` нет. Задание гоняет
`:app:testGithubDebugUnitTest` — вариант, который собирают для релизов с GitHub и запускают локально; варианты `rustore`
отличаются только флагом `SELF_UPDATE`. Локально то же самое: `cd android && ./gradlew :app:testGithubDebugUnitTest`.

`RestoreSchemaTests` и CLI-тесты (`AdminCliTests`, `CommunityCliTests`, `SyncCliTests`) работают: задание передаёт
`ZAPARA_RESTORE_TEST_CONTAINER`, `ZAPARA_RESTORE_TEST_DIRECTORY` и `DOTNET8`.

## Что нужно на раннере

SDK и интерпретаторы ставят сами задания: `actions/setup-dotnet` (.NET 8 в `$RUNNER_TEMP/dotnet`: каталоги по умолчанию пользователю раннера недоступны для записи; задание `desktop` ставит ещё SDK 10, генераторам Avalonia 12 нужен Roslyn 4.14+),
`actions/setup-node` (Node 24), `actions/setup-java` (JDK 17, Temurin) и `gradle/actions/setup-gradle` (`cache-provider: basic` —
открытый кэш на GitHub Actions cache; по умолчанию там коммерческий сервис). Android SDK задание `android` ставит без `sudo` в
`$RUNNER_TEMP/android-sdk`: command-line tools 23.0 (архив сверяется по SHA-256), `platforms;android-34`, `build-tools;34.0.0`;
Gradle home — `$RUNNER_TEMP/gradle-home`. PHP задания не ставят: у пользователя раннера нет `sudo`, а `setup-php` ставит PHP через `apt`.
Предустановить нужно:

- Linux x64, Ubuntu или Debian;
- PHP 8.3 или новее с расширениями `pdo_pgsql`, `pdo_sqlite`, `intl`, `zip`, `mbstring` и Composer 2 в `PATH` пользователя раннера.
  На Ubuntu/Debian (от root): `apt-get install php8.3-cli php8.3-pgsql php8.3-sqlite3 php8.3-intl php8.3-zip php8.3-mbstring composer`
  (имена пакетов зависят от версии PHP в дистрибутиве). Задание `admin` первым шагом проверяет PHP, расширения и Composer;
  если чего-то нет, оно за несколько секунд падает с `::error::` и списком недостающего. `sudo` раннеру не нужен;
- Docker Engine, пользователь раннера в группе `docker`; образ `postgres:16-alpine` скачивается при первом запуске;
- `git`, `curl`, `openssl`, `tar`, `xz-utils`, `unzip`;
- библиотеки для .NET и Avalonia headless: `libicu`, `libssl`, `libfontconfig1` и хотя бы один шрифт (если `libfontconfig1` или шрифтов нет, задание `desktop` распаковывает `libfontconfig1` и `fonts-dejavu-core` из `.deb` в `$RUNNER_TEMP` без root);
- свободные порты `127.0.0.1:56432` и `127.0.0.1:56543`.

Раннер постоянный, поэтому каждое задание в конце (`if: always()`) удаляет свой контейнер вместе с томом
(`docker rm -f -v`), временный каталог архивов и все неотслеживаемые файлы (`git reset --hard`, `git clean -ffdx`).
Контейнеры помечены `zapara-ci=1` и названы `zapara-ci-pg-<run_id>-<attempt>-<job>`; если задание убито вместе с раннером,
остатки можно найти командой `docker ps -a --filter label=zapara-ci=1`. Данные PostgreSQL лежат в tmpfs и на диск не попадают.
Кэши пакетов (`~/.nuget/packages`, кэш npm и Composer) остаются между запусками; секретов в них нет. Задание `android` в конце
удаляет свой SDK и Gradle home из `$RUNNER_TEMP`; зависимости Gradle между запусками приходят из кэша GitHub Actions.

Тесты desktop идут с `TMPDIR=$RUNNER_TEMP`: часть тестов создаёт каталоги в `/tmp/opencode`, а на общем раннере такой каталог может принадлежать другому пользователю.

Задания `web` и `android` идут с `TZ=Europe/Moscow`: тесты форматирования времени ждут московское время, а часы раннера в UTC.

## После слияния PR #2

`security/imagesharp` добавляет `.github/workflows/server-release.yml` с `runs-on: ubuntu-latest`. После слияния его стоит
перевести на `[self-hosted, Linux, X64]` с той же защитой от форков, `persist-credentials: false`, закреплёнными по SHA
действиями и шагом очистки. Секрет `SIXLABORS_LICENSE_KEY` в PR-заданиях на собственном раннере появляться не должен.
