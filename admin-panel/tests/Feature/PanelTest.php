<?php

namespace Tests\Feature;

use App\Auth\IdentityPassword;
use App\Filament\Pages\AuditLog;
use App\Filament\Pages\Communities;
use App\Filament\Pages\Content;
use App\Filament\Pages\Login;
use App\Filament\Pages\Memberships;
use App\Filament\Pages\PlatformStatus;
use App\Filament\Pages\Quotas;
use App\Filament\Pages\Sessions;
use App\Filament\Pages\Staff;
use App\Filament\Pages\SupportDesk;
use App\Filament\Pages\SystemSettings;
use App\Filament\Resources\AccountUsers\Pages\CreateAccountUser;
use App\Filament\Resources\AccountUsers\Pages\EditAccountUser;
use App\Filament\Resources\AccountUsers\Pages\ListAccountUsers;
use App\Models\AccountUser;
use App\Services\OperatorSettings;
use App\Support\Zapara;
use Filament\Actions\Testing\TestAction;
use Filament\Facades\Filament;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Livewire\Livewire;
use Symfony\Component\Process\Process;
use Tests\TestCase;

class PanelTest extends TestCase
{
    private const PASSWORD = 'Panel-password-1';

    /** @var list<string> */
    private array $users = [];

    public static function setUpBeforeClass(): void
    {
        parent::setUpBeforeClass();
        $result = self::dotnet('Prepare_panel_schemas');
        if ($result['code'] !== 0) {
            throw new \RuntimeException(self::redact($result['output']));
        }
    }

    protected function setUp(): void
    {
        parent::setUp();
        if (Zapara::dsn() === null) {
            $this->fail('ZAPARA_TEST_POSTGRES is required.');
        }
        Filament::setCurrentPanel(Filament::getPanel('admin'));
        config(['app.env' => 'local']);
    }

    protected function tearDown(): void
    {
        foreach ($this->users as $id) {
            $this->deleteUser($id);
        }
        $relation = DB::selectOne('select to_regclass(?)::text as name', [Zapara::settings().'.system_settings']);
        if (is_object($relation) && is_string($relation->name) && $relation->name !== '') {
            DB::table(Zapara::settings().'.system_settings')->where('key', 'registration_enabled')->delete();
        }
        parent::tearDown();
    }

    public function test_two_panel_launches_agree(): void
    {
        $first = $this->launchTranscript();
        $this->refreshApplication();
        config(['app.env' => 'local']);
        Filament::setCurrentPanel(Filament::getPanel('admin'));
        $second = $this->launchTranscript();
        $this->assertSame($first, $second);
        $this->assertStringContainsString('Имя пользователя', $first);
        $this->assertStringContainsString('Войти', $first);
        $this->assertStringContainsString('filament', strtolower($first));
        $this->assertStringContainsString('Настройки системы', $first);
        $this->assertStringContainsString('Состояние', $first);
        $this->assertStringContainsString('Квоты', $first);
        $this->assertStringContainsString('Поддержка', $first);
        $this->assertStringContainsString('Пользователи', $first);
        $dir = getenv('ZAPARA_LAUNCH_DIR');
        if (is_string($dir) && $dir !== '') {
            file_put_contents($dir.DIRECTORY_SEPARATOR.'panel-launch-1.txt', $first);
            file_put_contents($dir.DIRECTORY_SEPARATOR.'panel-launch-2.txt', $second);
        }
    }

    public function test_identity_hash_round_trips(): void
    {
        $hash = IdentityPassword::hash(self::PASSWORD);
        $this->assertTrue(IdentityPassword::verify($hash, self::PASSWORD));
        $this->assertFalse(IdentityPassword::verify($hash, self::PASSWORD.'x'));
    }

    public function test_platform_admin_enters_and_non_admin_is_refused_in_local(): void
    {
        $admin = $this->makeUser(true);
        $member = $this->makeUser(false);

        $this->get('/admin/login')
            ->assertOk()
            ->assertSee('Имя пользователя')
            ->assertSee('Пароль')
            ->assertSee('Войти')
            ->assertSee('filament', false);

        Livewire::test(Login::class)
            ->fillForm(['username' => $member->username, 'password' => self::PASSWORD])
            ->call('authenticate')
            ->assertHasFormErrors(['username']);
        $this->assertGuest();

        $this->actingAs($member);
        $this->get('/admin')->assertForbidden();
        Livewire::test(ListAccountUsers::class)->assertForbidden();
        Livewire::test(SystemSettings::class)->assertForbidden();
        Livewire::test(CreateAccountUser::class)->assertForbidden();
        auth()->logout();

        Livewire::test(Login::class)
            ->fillForm(['username' => $admin->username, 'password' => self::PASSWORD])
            ->call('authenticate')
            ->assertHasNoFormErrors();

        $this->get('/admin')
            ->assertOk()
            ->assertSee('Настройки системы')
            ->assertSee('Пользователи');
    }

    public function test_settings_are_saved_and_read_by_the_panel_and_the_server(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);

        Livewire::test(SystemSettings::class)
            ->fillForm(['registration_enabled' => false])
            ->call('save')
            ->assertHasNoFormErrors();

        Livewire::test(SystemSettings::class)
            ->assertSchemaStateSet(['registration_enabled' => false]);
        $this->assertFalse(OperatorSettings::registrationEnabled());

        $server = self::dotnet('Default_operator_store_matches_register_route');
        $this->assertSame(0, $server['code'], self::redact($server['output']));
        $this->assertStringContainsString('OPERATOR_REGISTRATION=false', $server['marker']);
        $this->assertStringContainsString('REGISTER_STATUS=503', $server['marker']);

        Livewire::test(SystemSettings::class)
            ->fillForm(['registration_enabled' => true])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertTrue(OperatorSettings::registrationEnabled());
    }

    public function test_provider_storage_and_limits_round_trip_without_showing_secrets(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        $vkSecret = 'vk-panel-secret-value';
        $yandexSecret = 'ya-panel-secret-value';
        $s3Secret = 's3-panel-secret-value';
        Livewire::test(SystemSettings::class)
            ->fillForm([
                'registration_enabled' => true,
                'vk_enabled' => true,
                'vk_client_id' => 'vk-app',
                'vk_callback' => 'https://voen.teriahost.ru/auth/vk/callback',
                'vk_secret' => $vkSecret,
                'yandex_enabled' => true,
                'yandex_client_id' => 'ya-app',
                'yandex_callback' => 'https://voen.teriahost.ru/auth/yandex/callback',
                'yandex_secret' => $yandexSecret,
                's3_endpoint' => 'http://127.0.0.1:9',
                's3_region' => 'ru-central1',
                's3_bucket' => 'zapara-bucket',
                's3_access_key' => 'AKIAEXAMPLE',
                's3_secret' => $s3Secret,
                'quota_group_bytes' => '1073741824',
                'quota_user_bytes' => '524288000',
            ])
            ->call('save')
            ->assertHasNoFormErrors();

        $reloaded = Livewire::test(SystemSettings::class);
        $reloaded->assertSchemaStateSet([
            'registration_enabled' => true,
            'vk_enabled' => true,
            'vk_client_id' => 'vk-app',
            'vk_callback' => 'https://voen.teriahost.ru/auth/vk/callback',
            'yandex_enabled' => true,
            'yandex_client_id' => 'ya-app',
            'yandex_callback' => 'https://voen.teriahost.ru/auth/yandex/callback',
            's3_endpoint' => 'http://127.0.0.1:9',
            's3_region' => 'ru-central1',
            's3_bucket' => 'zapara-bucket',
            's3_access_key' => 'AKIAEXAMPLE',
            'quota_group_bytes' => '1073741824',
            'quota_user_bytes' => '524288000',
        ]);
        $html = (string) $reloaded->html();
        $this->assertStringNotContainsString($vkSecret, $html);
        $this->assertStringNotContainsString($yandexSecret, $html);
        $this->assertStringNotContainsString($s3Secret, $html);
        $page = (string) $this->get('/admin/settings')->assertOk()->getContent();
        $this->assertStringNotContainsString($vkSecret, $page);
        $this->assertStringNotContainsString($yandexSecret, $page);
        $this->assertStringNotContainsString($s3Secret, $page);
        $this->assertSame('configured', OperatorSettings::publicValue('vk_secret'));
        $this->assertSame('configured', OperatorSettings::publicValue('yandex_secret'));
        $this->assertSame('configured', OperatorSettings::publicValue('s3_secret'));
        $this->assertSame($vkSecret, OperatorSettings::read('vk_secret'));

        Livewire::test(SystemSettings::class)
            ->fillForm([
                'registration_enabled' => true,
                'vk_enabled' => false,
                'vk_client_id' => 'vk-app-2',
                'vk_callback' => 'https://voen.teriahost.ru/auth/vk/callback',
                'vk_secret' => '',
                'yandex_enabled' => true,
                'yandex_client_id' => 'ya-app',
                'yandex_callback' => 'https://voen.teriahost.ru/auth/yandex/callback',
                'yandex_secret' => '',
                's3_endpoint' => 'http://127.0.0.1:9',
                's3_region' => 'ru-central1',
                's3_bucket' => 'zapara-bucket',
                's3_access_key' => 'AKIAEXAMPLE',
                's3_secret' => '',
                'quota_group_bytes' => '1000',
                'quota_user_bytes' => '500',
            ])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertSame($vkSecret, OperatorSettings::read('vk_secret'));
        $this->assertSame($yandexSecret, OperatorSettings::read('yandex_secret'));
        $this->assertSame($s3Secret, OperatorSettings::read('s3_secret'));
        $this->assertSame('vk-app-2', OperatorSettings::read('vk_client_id'));
        $this->assertSame('false', OperatorSettings::read('vk_enabled'));
        $this->assertSame('1000', OperatorSettings::read('quota_group_bytes'));
        $this->assertSame('500', OperatorSettings::read('quota_user_bytes'));

        $server = self::dotnet('Panel_settings_drive_capabilities');
        $this->assertSame(0, $server['code'], self::redact($server['output']));
        $this->assertStringContainsString('CAPABILITIES_VK=false', $server['marker']);
        $this->assertStringContainsString('CAPABILITIES_YANDEX=true', $server['marker']);
        $this->assertStringContainsString('STORAGE=s3', $server['marker']);
        $this->assertStringContainsString('CAPABILITIES_VK_AFTER=true', $server['marker']);
        $this->assertStringContainsString('STORAGE_AFTER=local', $server['marker']);
        $this->assertSame($vkSecret, OperatorSettings::read('vk_secret'));

        $status = Livewire::test(PlatformStatus::class);
        $status->assertOk();
        $statusHtml = (string) $status->html();
        $this->assertStringContainsString('Парсер расписания', $statusHtml);
        $this->assertStringContainsString('Снимок расписания', $statusHtml);
        $this->assertStringContainsString('S3', $statusHtml);
        $this->assertStringContainsString('Не работает', $statusHtml);
        Livewire::test(Quotas::class)->assertOk();
        Livewire::test(SupportDesk::class)->assertOk();
    }

    public function test_user_management_writes_the_account_system_and_rejects_invalid_input(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        $before = DB::table(Zapara::accounts().'.users')->count();
        $username = $this->token('u');

        Livewire::test(CreateAccountUser::class)
            ->fillForm(['username' => 'x', 'display_name' => 'Имя', 'password' => 'short'])
            ->call('create')
            ->assertHasFormErrors(['username', 'password']);
        $this->assertSame($before, DB::table(Zapara::accounts().'.users')->count());

        Livewire::test(CreateAccountUser::class)
            ->fillForm(['username' => $username, 'display_name' => 'Новый человек', 'password' => self::PASSWORD])
            ->call('create')
            ->assertHasNoFormErrors();
        $created = AccountUser::query()->where('username', $username)->firstOrFail();
        $this->users[] = $created->user_id;
        $this->assertSame('active', $created->status);
        $this->assertNotSame($admin->user_id, $created->user_id);
        $hash = DB::table(Zapara::accounts().'.password_credentials')->where('user_id', $created->user_id)->value('password_hash');
        $this->assertTrue(IdentityPassword::verify((string) $hash, self::PASSWORD));

        $active = self::dotnet('Panel_user_authentication_follows_account_status', [
            'ZAPARA_PANEL_USERNAME' => $username,
            'ZAPARA_PANEL_PASSWORD' => self::PASSWORD,
            'ZAPARA_PANEL_EXPECT' => 'active',
        ]);
        $this->assertSame(0, $active['code'], self::redact($active['output']));
        $this->assertStringContainsString('PANEL_LOGIN=ok', $active['marker']);

        Livewire::test(ListAccountUsers::class)
            ->searchTable($username)
            ->assertCanSeeTableRecords([$created]);

        Livewire::test(EditAccountUser::class, ['record' => $created->user_id])
            ->fillForm(['username' => $username, 'display_name' => 'Изменённое имя'])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertSame('Изменённое имя', $created->refresh()->display_name);

        Livewire::test(ListAccountUsers::class)
            ->callAction(TestAction::make('disable')->table($created), ['current_password' => ''])
            ->assertHasErrors(['data.current_password']);
        $this->assertSame('active', $created->refresh()->status);

        Livewire::test(ListAccountUsers::class)
            ->callAction(TestAction::make('disable')->table($created), ['current_password' => 'wrong-password'])
            ->assertHasErrors(['data.current_password']);
        $this->assertSame('active', $created->refresh()->status);

        Livewire::test(ListAccountUsers::class)
            ->callAction(TestAction::make('disable')->table($created), ['current_password' => self::PASSWORD])
            ->assertHasNoFormErrors();
        $this->assertSame('disabled', $created->refresh()->status);

        $blocked = self::dotnet('Panel_user_authentication_follows_account_status', [
            'ZAPARA_PANEL_USERNAME' => $username,
            'ZAPARA_PANEL_PASSWORD' => self::PASSWORD,
            'ZAPARA_PANEL_EXPECT' => 'blocked',
        ]);
        $this->assertSame(0, $blocked['code'], self::redact($blocked['output']));
        $this->assertStringContainsString('PANEL_LOGIN=blocked', $blocked['marker']);

        $removedName = $this->token('r');
        Livewire::test(CreateAccountUser::class)
            ->fillForm(['username' => $removedName, 'display_name' => 'К удалению', 'password' => self::PASSWORD])
            ->call('create')
            ->assertHasNoFormErrors();
        $removed = AccountUser::query()->where('username', $removedName)->firstOrFail();
        $this->users[] = $removed->user_id;
        Livewire::test(ListAccountUsers::class)
            ->callAction(TestAction::make('remove')->table($removed), ['current_password' => self::PASSWORD])
            ->assertHasNoFormErrors();
        $this->assertSame('deleting', $removed->refresh()->status);
        $removedLogin = self::dotnet('Panel_user_authentication_follows_account_status', [
            'ZAPARA_PANEL_USERNAME' => $removedName,
            'ZAPARA_PANEL_PASSWORD' => self::PASSWORD,
            'ZAPARA_PANEL_EXPECT' => 'blocked',
        ]);
        $this->assertSame(0, $removedLogin['code'], self::redact($removedLogin['output']));
        $this->assertStringContainsString('PANEL_LOGIN=blocked', $removedLogin['marker']);

        $this->actingAs($this->makeUser(false));
        Livewire::test(CreateAccountUser::class)->assertForbidden();
    }

    public function test_panel_performs_admin_jobs_and_refuses_a_missing_or_wrong_password(): void
    {
        $admin = $this->makeUser(true);
        $staff = $this->makeUser(false);
        $member = $this->makeUser(false);
        $this->actingAs($admin);
        $name = 'Группа '.$this->token('c');

        $created = Livewire::test(Communities::class)
            ->fillForm(['name' => $name, 'description' => 'Учебная группа'])
            ->call('createCommunity')
            ->assertHasNoFormErrors();
        $communityId = (string) DB::table(Zapara::communities().'.communities')->where('name', $name)->value('community_id');
        $this->assertNotSame('', $communityId);
        $created->assertSee($communityId)->assertSee($name);

        $groupId = 'g'.$this->token('g');
        Livewire::test(Communities::class)
            ->assertSee($communityId)
            ->assertSee($name)
            ->fillForm([
                'community_id' => $communityId,
                'group_id' => $groupId,
                'group_name' => 'Группа каталога',
            ])
            ->call('mapCatalog')
            ->assertHasNoFormErrors();
        $this->assertSame(1, DB::table(Zapara::communities().'.catalog_maps')->where('group_id', $groupId)->count());
        Livewire::test(Communities::class)->assertSee($groupId);

        Livewire::test(Staff::class)
            ->assertSee($communityId)
            ->assertSee($name)
            ->assertSee($staff->username)
            ->fillForm([
                'community_id' => $communityId,
                'user_id' => $staff->user_id,
                'role' => 'headman',
                'current_password' => '',
            ])
            ->call('assignStaff');
        $this->assertSame(0, DB::table(Zapara::communities().'.staff_assignments')->where('user_id', $staff->user_id)->count());

        Livewire::test(Staff::class)
            ->fillForm([
                'community_id' => $communityId,
                'user_id' => $staff->user_id,
                'role' => 'headman',
                'current_password' => 'wrong-password',
            ])
            ->call('assignStaff');
        $this->assertSame(0, DB::table(Zapara::communities().'.staff_assignments')->where('user_id', $staff->user_id)->count());

        Livewire::test(Staff::class)
            ->fillForm([
                'community_id' => $communityId,
                'user_id' => $staff->user_id,
                'role' => 'headman',
                'current_password' => self::PASSWORD,
            ])
            ->call('assignStaff')
            ->assertHasNoFormErrors();
        $this->assertSame('headman', DB::table(Zapara::communities().'.memberships')->where('user_id', $staff->user_id)->value('role'));

        $requestId = (string) Str::uuid();
        DB::table(Zapara::communities().'.join_requests')->insert([
            'request_id' => $requestId,
            'community_id' => $communityId,
            'user_id' => $member->user_id,
            'status' => 'pending',
            'created_at' => now(),
        ]);
        Livewire::test(Memberships::class)
            ->assertSee($member->username)
            ->assertSee($name)
            ->assertSee('Принять')
            ->call('acceptJoin', $requestId)
            ->assertHasNoFormErrors();
        $this->assertSame('member', DB::table(Zapara::communities().'.memberships')->where('user_id', $member->user_id)->value('role'));
        $this->assertSame('accepted', DB::table(Zapara::communities().'.join_requests')->where('request_id', $requestId)->value('status'));

        $rejectId = (string) Str::uuid();
        $other = $this->makeUser(false);
        DB::table(Zapara::communities().'.join_requests')->insert([
            'request_id' => $rejectId,
            'community_id' => $communityId,
            'user_id' => $other->user_id,
            'status' => 'pending',
            'created_at' => now(),
        ]);
        Livewire::test(Memberships::class)
            ->assertSee($other->username)
            ->assertSee('Отклонить')
            ->call('rejectJoin', $rejectId)
            ->assertHasNoFormErrors();
        $this->assertSame('rejected', DB::table(Zapara::communities().'.join_requests')->where('request_id', $rejectId)->value('status'));

        Livewire::test(Content::class)
            ->assertSee($name)
            ->assertSee($communityId)
            ->fillForm([
                'community_id' => $communityId,
                'title' => 'Собрание',
                'body' => 'Текст объявления',
            ])
            ->call('publish')
            ->assertHasNoFormErrors();
        $announcementId = (string) DB::table(Zapara::communities().'.announcements')->where('community_id', $communityId)->value('announcement_id');
        $this->assertNotSame('', $announcementId);

        $homeworkId = (string) Str::uuid();
        DB::table(Zapara::communities().'.shared_homework')->insert([
            'homework_id' => $homeworkId,
            'community_id' => $communityId,
            'title' => 'Спам',
            'body' => 'Удалить',
            'revision' => 1,
            'created_by' => $staff->user_id,
            'created_at' => now(),
            'updated_at' => now(),
        ]);
        $listed = Livewire::test(Content::class);
        $listed->assertSee('Собрание');
        $listed->assertSee('Спам');
        $listed->assertSee('Домашнее задание');
        $listed->assertSee('Снять');
        $listed->fillForm(['current_password' => ''])->call('moderate', 'shared_homework', $homeworkId, $communityId);
        $this->assertSame(1, DB::table(Zapara::communities().'.shared_homework')->where('homework_id', $homeworkId)->count());
        Livewire::test(Content::class)
            ->assertSee('Спам')
            ->fillForm(['current_password' => self::PASSWORD])
            ->call('moderate', 'shared_homework', $homeworkId, $communityId)
            ->assertHasNoFormErrors();
        $this->assertSame(0, DB::table(Zapara::communities().'.shared_homework')->where('homework_id', $homeworkId)->count());

        $familyId = (string) Str::uuid();
        $otherFamilyId = (string) Str::uuid();
        $device = 'Ноутбук '.$this->token('d');
        $otherDevice = 'Планшет '.$this->token('d');
        foreach ([$familyId => $device, $otherFamilyId => $otherDevice] as $id => $deviceName) {
            DB::table(Zapara::accounts().'.session_families')->insert([
                'family_id' => $id,
                'user_id' => $member->user_id,
                'device_id' => (string) Str::uuid(),
                'device_name' => $deviceName,
                'platform' => 'windows',
                'created_at' => now(),
                'authenticated_at' => now(),
                'last_seen_at' => now(),
                'expires_at' => now()->addDay(),
            ]);
        }
        $sessions = Livewire::test(Sessions::class);
        $sessions->assertSee($member->username);
        $sessions->assertSee($device);
        $sessions->assertSee($otherDevice);
        $sessions->fillForm(['current_password' => ''])->call('revokeFamily', $familyId);
        $this->assertNull(DB::table(Zapara::accounts().'.session_families')->where('family_id', $familyId)->value('revoked_at'));
        Livewire::test(Sessions::class)
            ->assertSee($device)
            ->fillForm(['current_password' => 'wrong-password'])
            ->call('revokeFamily', $familyId);
        $this->assertNull(DB::table(Zapara::accounts().'.session_families')->where('family_id', $familyId)->value('revoked_at'));
        Livewire::test(Sessions::class)
            ->assertSee($member->username)
            ->assertSee($device)
            ->fillForm(['current_password' => self::PASSWORD])
            ->call('revokeFamily', $familyId)
            ->assertHasNoFormErrors();
        $this->assertSame('revoke', DB::table(Zapara::accounts().'.session_families')->where('family_id', $familyId)->value('revocation_reason'));
        $this->assertNull(DB::table(Zapara::accounts().'.session_families')->where('family_id', $otherFamilyId)->value('revoked_at'));

        Livewire::test(AuditLog::class)
            ->assertSee('community_created')
            ->assertSee('catalog_mapped')
            ->assertSee('staff_assigned')
            ->assertSee('join_accepted')
            ->assertSee('join_rejected')
            ->assertSee('content_moderated')
            ->assertSee('session_revoked');
    }

    public function test_user_list_shows_russian_statuses_without_changing_the_stored_value(): void
    {
        $admin = $this->makeUser(true);
        $active = $this->makeUser(false);
        $disabled = $this->makeUser(false);
        $deleting = $this->makeUser(false);
        $accounts = Zapara::accounts();
        DB::table($accounts.'.users')->where('user_id', $disabled->user_id)->update(['status' => 'disabled']);
        DB::table($accounts.'.users')->where('user_id', $deleting->user_id)->update(['status' => 'deleting']);
        $this->actingAs($admin);

        Livewire::test(ListAccountUsers::class)
            ->searchTable($active->username)
            ->assertCanSeeTableRecords([$active])
            ->assertSee('активен');
        Livewire::test(ListAccountUsers::class)
            ->searchTable($disabled->username)
            ->assertCanSeeTableRecords([$disabled])
            ->assertSee('отключён');
        Livewire::test(ListAccountUsers::class)
            ->searchTable($deleting->username)
            ->assertCanSeeTableRecords([$deleting])
            ->assertSee('удаляется');

        $this->assertSame('active', DB::table($accounts.'.users')->where('user_id', $active->user_id)->value('status'));
        $this->assertSame('disabled', DB::table($accounts.'.users')->where('user_id', $disabled->user_id)->value('status'));
        $this->assertSame('deleting', DB::table($accounts.'.users')->where('user_id', $deleting->user_id)->value('status'));
    }

    public function test_password_change_rejects_an_outstanding_reset(): void
    {
        $admin = $this->makeUser(true);
        $user = $this->makeUser(false);
        $this->actingAs($admin);
        $token = $this->resetToken();
        $accounts = Zapara::accounts();
        $expires = now()->addMinutes(15)->toIso8601String();
        DB::insert(
            'insert into '.$accounts.'.password_reset_tokens (token_hash, user_id, expires_at) values (decode(?, \'hex\'), ?::uuid, ?)',
            [bin2hex(hash('sha256', $token, true)), $user->user_id, $expires],
        );
        DB::insert(
            'insert into '.$accounts.'.recovery_email_tokens (token_hash, user_id, email, expires_at) values (decode(?, \'hex\'), ?::uuid, ?, ?)',
            [bin2hex(hash('sha256', $this->resetToken(), true)), $user->user_id, 'panel.reset@example.com', $expires],
        );
        $replacement = 'Panel-password-2';
        Livewire::test(EditAccountUser::class, ['record' => $user->user_id])
            ->fillForm([
                'username' => $user->username,
                'display_name' => $user->display_name,
                'password' => $replacement,
            ])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertNotNull(DB::table($accounts.'.password_reset_tokens')->where('user_id', $user->user_id)->value('consumed_at'));
        $this->assertNotNull(DB::table($accounts.'.recovery_email_tokens')->where('user_id', $user->user_id)->value('consumed_at'));

        $result = self::dotnet('Panel_password_change_rejects_outstanding_reset', [
            'ZAPARA_PANEL_USERNAME' => $user->username,
            'ZAPARA_PANEL_PASSWORD' => $replacement,
            'ZAPARA_PANEL_RESET_TOKEN' => $token,
        ]);
        $this->assertSame(0, $result['code'], self::redact($result['output']));
        $this->assertStringContainsString('RESET_CONFIRM=rejected', $result['marker']);
        $this->assertStringContainsString('PANEL_PASSWORD=kept', $result['marker']);
    }

    private function launchTranscript(): string
    {
        $admin = $this->makeUser(true);
        $login = $this->get('/admin/login');
        $login->assertOk();
        $this->actingAs($admin);
        $panel = $this->get('/admin');
        $panel->assertOk();

        return "SIGN-IN\n".$this->normalize((string) $login->getContent())."\nPANEL\n".$this->normalize((string) $panel->getContent());
    }

    private function normalize(string $html): string
    {
        $html = preg_replace('/wire:(snapshot|id|effects|initial-data|key)="[^"]*"/', 'wire:$1=""', $html) ?? $html;
        $html = preg_replace("/livewireId: '[^']*'/", "livewireId: ''", $html) ?? $html;
        $html = preg_replace('/data-csrf="[^"]*"/', 'data-csrf=""', $html) ?? $html;
        $html = preg_replace('/name="csrf-token" content="[^"]*"/', 'name="csrf-token" content=""', $html) ?? $html;
        $html = preg_replace('/value="[^"]{16,}"/', 'value=""', $html) ?? $html;

        return $html;
    }

    private function makeUser(bool $admin): AccountUser
    {
        $username = $this->token($admin ? 'a' : 'm');
        $id = (string) Str::uuid();
        $now = now();
        DB::table(Zapara::accounts().'.users')->insert([
            'user_id' => $id,
            'username' => $username,
            'normalized_username' => strtolower($username),
            'display_name' => $admin ? 'Оператор' : 'Участник',
            'created_at' => $now,
            'status' => 'active',
        ]);
        DB::table(Zapara::accounts().'.password_credentials')->insert([
            'user_id' => $id,
            'password_hash' => IdentityPassword::hash(self::PASSWORD),
            'changed_at' => $now,
        ]);
        if ($admin) {
            DB::table(Zapara::admin().'.platform_admins')->insert([
                'user_id' => $id,
                'granted_at' => $now,
                'revoked_at' => null,
            ]);
        }
        $this->users[] = $id;

        return AccountUser::query()->findOrFail($id);
    }

    private function token(string $prefix): string
    {
        return $prefix.bin2hex(random_bytes(4));
    }

    private function resetToken(): string
    {
        return rtrim(strtr(base64_encode(random_bytes(32)), '+/', '-_'), '=');
    }

    private function deleteUser(string $id): void
    {
        $audited = DB::table(Zapara::communities().'.community_audit')->where('actor_id', $id)->exists()
            || DB::table(Zapara::admin().'.admin_audit')->where('actor_id', $id)->exists();
        if ($audited) {
            return;
        }
        $accounts = Zapara::accounts();
        $families = DB::table($accounts.'.session_families')->where('user_id', $id)->pluck('family_id');
        if ($families->isNotEmpty()) {
            DB::table($accounts.'.access_tokens')->whereIn('family_id', $families)->delete();
            DB::table($accounts.'.refresh_tokens')->whereIn('family_id', $families)->delete();
            DB::table($accounts.'.session_families')->where('user_id', $id)->delete();
        }
        DB::table($accounts.'.password_reset_tokens')->where('user_id', $id)->delete();
        DB::table($accounts.'.recovery_email_tokens')->where('user_id', $id)->delete();
        DB::table($accounts.'.password_credentials')->where('user_id', $id)->delete();
        DB::table(Zapara::admin().'.admin_sessions')->where('user_id', $id)->delete();
        DB::table(Zapara::admin().'.platform_admins')->where('user_id', $id)->delete();
        DB::table($accounts.'.users')->where('user_id', $id)->delete();
    }

    /**
     * @param  array<string, string>  $env
     * @return array{code: int, output: string, marker: string}
     */
    private static function dotnet(string $filter, array $env = []): array
    {
        $dsn = Zapara::dsn();
        if ($dsn === null) {
            return ['code' => 1, 'output' => 'ZAPARA_TEST_POSTGRES is required.', 'marker' => ''];
        }
        $root = dirname(__DIR__, 3);
        $project = $root.DIRECTORY_SEPARATOR.'src'.DIRECTORY_SEPARATOR.'Zapara.Server.Tests'.DIRECTORY_SEPARATOR.'Zapara.Server.Tests.csproj';
        $marker = tempnam(sys_get_temp_dir(), 'zapara-panel-');
        if ($marker === false) {
            return ['code' => 1, 'output' => 'marker file was not created', 'marker' => ''];
        }
        $process = new Process(
            ['dotnet', 'test', $project, '--filter', $filter, '--nologo', '--verbosity', 'minimal'],
            $root,
            array_merge($env, [
                'ZAPARA_TEST_POSTGRES' => $dsn,
                'ZAPARA_PANEL_RESULT' => $marker,
            ]),
            null,
            300,
        );
        $process->run();
        $output = $process->getOutput().$process->getErrorOutput();
        $written = is_file($marker) ? (string) file_get_contents($marker) : '';
        @unlink($marker);

        return ['code' => $process->getExitCode() ?? 1, 'output' => $output, 'marker' => $written];
    }

    private static function redact(string $output): string
    {
        return (string) preg_replace('/Password=[^;\\s]*/', 'Password=[redacted]', $output);
    }
}
