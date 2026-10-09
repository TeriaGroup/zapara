<?php

namespace App\Filament\Navigation;

use App\Support\Zapara;
use Filament\Navigation\NavigationBuilder;
use Filament\Navigation\NavigationGroup;
use Filament\Navigation\NavigationItem;
use Filament\Pages\Dashboard;
use Filament\Panel;
use Illuminate\Support\Facades\DB;

/**
 * Навигация панели: три группы, иконки и счётчики очередей.
 * Пункты ищутся по slug страницы или ресурса, поэтому настройка не зависит от того,
 * страница это или ресурс Filament (например, «Сообщества» и «Персонал» после #24).
 */
final class AdminNavigation
{
    /**
     * slug => [группа, иконка, подпись в меню или null]
     *
     * @var array<string, array{0: string, 1: string, 2: ?string}>
     */
    public const ITEMS = [
        'memberships' => ['Модерация', 'heroicon-o-user-plus', null],
        'support' => ['Модерация', 'heroicon-o-lifebuoy', null],
        'communities' => ['Модерация', 'heroicon-o-user-group', null],
        'staff' => ['Модерация', 'heroicon-o-identification', null],
        'users' => ['Пользователи', 'heroicon-o-users', null],
        'sessions' => ['Пользователи', 'heroicon-o-device-phone-mobile', 'Сеансы входа'],
        'status' => ['Система', 'heroicon-o-signal', null],
        'settings' => ['Система', 'heroicon-o-cog-6-tooth', null],
        'quotas' => ['Система', 'heroicon-o-circle-stack', 'Квоты файлов'],
        'content' => ['Система', 'heroicon-o-document-text', 'Материалы групп'],
        'audit' => ['Система', 'heroicon-o-clipboard-document-list', null],
    ];

    public const GROUPS = ['Модерация', 'Пользователи', 'Система'];

    public static function build(NavigationBuilder $builder, Panel $panel): NavigationBuilder
    {
        $grouped = array_fill_keys(self::GROUPS, []);
        $loose = [];
        $order = array_flip(array_keys(self::ITEMS));
        foreach ([...$panel->getPages(), ...$panel->getResources()] as $class) {
            if (! $class::shouldRegisterNavigation() || ! $class::canAccess()) {
                continue;
            }
            $slug = $class === Dashboard::class || is_subclass_of($class, Dashboard::class) ? '' : (string) $class::getSlug($panel);
            foreach ($class::getNavigationItems() as $item) {
                $config = self::ITEMS[$slug] ?? null;
                if ($config === null) {
                    $loose[] = $item;

                    continue;
                }
                [$group, $icon, $label] = $config;
                $item->group($group)->icon($icon)->sort($order[$slug]);
                if ($label !== null) {
                    $item->label($label);
                }
                self::badge($slug, $item);
                $grouped[$group][] = $item;
            }
        }
        usort($loose, fn (NavigationItem $a, NavigationItem $b): int => $a->getSort() <=> $b->getSort());
        $builder->items($loose);
        foreach ($grouped as $group => $items) {
            usort($items, fn (NavigationItem $a, NavigationItem $b): int => $a->getSort() <=> $b->getSort());
            $builder->group(NavigationGroup::make($group)->items($items));
        }

        return $builder;
    }

    /**
     * Счётчики очередей. Если страница уже задала свой бейдж, он не перезаписывается.
     */
    private static function badge(string $slug, NavigationItem $item): void
    {
        if ($item->getBadge() !== null) {
            return;
        }
        $count = match ($slug) {
            'memberships' => self::pendingJoins(),
            'support' => self::waitingSupport(),
            default => 0,
        };
        if ($count > 0) {
            $item->badge((string) $count, 'warning')
                ->badgeTooltip($slug === 'support' ? 'Ждут ответа' : 'Ожидают решения');
        }
    }

    private static function pendingJoins(): int
    {
        try {
            return DB::table(Zapara::communities().'.join_requests')->where('status', 'pending')->count();
        } catch (\Throwable) {
            return 0;
        }
    }

    private static function waitingSupport(): int
    {
        $schema = Zapara::settings();
        try {
            $exists = DB::selectOne('select to_regclass(?)::text as name', [$schema.'.support_messages']);
            if (! is_object($exists) || ! is_string($exists->name) || $exists->name === '') {
                return 0;
            }
            $row = DB::selectOne(
                "select count(*) as waiting from {$schema}.support_threads t
                 where (select m.author from {$schema}.support_messages m where m.thread_id = t.thread_id order by m.created_at desc limit 1) = 'user'"
            );

            return (int) ($row->waiting ?? 0);
        } catch (\Throwable) {
            return 0;
        }
    }
}
