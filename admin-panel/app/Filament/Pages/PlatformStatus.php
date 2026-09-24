<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use Filament\Pages\Page;
use Illuminate\Support\Facades\DB;

class PlatformStatus extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Состояние';

    protected static ?string $title = 'Состояние';

    protected static ?string $slug = 'status';

    protected static ?int $navigationSort = 12;

    protected string $view = 'filament.pages.status';

    /**
     * @return list<array{name: string, ok: bool, detail: string}>
     */
    public function rows(): array
    {
        $rows = [];
        $timetable = $this->identifier('Timetable__Schema', 'timetable');
        try {
            $attempt = DB::selectOne('select status, error_code from '.$timetable.'.refresh_attempts order by sequence desc limit 1');
            $status = is_object($attempt) ? (string) $attempt->status : '';
            $error = is_object($attempt) ? (string) ($attempt->error_code ?? '') : '';
            $rows[] = match ($status) {
                'running' => ['name' => 'Парсер расписания', 'ok' => true, 'detail' => 'Сейчас идёт обновление расписания.'],
                'success' => ['name' => 'Парсер расписания', 'ok' => true, 'detail' => 'Последняя попытка обновления прошла.'],
                'failed', 'abandoned' => ['name' => 'Парсер расписания', 'ok' => false, 'detail' => 'Обновление сорвалось'.($error !== '' ? ': '.$error : '.').' На сайте остаётся прежний снимок.'],
                default => ['name' => 'Парсер расписания', 'ok' => false, 'detail' => 'Попыток обновления ещё не было.'],
            };
        } catch (\Throwable) {
            $rows[] = ['name' => 'Парсер расписания', 'ok' => false, 'detail' => 'Таблицы расписания не найдены. Парсер проверить нельзя.'];
        }
        try {
            $failedAfter = DB::selectOne("select error_code from {$timetable}.refresh_attempts where status in ('failed', 'abandoned') and sequence > coalesce((select max(sequence) from {$timetable}.refresh_attempts where status = 'success'), 0) order by sequence desc limit 1");
            $snapshot = DB::selectOne('select s.fetched_at from '.$timetable.'.state t join '.$timetable.'.snapshots s on s.snapshot_id = t.current_snapshot_id');
            if (! is_object($snapshot)) {
                $rows[] = ['name' => 'Снимок расписания', 'ok' => false, 'detail' => 'Сохранённого расписания нет. Сайт не сможет показать пары.'];
            } elseif (is_object($failedAfter)) {
                $code = (string) ($failedAfter->error_code ?? '');
                $rows[] = ['name' => 'Снимок расписания', 'ok' => false, 'detail' => 'После удачного снимка обновление снова сорвалось'.($code !== '' ? ': '.$code : '.').' Показывается прошлый снимок.'];
            } else {
                $at = strtotime((string) $snapshot->fetched_at) ?: 0;
                $stale = $at > 0 && (time() - $at) >= 86400;
                $when = $at > 0 ? gmdate('d.m.Y H:i', $at).' UTC' : 'неизвестно';
                $rows[] = ['name' => 'Снимок расписания', 'ok' => ! $stale, 'detail' => $stale ? 'Снимок старше суток. Последний успех '.$when.'.' : 'Снимок свежий. Последний успех '.$when.'.'];
            }
        } catch (\Throwable) {
            $rows[] = ['name' => 'Снимок расписания', 'ok' => false, 'detail' => 'Таблицы расписания не найдены.'];
        }
        $schemas = [
            'Расписание' => [$timetable.'.schema_version', 'Таблицы расписания открываются.'],
            'Аккаунты' => [$this->identifier('Accounts__Schema', 'accounts').'.users', 'Таблицы пользователей открываются.'],
            'Синхронизация' => [$this->identifier('Sync__Schema', 'sync').'.schema_migrations', 'Таблицы синхронизации открываются.'],
            'Сообщества' => [$this->identifier('Communities__Schema', 'communities').'.schema_migrations', 'Таблицы сообществ открываются.'],
        ];
        foreach ($schemas as $name => $probe) {
            try {
                DB::select('select 1 from '.$probe[0].' limit 1');
                $rows[] = ['name' => $name, 'ok' => true, 'detail' => $probe[1]];
            } catch (\Throwable) {
                $rows[] = ['name' => $name, 'ok' => false, 'detail' => 'Таблицы не найдены. Эта часть сайта сейчас недоступна.'];
            }
        }
        $rows[] = $this->storage();

        return $rows;
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
     * @return array{name: string, ok: bool, detail: string}
     */
    private function storage(): array
    {
        $endpoint = \App\Services\OperatorSettings::read('s3_endpoint') ?? '';
        $region = \App\Services\OperatorSettings::read('s3_region') ?? '';
        $bucket = \App\Services\OperatorSettings::read('s3_bucket') ?? '';
        $access = \App\Services\OperatorSettings::read('s3_access_key') ?? '';
        $secret = \App\Services\OperatorSettings::read('s3_secret') ?? '';
        if ($endpoint === '' || $region === '' || $bucket === '' || $access === '' || $secret === '') {
            return ['name' => 'Хранилище S3', 'ok' => false, 'detail' => 'S3 не заполнено. Новые файлы вошедших людей пишутся на диск сервера.'];
        }
        try {
            $context = stream_context_create(['http' => ['method' => 'GET', 'timeout' => 2, 'ignore_errors' => true]]);
            $body = @file_get_contents($endpoint, false, $context);
            $code = 0;
            if (isset($http_response_header[0]) && preg_match('/\s(\d{3})\s/', (string) $http_response_header[0], $match) === 1) {
                $code = (int) $match[1];
            }
            if ($body === false && $code === 0) {
                return ['name' => 'Хранилище S3', 'ok' => false, 'detail' => 'Бакет не отвечает. Новые файлы вошедших людей сейчас не сохраняются в S3.'];
            }
            if ($code >= 500 || $code === 0) {
                return ['name' => 'Хранилище S3', 'ok' => false, 'detail' => 'Бакет ответил ошибкой HTTP '.$code.'. Новые файлы в S3 не кладутся.'];
            }

            return ['name' => 'Хранилище S3', 'ok' => true, 'detail' => 'Бакет отвечает. Новые файлы вошедших людей пишутся туда.'];
        } catch (\Throwable) {
            return ['name' => 'Хранилище S3', 'ok' => false, 'detail' => 'Бакет не отвечает. Новые файлы вошедших людей сейчас не сохраняются в S3.'];
        }
    }
}
