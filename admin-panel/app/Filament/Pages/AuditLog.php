<?php

namespace App\Filament\Pages;

use App\Filament\Support\MoscowTime;
use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Models\AdminAudit;
use App\Support\AuditDictionary;
use App\Support\AuditObjects;
use App\Support\Zapara;
use Filament\Forms\Components\DatePicker;
use Filament\Pages\Page;
use Filament\Schemas\Components\EmbeddedTable;
use Filament\Schemas\Schema;
use Filament\Tables\Columns\TextColumn;
use Filament\Tables\Concerns\InteractsWithTable;
use Filament\Tables\Contracts\HasTable;
use Filament\Tables\Filters\Filter;
use Filament\Tables\Filters\SelectFilter;
use Filament\Tables\Table;
use Illuminate\Database\Eloquent\Builder;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class AuditLog extends Page implements HasTable
{
    use GuardsPlatformAdmin;
    use InteractsWithTable;

    protected static ?string $navigationLabel = 'Журнал';

    protected static ?string $title = 'Журнал';

    protected static ?string $slug = 'audit';

    protected static ?int $navigationSort = 80;

    public function content(Schema $schema): Schema
    {
        return $schema->components([
            EmbeddedTable::make(),
        ]);
    }

    public function table(Table $table): Table
    {
        return static::configureTable($table)
            ->query(AdminAudit::query()->with('actor'))
            ->filters([
                SelectFilter::make('actor_id')
                    ->label('Кто')
                    ->placeholder('Все')
                    ->options(fn (): array => static::actorOptions())
                    ->searchable(),
                SelectFilter::make('action')
                    ->label('Действие')
                    ->placeholder('Все')
                    ->multiple()
                    ->options(AuditDictionary::ACTIONS),
                Filter::make('created_at')
                    ->label('Когда')
                    ->schema([
                        DatePicker::make('from')->label('С')->native(false)->displayFormat('d.m.Y'),
                        DatePicker::make('until')->label('По')->native(false)->displayFormat('d.m.Y'),
                    ])
                    ->query(function (Builder $query, array $data): Builder {
                        if (filled($data['from'] ?? null)) {
                            $query->where('created_at', '>=', Carbon::parse($data['from'], 'Europe/Moscow')->startOfDay()->utc());
                        }
                        if (filled($data['until'] ?? null)) {
                            $query->where('created_at', '<', Carbon::parse($data['until'], 'Europe/Moscow')->addDay()->startOfDay()->utc());
                        }

                        return $query;
                    })
                    ->indicateUsing(function (array $data): array {
                        $indicators = [];
                        if (filled($data['from'] ?? null)) {
                            $indicators[] = 'С '.Carbon::parse($data['from'])->format('d.m.Y');
                        }
                        if (filled($data['until'] ?? null)) {
                            $indicators[] = 'По '.Carbon::parse($data['until'])->format('d.m.Y');
                        }

                        return $indicators;
                    }),
            ]);
    }

    /**
     * Общая настройка таблицы журнала: страница «Журнал» и блок «Последние действия» на инфопанели.
     */
    public static function configureTable(Table $table): Table
    {
        $objects = app(AuditObjects::class);

        return $table
            ->columns([
                TextColumn::make('created_at')
                    ->label('Когда (МСК)')
                    ->formatStateUsing(fn ($state): string => MoscowTime::short($state)) // G-3: «9 окт., 18:49», год — если не текущий
                    ->sortable(),
                TextColumn::make('actor.username')
                    ->label('Кто')
                    ->placeholder('Система')
                    ->description(fn (AdminAudit $record): ?string => static::actorNote($record->actor?->username, $record->actor?->display_name)),
                TextColumn::make('action')
                    ->label('Действие')
                    ->formatStateUsing(fn (string $state): string => AuditDictionary::action($state))
                    ->weight('medium'),
                TextColumn::make('object_id')
                    ->label('Объект')
                    ->formatStateUsing(fn (AdminAudit $record): string => $objects->describe((string) $record->object_type, (string) $record->object_id)['label'])
                    ->description(fn (AdminAudit $record): string => $objects->describe((string) $record->object_type, (string) $record->object_id)['note'])
                    ->url(fn (AdminAudit $record): ?string => $objects->describe((string) $record->object_type, (string) $record->object_id)['url'])
                    ->color(fn (AdminAudit $record): string => $objects->describe((string) $record->object_type, (string) $record->object_id)['url'] === null ? 'gray' : 'primary')
                    ->tooltip(fn (AdminAudit $record): string => 'ID: '.$record->object_id.' (нажмите на значок, чтобы скопировать)')
                    ->icon('heroicon-m-clipboard-document')
                    ->iconPosition('after')
                    ->copyable()
                    ->copyableState(fn (AdminAudit $record): string => (string) $record->object_id)
                    ->copyMessage('ID скопирован')
                    ->wrap(),
                TextColumn::make('outcome')
                    ->label('Результат')
                    ->badge()
                    ->formatStateUsing(fn (string $state): string => AuditDictionary::outcome($state))
                    ->color(fn (string $state): string => AuditDictionary::outcomeColor($state)),
            ])
            ->defaultSort(fn (Builder $query): Builder => $query->orderByDesc('created_at')->orderByDesc('event_id'))
            ->stackedOnMobile()
            ->emptyStateHeading('Записей нет')
            ->emptyStateDescription('Здесь появятся действия администраторов: заявки, назначения, отключения.');
    }

    /**
     * @return array<string, string>
     */
    /**
     * r2: вторая строка «Кто» — имя, только если оно отличается от логина. Раньше при одинаковых
     * имени и логине (design.admin) колонка показывала одно и то же дважды.
     */
    public static function actorNote(?string $username, ?string $displayName): ?string
    {
        $name = trim((string) $displayName);
        if ($name === '') {
            return null;
        }

        return mb_strtolower($name) === mb_strtolower(trim((string) $username)) ? null : $name;
    }

    private static function actorOptions(): array
    {
        return DB::table(Zapara::admin().'.admin_audit as a')
            ->join(Zapara::accounts().'.users as u', 'u.user_id', '=', 'a.actor_id')
            ->distinct()
            ->orderBy('u.username')
            ->pluck('u.username', 'u.user_id')
            ->all();
    }
}
