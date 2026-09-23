<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use Filament\Pages\Page;
use Illuminate\Support\Facades\DB;

class Quotas extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Квоты';

    protected static ?string $title = 'Квоты';

    protected static ?string $slug = 'quotas';

    protected static ?int $navigationSort = 16;

    protected string $view = 'filament.pages.quotas';

    /**
     * @return list<array{scope: string, id: string, bytes: int, limit: int}>
     */
    public function rows(): array
    {
        $schema = \App\Support\Zapara::settings();
        try {
            DB::statement('CREATE TABLE IF NOT EXISTS '.$schema.'.quota_counters (scope text NOT NULL, scope_id text NOT NULL, bytes bigint NOT NULL, PRIMARY KEY (scope, scope_id))');
            $userLimit = (int) (\App\Services\OperatorSettings::read('quota_user_bytes') ?: '524288000');
            $groupLimit = (int) (\App\Services\OperatorSettings::read('quota_group_bytes') ?: '1073741824');
            return array_map(static function ($row) use ($userLimit, $groupLimit): array {
                $scope = (string) $row->scope;

                return [
                    'scope' => $scope === 'group' ? 'Группа' : 'Студент',
                    'id' => (string) $row->scope_id,
                    'bytes' => (int) $row->bytes,
                    'limit' => $scope === 'group' ? $groupLimit : $userLimit,
                ];
            }, DB::select("select scope, scope_id, bytes from {$schema}.quota_counters order by scope, scope_id"));
        } catch (\Throwable) {
            return [];
        }
    }
}
