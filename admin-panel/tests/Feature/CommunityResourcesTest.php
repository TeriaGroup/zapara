<?php

namespace Tests\Feature;

use App\Filament\Resources\Communities\CommunityResource;
use App\Filament\Resources\Communities\Pages\ListCommunities;
use App\Filament\Resources\Communities\Pages\ViewCommunity;
use App\Filament\Resources\Communities\RelationManagers\GroupsRelationManager;
use App\Filament\Resources\Communities\RelationManagers\StaffRelationManager;
use App\Filament\Resources\StaffAssignments\Pages\ManageStaffAssignments;
use App\Models\Community;
use App\Support\Zapara;
use Filament\Actions\Testing\TestAction;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

class CommunityResourcesTest extends TestCase
{
    use PanelFixtures;

    public function test_create_community_opens_a_modal_with_only_name_and_description(): void
    {
        $this->actingAs($this->makeUser(true));
        $name = 'Группа '.$this->token('c');

        $page = Livewire::test(ListCommunities::class)
            ->mountAction('createCommunity')
            ->assertMountedActionModalSee('Новое сообщество')
            ->assertMountedActionModalSee('Название')
            ->assertMountedActionModalSee('Описание')
            ->assertMountedActionModalDontSee('Код группы');
        $page->setActionData(['name' => $name, 'description' => 'Описание'])
            ->callMountedAction()
            ->assertHasNoActionErrors();
        $id = (string) DB::table(Zapara::communities().'.communities')->where('name', $name)->value('community_id');
        $this->assertNotSame('', $id);
        $page->assertRedirect(CommunityResource::getUrl('view', ['record' => $id]));

        Livewire::test(ListCommunities::class)
            ->callAction('createCommunity', ['name' => '', 'description' => ''])
            ->assertHasActionErrors(['name' => 'required']);
    }

    public function test_group_mapping_lives_inside_the_community_and_reports_duplicates_at_the_field(): void
    {
        $this->actingAs($this->makeUser(true));
        $first = Community::query()->findOrFail($this->makeCommunity('Группа '.$this->token('c')));
        $second = Community::query()->findOrFail($this->makeCommunity('Группа '.$this->token('c')));
        $groupId = 'g'.$this->token('g');

        Livewire::test(GroupsRelationManager::class, ['ownerRecord' => $first, 'pageClass' => ViewCommunity::class])
            ->callAction(TestAction::make('mapCatalog')->table(), ['group_id' => $groupId, 'group_name' => 'Каталог'])
            ->assertHasNoActionErrors()
            ->assertSee($groupId);
        Livewire::test(GroupsRelationManager::class, ['ownerRecord' => $second, 'pageClass' => ViewCommunity::class])
            ->callAction(TestAction::make('mapCatalog')->table(), ['group_id' => $groupId, 'group_name' => 'Каталог'])
            ->assertHasActionErrors(['group_id']);
        $this->assertSame($first->community_id, DB::table(Zapara::communities().'.catalog_maps')->where('group_id', $groupId)->value('community_id'));
        DB::table(Zapara::communities().'.catalog_maps')->where('group_id', $groupId)->delete();
    }

    public function test_staff_assignment_has_no_default_role_and_explains_the_password(): void
    {
        $this->actingAs($this->makeUser(true));
        $community = Community::query()->findOrFail($this->makeCommunity('Группа '.$this->token('c')));
        $owner = ['ownerRecord' => $community, 'pageClass' => ViewCommunity::class];
        $staff = $this->makeUser(false, 'Иван Иванов');

        Livewire::test(StaffRelationManager::class, $owner)
            ->assertSee('Персонал не назначен')
            ->mountAction(TestAction::make('assignStaff')->table())
            ->assertActionDataSet(['role' => null, 'user_id' => null])
            ->assertMountedActionModalSee('Выберите роль')
            ->assertMountedActionModalSee('Введите свой пароль для подтверждения.');

        Livewire::test(StaffRelationManager::class, $owner)
            ->callAction(TestAction::make('assignStaff')->table(), ['user_id' => $staff->user_id, 'current_password' => self::PANEL_PASSWORD])
            ->assertHasActionErrors(['role' => 'required']);
        $this->assertSame(0, DB::table(Zapara::communities().'.staff_assignments')->where('user_id', $staff->user_id)->count());

        Livewire::test(StaffRelationManager::class, $owner)
            ->callAction(TestAction::make('assignStaff')->table(), ['user_id' => $staff->user_id, 'role' => 'curator', 'current_password' => self::PANEL_PASSWORD])
            ->assertHasNoActionErrors()
            ->assertSee($staff->username)
            ->assertSee('Иван Иванов')
            ->assertSee('Куратор');

        Livewire::test(ManageStaffAssignments::class)
            ->searchTable($staff->username)
            ->assertSee($staff->username)
            ->assertSee((string) $community->name)
            ->assertSee('Куратор');
        DB::table(Zapara::communities().'.staff_assignments')->where('user_id', $staff->user_id)->delete();
    }

    public function test_staff_page_assigns_with_a_community_choice(): void
    {
        $this->actingAs($this->makeUser(true));
        $communityId = $this->makeCommunity('Группа '.$this->token('c'));
        $staff = $this->makeUser(false);

        Livewire::test(ManageStaffAssignments::class)
            ->callAction('assignStaff', ['community_id' => $communityId, 'user_id' => $staff->user_id, 'role' => 'headman', 'current_password' => 'wrong-password'])
            ->assertHasActionErrors(['current_password']);
        Livewire::test(ManageStaffAssignments::class)
            ->callAction('assignStaff', ['community_id' => $communityId, 'user_id' => $staff->user_id, 'role' => 'headman', 'current_password' => self::PANEL_PASSWORD])
            ->assertHasNoActionErrors();
        $this->assertSame('headman', DB::table(Zapara::communities().'.staff_assignments')->where('user_id', $staff->user_id)->whereNull('revoked_at')->value('role'));
        DB::table(Zapara::communities().'.staff_assignments')->where('user_id', $staff->user_id)->delete();
    }

    public function test_non_admin_cannot_open_communities_or_staff(): void
    {
        $this->actingAs($this->makeUser(false));
        $this->get('/admin/communities')->assertForbidden();
        $this->get('/admin/staff')->assertForbidden();
    }

    public function test_non_uuid_community_id_is_not_found(): void
    {
        $this->actingAs($this->makeUser(true));
        foreach (['not-a-uuid', '123', "1'--", '00000000-0000-0000-0000-00000000000g'] as $id) {
            $this->get('/admin/communities/'.rawurlencode($id))->assertNotFound();
        }
        $this->get('/admin/communities/'.Str::uuid()->toString())->assertNotFound();
    }

    public function test_relation_managers_follow_the_resource_access_rule(): void
    {
        $community = new Community;
        $this->actingAs($this->makeUser(false));
        $this->assertFalse(GroupsRelationManager::canViewForRecord($community, ViewCommunity::class));
        $this->assertFalse(StaffRelationManager::canViewForRecord($community, ViewCommunity::class));
        $this->actingAs($this->makeUser(true));
        $this->assertTrue(GroupsRelationManager::canViewForRecord($community, ViewCommunity::class));
        $this->assertTrue(StaffRelationManager::canViewForRecord($community, ViewCommunity::class));
    }

    /** Ревью: колонки времени «Привязана» и «Назначен» помечены МСК, как «Когда (МСК)» в журнале. */
    public function test_assignment_and_link_time_columns_are_marked_moscow(): void
    {
        $this->actingAs($this->makeUser(true));
        $community = Community::query()->findOrFail($this->makeCommunity('Группа '.$this->token('c')));
        $owner = ['ownerRecord' => $community, 'pageClass' => ViewCommunity::class];
        $label = fn (string $expected) => fn ($column): bool => $column->getLabel() === $expected;

        Livewire::test(GroupsRelationManager::class, $owner)
            ->assertTableColumnExists('created_at', $label('Привязана (МСК)'));
        Livewire::test(StaffRelationManager::class, $owner)
            ->assertTableColumnExists('assigned_at', $label('Назначен (МСК)'));
        Livewire::test(ManageStaffAssignments::class)
            ->assertTableColumnExists('assigned_at', $label('Назначен (МСК)'));
    }
}

