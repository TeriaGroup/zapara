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
Android (`gradlew`), `scripts/timetable/Verify-Milestone.ps1`.

`RestoreSchemaTests` и CLI-тесты (`AdminCliTests`, `CommunityCliTests`, `SyncCliTests`) работают: задание передаёт
`ZAPARA_RESTORE_TEST_CONTAINER`, `ZAPARA_RESTORE_TEST_DIRECTORY` и `DOTNET8`.

## Что нужно на раннере

SDK и интерпретаторы ставят сами задания: `actions/setup-dotnet` (.NET 8 в `$RUNNER_TEMP/dotnet`: каталоги по умолчанию пользователю раннера недоступны для записи),
`actions/setup-node` (Node 24), `shivammathur/setup-php` (PHP 8.3, `pdo_pgsql`, `pdo_sqlite`, `intl`, `zip`, `mbstring`, Composer 2).
Предустановить нужно:

- Linux x64, Ubuntu или Debian (setup-php на собственном раннере поддерживает только их);
- `sudo` без пароля для пользователя раннера: setup-php ставит PHP и расширения через `apt`;
- Docker Engine, пользователь раннера в группе `docker`; образ `postgres:16-alpine` скачивается при первом запуске;
- `git`, `curl`, `openssl`, `tar`, `xz-utils`, `unzip`;
- библиотеки для .NET и Avalonia headless: `libicu`, `libssl`, `libfontconfig1`;
- свободные порты `127.0.0.1:56432` и `127.0.0.1:56543`.

Раннер постоянный, поэтому каждое задание в конце (`if: always()`) удаляет свой контейнер вместе с томом
(`docker rm -f -v`), временный каталог архивов и все неотслеживаемые файлы (`git reset --hard`, `git clean -ffdx`).
Контейнеры помечены `zapara-ci=1` и названы `zapara-ci-pg-<run_id>-<attempt>-<job>`; если задание убито вместе с раннером,
остатки можно найти командой `docker ps -a --filter label=zapara-ci=1`. Данные PostgreSQL лежат в tmpfs и на диск не попадают.
Кэши пакетов (`~/.nuget/packages`, кэш npm и Composer) остаются между запусками; секретов в них нет.

## После слияния PR #2

`security/imagesharp` добавляет `.github/workflows/server-release.yml` с `runs-on: ubuntu-latest`. После слияния его стоит
перевести на `[self-hosted, Linux, X64]` с той же защитой от форков, `persist-credentials: false`, закреплёнными по SHA
действиями и шагом очистки. Секрет `SIXLABORS_LICENSE_KEY` в PR-заданиях на собственном раннере появляться не должен.
