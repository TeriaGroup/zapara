<?php

namespace App\Services;

use App\Filament\Support\MoscowTime;
use Illuminate\Support\Facades\DB;

/**
 * Проверки страницы «Состояние» и виджета на инфопанели.
 * level: error — часть не работает; warning — работает с оговоркой или не настроена; ok — работает.
 */
final class PlatformHealth
{
    public const LEVEL_ORDER = ['error' => 0, 'warning' => 1, 'ok' => 2];

    /**
     * @return list<array{name: string, level: string, badge: string, detail: string}>
     */
    public function checks(): array
    {
        $rows = [];
        $timetable = $this->identifier('Timetable__Schema', 'timetable');
        try {
            $attempt = DB::selectOne('select status, error_code from '.$timetable.'.refresh_attempts order by sequence desc limit 1');
            $status = is_object($attempt) ? (string) $attempt->status : '';
            $error = is_object($attempt) ? (string) ($attempt->error_code ?? '') : '';
            $rows[] = match ($status) {
                'running' => $this->row('Парсер расписания', 'ok', 'Сейчас идёт обновление расписания.'),
                'success' => $this->row('Парсер расписания', 'ok', 'Последнее обновление прошло успешно.'),
                'failed', 'abandoned' => $this->row('Парсер расписания', 'error', 'Обновление сорвалось'.($error !== '' ? ': '.$error : '.').' На сайте остаётся прежний снимок.'),
                default => $this->row('Парсер расписания', 'warning', 'Попыток обновления ещё не было.', 'Нет данных'),
            };
        } catch (\Throwable) {
            $rows[] = $this->row('Парсер расписания', 'error', 'Таблицы расписания не найдены. Парсер проверить нельзя.');
        }
        try {
            $failedAfter = DB::selectOne("select error_code from {$timetable}.refresh_attempts where status in ('failed', 'abandoned') and sequence > coalesce((select max(sequence) from {$timetable}.refresh_attempts where status = 'success'), 0) order by sequence desc limit 1");
            $snapshot = DB::selectOne('select s.fetched_at from '.$timetable.'.state t join '.$timetable.'.snapshots s on s.snapshot_id = t.current_snapshot_id');
            if (! is_object($snapshot)) {
                $rows[] = $this->row('Снимок расписания', 'error', 'Сохранённого расписания нет. Сайт не сможет показать пары.');
            } elseif (is_object($failedAfter)) {
                $code = (string) ($failedAfter->error_code ?? '');
                $rows[] = $this->row('Снимок расписания', 'warning', 'После удачного снимка обновление снова сорвалось'.($code !== '' ? ': '.$code : '.').' Показывается прошлый снимок.', 'Устарел');
            } else {
                $at = strtotime((string) $snapshot->fetched_at) ?: 0;
                $stale = $at > 0 && (time() - $at) >= 86400;
                $when = $at > 0 ? MoscowTime::dateTime((string) $snapshot->fetched_at) : 'неизвестно';
                $rows[] = $stale
                    ? $this->row('Снимок расписания', 'warning', 'Снимок старше суток. Последнее обновление: '.$when.'.', 'Устарел')
                    : $this->row('Снимок расписания', 'ok', 'Снимок свежий. Последнее обновление: '.$when.'.');
            }
        } catch (\Throwable) {
            $rows[] = $this->row('Снимок расписания', 'error', 'Таблицы расписания не найдены.');
        }
        // R2-20: «Данные доступны», а не «Таблицы … открываются» — без языка разработчика.
        $schemas = [
            'Расписание' => [$timetable.'.schema_version', 'Данные доступны.'],
            'Аккаунты' => [$this->identifier('Accounts__Schema', 'accounts').'.users', 'Данные доступны.'],
            'Синхронизация' => [$this->identifier('Sync__Schema', 'sync').'.schema_migrations', 'Данные доступны.'],
            'Сообщества' => [$this->identifier('Communities__Schema', 'communities').'.schema_migrations', 'Данные доступны.'],
        ];
        foreach ($schemas as $name => $probe) {
            try {
                DB::select('select 1 from '.$probe[0].' limit 1');
                $rows[] = $this->row($name, 'ok', $probe[1]);
            } catch (\Throwable) {
                $rows[] = $this->row($name, 'error', 'Таблицы не найдены. Эта часть сайта сейчас недоступна.');
            }
        }
        $rows[] = $this->storage();

        return $rows;
    }

    /**
     * Проблемы первыми, порядок внутри уровня сохраняется.
     *
     * @return list<array{name: string, level: string, badge: string, detail: string}>
     */
    public function sorted(): array
    {
        $rows = $this->checks();
        $indexed = [];
        foreach ($rows as $i => $row) {
            $indexed[] = [self::LEVEL_ORDER[$row['level']] ?? 9, $i, $row];
        }
        usort($indexed, fn (array $a, array $b): int => [$a[0], $a[1]] <=> [$b[0], $b[1]]);

        return array_map(fn (array $item): array => $item[2], $indexed);
    }

    /**
     * @param  list<array{level: string}>  $rows
     * @return array{errors: int, warnings: int, text: string}
     */
    public static function summary(array $rows): array
    {
        $errors = count(array_filter($rows, fn (array $row): bool => $row['level'] === 'error'));
        $warnings = count(array_filter($rows, fn (array $row): bool => $row['level'] === 'warning'));
        $parts = [];
        if ($errors > 0) {
            $parts[] = $errors.' '.self::plural($errors, 'проблема', 'проблемы', 'проблем');
        }
        if ($warnings > 0) {
            $parts[] = $warnings.' '.self::plural($warnings, 'предупреждение', 'предупреждения', 'предупреждений');
        }

        return ['errors' => $errors, 'warnings' => $warnings, 'text' => $parts === [] ? 'Всё работает' : implode(', ', $parts)];
    }

    public static function plural(int $n, string $one, string $few, string $many): string
    {
        $mod100 = $n % 100;
        $mod10 = $n % 10;
        if ($mod100 >= 11 && $mod100 <= 14) {
            return $many;
        }

        return match (true) {
            $mod10 === 1 => $one,
            $mod10 >= 2 && $mod10 <= 4 => $few,
            default => $many,
        };
    }

    /**
     * Время последнего удачного снимка расписания или null.
     */
    public function lastSnapshotAt(): ?string
    {
        try {
            $timetable = $this->identifier('Timetable__Schema', 'timetable');
            $snapshot = DB::selectOne('select s.fetched_at from '.$timetable.'.state t join '.$timetable.'.snapshots s on s.snapshot_id = t.current_snapshot_id');

            return is_object($snapshot) && $snapshot->fetched_at !== null ? (string) $snapshot->fetched_at : null;
        } catch (\Throwable) {
            return null;
        }
    }

    /**
     * @return array{name: string, level: string, badge: string, detail: string}
     */
    private function row(string $name, string $level, string $detail, ?string $badge = null): array
    {
        return [
            'name' => $name,
            'level' => $level,
            'badge' => $badge ?? match ($level) {
                'ok' => 'Работает',
                'warning' => 'Внимание',
                default => 'Не работает',
            },
            'detail' => $detail,
        ];
    }

    private function identifier(string $key, string $fallback): string
    {
        $raw = $_ENV[$key] ?? getenv($key);
        if (! is_string($raw) || preg_match('/\A[a-z][a-z0-9_]{0,62}\z/', $raw) !== 1) {
            return $fallback;
        }

        return $raw;
    }

    /**
     * @return array{name: string, level: string, badge: string, detail: string}
     */
    private function storage(): array
    {
        $endpoint = OperatorSettings::read('s3_endpoint') ?? '';
        $region = OperatorSettings::read('s3_region') ?? '';
        $bucket = OperatorSettings::read('s3_bucket') ?? '';
        $access = OperatorSettings::read('s3_access_key') ?? '';
        $secret = OperatorSettings::read('s3_secret') ?? '';
        if ($endpoint === '' || $region === '' || $bucket === '' || $access === '' || $secret === '') {
            return $this->row('Хранилище S3', 'warning', 'S3 не настроено. Файлы сейчас сохраняются на диск сервера, загрузка работает. Чтобы хранить их в S3, заполните раздел «Хранилище» в настройках системы.', 'Не настроено');
        }
        try {
            $context = stream_context_create(['http' => ['method' => 'GET', 'timeout' => 2, 'ignore_errors' => true]]);
            $body = @file_get_contents($endpoint, false, $context);
            $code = 0;
            if (isset($http_response_header[0]) && preg_match('/\s(\d{3})\s/', (string) $http_response_header[0], $match) === 1) {
                $code = (int) $match[1];
            }
            if ($body === false && $code === 0) {
                return $this->row('Хранилище S3', 'error', 'Бакет не отвечает. Новые файлы вошедших людей сейчас не сохраняются в S3.');
            }
            if ($code >= 500 || $code === 0) {
                return $this->row('Хранилище S3', 'error', 'Бакет ответил ошибкой HTTP '.$code.'. Новые файлы в S3 не кладутся.');
            }

            return $this->row('Хранилище S3', 'ok', 'Бакет отвечает. Новые файлы вошедших людей пишутся туда.');
        } catch (\Throwable) {
            return $this->row('Хранилище S3', 'error', 'Бакет не отвечает. Новые файлы вошедших людей сейчас не сохраняются в S3.');
        }
    }
}
