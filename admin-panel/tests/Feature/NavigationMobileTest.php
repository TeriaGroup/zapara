<?php

namespace Tests\Feature;

use App\Filament\Navigation\AdminNavigation;
use App\Filament\Resources\AccountUsers\Pages\ListAccountUsers;
use App\Support\Zapara;
use Filament\Facades\Filament;
use Filament\Navigation\NavigationBuilder;
use Filament\Navigation\NavigationGroup;
use Filament\Navigation\NavigationItem;
use Illuminate\Support\Facades\DB;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

class NavigationMobileTest extends TestCase
{
    use PanelFixtures;

    public function test_navigation_has_three_groups_and_every_item_has_an_icon(): void
    {
        $this->actingAs($this->makeUser(true));
        $this->get('/admin')->assertOk();

        $navigation = Filament::getPanel('admin')->getNavigation();
        $labels = array_map(fn (NavigationGroup $group): ?string => $group->getLabel(), $navigation);
        $this->assertSame([null, 'Модерация', 'Пользователи', 'Система'], array_values($labels));

        $byGroup = [];
        foreach ($navigation as $group) {
            foreach ($group->getItems() as $item) {
                /** @var NavigationItem $item */
                $this->assertNotNull($item->getIcon(), $item->getLabel().' без иконки');
                $byGroup[(string) $group->getLabel()][] = $item->getLabel();
            }
        }
        $this->assertSame(['Инфопанель'], $byGroup['']);
        $this->assertSame(['Заявки', 'Поддержка', 'Сообщества', 'Персонал'], $byGroup['Модерация']);
        $this->assertSame(['Пользователи', 'Сеансы входа'], $byGroup['Пользователи']);
        $this->assertSame(['Состояние', 'Настройки системы', 'Квоты файлов', 'Материалы групп', 'Журнал'], $byGroup['Система']);
        $this->assertCount(count(AdminNavigation::ITEMS), array_merge($byGroup['Модерация'], $byGroup['Пользователи'], $byGroup['Система']));
    }

    public function test_queue_items_show_counters(): void
    {
        $this->actingAs($this->makeUser(true));
        $communityId = $this->makeCommunity('Группа '.$this->token('c'));
        $this->makeJoinRequest($communityId, $this->makeUser(false));
        $pending = DB::table(Zapara::communities().'.join_requests')->where('status', 'pending')->count();
        $this->get('/admin')->assertOk();

        $items = collect(Filament::getPanel('admin')->getNavigation())->flatMap(fn (NavigationGroup $group): array => $group->getItems());
        $joins = $items->first(fn (NavigationItem $item): bool => $item->getLabel() === 'Заявки');
        $this->assertSame((string) $pending, (string) $joins->getBadge());
    }

    public function test_non_admin_gets_no_navigation_items(): void
    {
        $this->actingAs($this->makeUser(false));
        $groups = AdminNavigation::build(new NavigationBuilder, Filament::getPanel('admin'))->getNavigation();
        $items = collect($groups)->flatMap(fn (NavigationGroup $group): array => $group->getItems())->map(fn (NavigationItem $item): string => $item->getLabel());
        $this->assertNotContains('Пользователи', $items->all());
        $this->assertNotContains('Журнал', $items->all());
    }

    public function test_mobile_theme_is_loaded_and_tables_stack_on_phones(): void
    {
        $this->actingAs($this->makeUser(true));
        $this->get('/admin')
            ->assertOk()
            ->assertSee('min-height: 44px', false)
            ->assertSee('.fi-section-header', false);

        $table = Livewire::test(ListAccountUsers::class)->instance()->getTable();
        $this->assertTrue($table->isStackedOnMobile());
    }
}
