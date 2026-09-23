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
                'running' => ['name' => 'Парсер расписания', 'ok' => true, 'detail' => 'Работает: обновление идёт'],
                'success' => ['name' => 'Парсер расписания', 'ok' => true, 'detail' => 'Работает'],
                'failed', 'abandoned' => ['name' => 'Парсер расписания', 'ok' => false, 'detail' => 'Не работает: '.($error !== '' ? $error : 'сбой парсера')],
                default => ['name' => 'Парсер расписания', 'ok' => false, 'detail' => 'Не работает: нет попыток обновления'],
            };
        } catch (\Throwable) {
            $rows[] = ['name' => 'Парсер расписания', 'ok' => false, 'detail' => 'Не работает: схема '.$timetable.' отсутствует'];
        }
        try {
            $failedAfter = DB::selectOne("select error_code from {$timetable}.refresh_attempts where status in ('failed', 'abandoned') and sequence > coalesce((select max(sequence) from {$timetable}.refresh_attempts where status = 'success'), 0) order by sequence desc limit 1");
            $snapshot = DB::selectOne('select s.fetched_at from '.$timetable.'.state t join '.$timetable.'.snapshots s on s.snapshot_id = t.current_snapshot_id');
            if (! is_object($snapshot)) {
                $rows[] = ['name' => 'Снимок расписания', 'ok' => false, 'detail' => 'Не работает: снимка нет'];
            } elseif (is_object($failedAfter)) {
                $code = (string) ($failedAfter->error_code ?? '');
                $rows[] = ['name' => 'Снимок расписания', 'ok' => false, 'detail' => 'Не работает: обновление после успеха не удалось'.($code !== '' ? '. '.$code : '')];
            } else {
                $at = strtotime((string) $snapshot->fetched_at) ?: 0;
                $stale = $at > 0 && (time() - $at) >= 86400;
                $rows[] = ['name' => 'Снимок расписания', 'ok' => ! $stale, 'detail' => ($stale ? 'Не работает: снимок устарел. ' : 'Свежий. ').'последний успех '.gmdate('Y-m-d H:i', $at).'Z'];
            }
        } catch (\Throwable) {
            $rows[] = ['name' => 'Снимок расписания', 'ok' => false, 'detail' => 'Не работает: схема '.$timetable.' отсутствует'];
        }
        $schemas = [
            'timetable' => $timetable.'.schema_version',
            'accounts' => $this->identifier('Accounts__Schema', 'accounts').'.users',
            'sync' => $this->identifier('Sync__Schema', 'sync').'.schema_migrations',
            'communities' => $this->identifier('Communities__Schema', 'communities').'.schema_migrations',
        ];
        foreach ($schemas as $name => $table) {
            try {
                DB::select('select 1 from '.$table.' limit 1');
                $rows[] = ['name' => $name, 'ok' => true, 'detail' => 'Работает'];
            } catch (\Throwable) {
                $rows[] = ['name' => $name, 'ok' => false, 'detail' => 'Не работает: схема '.$name.' отсутствует'];
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
            return ['name' => 'S3', 'ok' => false, 'detail' => 'Не настроено'];
        }
        try {
            $context = stream_context_create(['http' => ['method' => 'GET', 'timeout' => 2, 'ignore_errors' => true]]);
            $body = @file_get_contents($endpoint, false, $context);
            $code = 0;
            if (isset($http_response_header[0]) && preg_match('/\s(\d{3})\s/', (string) $http_response_header[0], $match) === 1) {
                $code = (int) $match[1];
            }
            if ($body === false && $code === 0) {
                return ['name' => 'S3', 'ok' => false, 'detail' => 'Не работает: хранилище не отвечает'];
            }
            if ($code >= 500 || $code === 0) {
                return ['name' => 'S3', 'ok' => false, 'detail' => 'Не работает: HTTP '.$code];
            }

            return ['name' => 'S3', 'ok' => true, 'detail' => 'Доступно'];
        } catch (\Throwable) {
            return ['name' => 'S3', 'ok' => false, 'detail' => 'Не работает: хранилище не отвечает'];
        }
    }
}
