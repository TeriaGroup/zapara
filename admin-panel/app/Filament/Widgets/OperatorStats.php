<?php

namespace App\Filament\Widgets;

use App\Filament\Pages\Memberships;
use App\Filament\Pages\PlatformStatus;
use App\Filament\Pages\SupportDesk;
use App\Filament\Support\MoscowTime;
use App\Services\PlatformHealth;
use App\Support\Zapara;
use Filament\Widgets\StatsOverviewWidget;
use Filament\Widgets\StatsOverviewWidget\Stat;
use Illuminate\Support\Facades\DB;

class OperatorStats extends StatsOverviewWidget
{
    protected static ?int $sort = 1;

    protected static bool $isLazy = false;

    protected ?string $pollingInterval = null;

    protected int|array|null $columns = ['default' => 1, 'sm' => 2, 'xl' => 4];

    protected function getStats(): array
    {
        $health = app(PlatformHealth::class);
        $rows = $health->checks();
        $summary = PlatformHealth::summary($rows);
        $pending = $this->pendingJoins();
        [$threads, $waiting] = $this->supportCounts();
        $snapshot = $health->lastSnapshotAt();

        return [
            Stat::make('Заявки ожидают', (string) $pending)
                ->description($pending > 0 ? 'Открыть заявки' : 'Новых заявок нет')
                ->descriptionIcon('heroicon-m-arrow-right')
                ->color($pending > 0 ? 'warning' : 'gray')
                ->url(Memberships::getUrl()),
            Stat::make('Состояние: предупреждения', (string) ($summary['errors'] + $summary['warnings']))
                ->description($summary['text'])
                ->descriptionIcon('heroicon-m-arrow-right')
                ->color($summary['errors'] > 0 ? 'danger' : ($summary['warnings'] > 0 ? 'warning' : 'success'))
                ->url(PlatformStatus::getUrl()),
            Stat::make('Обращения', (string) $threads)
                ->description($waiting > 0 ? 'Ждут ответа: '.$waiting : 'Все с ответом')
                ->descriptionIcon('heroicon-m-arrow-right')
                ->color($waiting > 0 ? 'warning' : 'gray')
                ->url(SupportDesk::getUrl()),
            Stat::make('Обновление расписания', $snapshot === null ? 'Нет данных' : MoscowTime::time($snapshot))
                ->description($snapshot === null ? 'Снимка расписания нет' : MoscowTime::dateTime($snapshot))
                ->descriptionIcon('heroicon-m-arrow-right')
                ->color($snapshot === null ? 'danger' : 'gray')
                ->url(PlatformStatus::getUrl()),
        ];
    }

    private function pendingJoins(): int
    {
        try {
            return DB::table(Zapara::communities().'.join_requests')->where('status', 'pending')->count();
        } catch (\Throwable) {
            return 0;
        }
    }

    /**
     * @return array{0: int, 1: int} всего обращений и сколько ждут ответа (последнее сообщение от пользователя)
     */
    private function supportCounts(): array
    {
        $schema = Zapara::settings();
        try {
            $exists = DB::selectOne('select to_regclass(?)::text as name', [$schema.'.support_messages']);
            if (! is_object($exists) || ! is_string($exists->name) || $exists->name === '') {
                return [0, 0];
            }
            $row = DB::selectOne(
                "select count(*) as total, count(*) filter (where last.author = 'user') as waiting
                 from {$schema}.support_threads t
                 left join lateral (select m.author from {$schema}.support_messages m where m.thread_id = t.thread_id order by m.created_at desc limit 1) last on true"
            );

            return [(int) ($row->total ?? 0), (int) ($row->waiting ?? 0)];
        } catch (\Throwable) {
            return [0, 0];
        }
    }
}
