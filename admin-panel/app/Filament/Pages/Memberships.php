<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Filament\Support\MoscowTime;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\Section;
use Filament\Schemas\Components\Text;
use Filament\Schemas\Schema;
use Illuminate\Support\Js;
use Illuminate\Validation\ValidationException;

class Memberships extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Заявки';

    protected static ?string $title = 'Заявки';

    protected static ?string $slug = 'memberships';

    protected static ?int $navigationSort = 50;

    public static function getNavigationBadge(): ?string
    {
        if (! static::canAccess()) {
            return null;
        }
        $count = app(OperatorWork::class)->pendingJoinCount();

        return $count > 0 ? (string) $count : null;
    }

    public static function getNavigationBadgeColor(): string|array|null
    {
        return 'warning';
    }

    public static function getNavigationBadgeTooltip(): ?string
    {
        return 'Ожидают решения';
    }

    public function content(Schema $schema): Schema
    {
        $joins = app(OperatorWork::class)->pendingJoins();
        if ($joins === []) {
            return $schema->components([
                // R2-19: пустое состояние — карточкой с объяснением, как у «Поддержки», а не голым «Нет заявок».
                Section::make('Заявок нет')
                    ->description('Они появляются, когда студент просит вступить в группу.'),
            ]);
        }

        $components = [];
        foreach ($joins as $row) {
            $requestId = $row['request_id'];
            $key = str_replace('-', '', $requestId);
            $who = self::who($row);
            $group = $row['community_name'];
            $components[] = Section::make($who.' → '.$group)
                ->description('Заявка '.MoscowTime::dateTime($row['created_at']))
                ->key('join-'.$key)
                ->headerActions([
                    Action::make('copy'.$key)
                        ->label('Скопировать ID')
                        ->icon('heroicon-o-clipboard-document')
                        ->iconButton()
                        ->tooltip('Скопировать ID заявки')
                        ->color('gray')
                        ->alpineClickHandler('window.navigator.clipboard.writeText('.Js::from($requestId).'); $tooltip('.Js::from('Скопировано').', { theme: $store.theme, timeout: 2000 })'),
                    Action::make('reject'.$key)
                        ->label('Отклонить')
                        ->color('danger')
                        ->outlined()
                        ->requiresConfirmation()
                        ->modalHeading('Отклонить заявку?')
                        ->modalDescription('Заявка '.$who.' на вступление в «'.$group.'» будет отклонена.')
                        ->modalSubmitActionLabel('Отклонить')
                        ->action(fn () => $this->rejectJoin($requestId)),
                    Action::make('accept'.$key)
                        ->label('Принять')
                        ->color('primary')
                        ->action(fn () => $this->acceptJoin($requestId)),
                ]);
        }

        return $schema->components($components);
    }

    public function acceptJoin(string $requestId): void
    {
        $this->resolve($requestId, true);
        Notification::make()->title('Заявка принята')->success()->send();
    }

    public function rejectJoin(string $requestId): void
    {
        $this->resolve($requestId, false);
        Notification::make()->title('Заявка отклонена')->success()->send();
    }

    /**
     * @param  array{username: string, display_name: string}  $row
     */
    private static function who(array $row): string
    {
        return $row['display_name'] !== '' && $row['display_name'] !== $row['username']
            ? $row['display_name'].' ('.$row['username'].')'
            : $row['username'];
    }

    private function resolve(string $requestId, bool $accepted): void
    {
        $row = null;
        foreach (app(OperatorWork::class)->pendingJoins() as $candidate) {
            if ($candidate['request_id'] === $requestId) {
                $row = $candidate;
                break;
            }
        }
        if ($row === null) {
            throw ValidationException::withMessages(['data.request_id' => 'Объект не найден.']);
        }
        app(OperatorWork::class)->resolveJoin($this->actor(), $row['community_id'], $requestId, $accepted);
        $this->refreshList();
    }

    private function refreshList(): void
    {
        // Список собран до действия и закэширован на время запроса; без сброса карточка остаётся до перезагрузки.
        $this->cacheSchema('content', null);
    }
}
