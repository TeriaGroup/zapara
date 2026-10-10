<?php

namespace Tests\Feature;

use App\Filament\Pages\Memberships;
use App\Filament\Resources\AccountUsers\Pages\ListAccountUsers;
use Filament\Actions\Testing\TestAction;
use Illuminate\Support\Carbon;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

class DestructiveActionsTest extends TestCase
{
    use PanelFixtures;

    public function test_join_request_card_names_user_group_and_date_without_raw_ids(): void
    {
        $admin = $this->makeUser(true);
        $member = $this->makeUser(false, 'Мария Петрова');
        $group = 'И'.$this->token('g');
        $communityId = $this->makeCommunity($group);
        $requestId = $this->makeJoinRequest($communityId, $member, Carbon::parse('2026-10-09 15:40:00', 'UTC'));
        $this->actingAs($admin);

        $page = Livewire::test(Memberships::class)
            ->assertSee('Мария Петрова ('.$member->username.') → '.$group)
            ->assertSee('Заявка 9 октября, 18:40 МСК')
            ->assertSee('Скопировать ID');
        $this->assertStringNotContainsString($communityId, strip_tags((string) $page->html()));
        $this->assertStringNotContainsString('>'.$requestId.'<', (string) $page->html());
    }

    public function test_reject_is_danger_and_asks_for_confirmation_naming_user_and_group(): void
    {
        $admin = $this->makeUser(true);
        $member = $this->makeUser(false, 'Пётр Сидоров');
        $group = 'И'.$this->token('g');
        $communityId = $this->makeCommunity($group);
        $requestId = $this->makeJoinRequest($communityId, $member);
        $key = str_replace('-', '', $requestId);
        $this->actingAs($admin);
        $reject = TestAction::make('reject'.$key)->schemaComponent('join-'.$key, 'content');

        Livewire::test(Memberships::class)
            ->assertActionHasColor($reject, 'danger')
            ->assertActionHasColor(TestAction::make('accept'.$key)->schemaComponent('join-'.$key, 'content'), 'primary')
            ->mountAction($reject)
            ->assertMountedActionModalSee('Отклонить заявку?')
            ->assertMountedActionModalSee('Пётр Сидоров ('.$member->username.')')
            ->assertMountedActionModalSee('«'.$group.'»');
    }

    public function test_pending_requests_badge_counts_waiting_requests(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        $before = (int) (Memberships::getNavigationBadge() ?? 0);
        $communityId = $this->makeCommunity('И'.$this->token('g'));
        $this->makeJoinRequest($communityId, $this->makeUser(false));
        $this->makeJoinRequest($communityId, $this->makeUser(false));

        $this->assertSame((string) ($before + 2), Memberships::getNavigationBadge());
        $this->assertSame('warning', Memberships::getNavigationBadgeColor());
    }

    public function test_disable_and_remove_are_danger_actions_with_confirmation_naming_the_user(): void
    {
        $admin = $this->makeUser(true);
        $target = $this->makeUser(false, 'Анна Кузнецова');
        $this->actingAs($admin);

        foreach (['disable' => 'Отключить пользователя?', 'remove' => 'Удалить пользователя?'] as $name => $heading) {
            Livewire::test(ListAccountUsers::class)
                ->searchTable($target->username)
                ->assertActionHasColor(TestAction::make($name)->table($target), 'danger')
                ->mountAction(TestAction::make($name)->table($target))
                ->assertMountedActionModalSee($heading)
                ->assertMountedActionModalSee('Анна Кузнецова ('.$target->username.')')
                ->assertMountedActionModalSee('Введите свой пароль для подтверждения.');
        }
        $this->assertSame('active', $target->refresh()->status);

        Livewire::test(ListAccountUsers::class)
            ->searchTable($target->username)
            ->assertActionDoesNotHaveColor(TestAction::make('edit')->table($target), 'danger');
    }
}
