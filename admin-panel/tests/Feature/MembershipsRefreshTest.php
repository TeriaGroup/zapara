<?php

namespace Tests\Feature;

use App\Filament\Pages\Memberships;
use App\Support\Zapara;
use Filament\Actions\Testing\TestAction;
use Illuminate\Support\Facades\DB;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

class MembershipsRefreshTest extends TestCase
{
    use PanelFixtures;

    public function test_accepted_request_leaves_the_list_without_reload(): void
    {
        $admin = $this->makeUser(true);
        $accepted = $this->makeUser(false);
        $waiting = $this->makeUser(false);
        $communityId = $this->makeCommunity('И'.$this->token('g'));
        $requestId = $this->makeJoinRequest($communityId, $accepted);
        $this->makeJoinRequest($communityId, $waiting);
        $key = str_replace('-', '', $requestId);
        $this->actingAs($admin);

        Livewire::test(Memberships::class)
            ->assertSee($accepted->username)
            ->callAction(TestAction::make('accept'.$key)->schemaComponent('join-'.$key, 'content'))
            ->assertNotified('Заявка принята')
            ->assertDontSee($accepted->username)
            ->assertSee($waiting->username);
        $this->assertSame('accepted', DB::table(Zapara::communities().'.join_requests')->where('request_id', $requestId)->value('status'));
    }

    public function test_rejected_request_leaves_the_list_and_last_one_shows_empty_state(): void
    {
        $admin = $this->makeUser(true);
        $member = $this->makeUser(false);
        $communityId = $this->makeCommunity('И'.$this->token('g'));
        $requestId = $this->makeJoinRequest($communityId, $member);
        $key = str_replace('-', '', $requestId);
        $this->actingAs($admin);

        $page = Livewire::test(Memberships::class)
            ->assertSee($member->username)
            ->callAction(TestAction::make('reject'.$key)->schemaComponent('join-'.$key, 'content'))
            ->assertNotified('Заявка отклонена')
            ->assertDontSee($member->username);
        if (DB::table(Zapara::communities().'.join_requests')->where('status', 'pending')->doesntExist()) {
            $page->assertSee('Нет заявок');
        }
        $this->assertSame('rejected', DB::table(Zapara::communities().'.join_requests')->where('request_id', $requestId)->value('status'));
    }
}
