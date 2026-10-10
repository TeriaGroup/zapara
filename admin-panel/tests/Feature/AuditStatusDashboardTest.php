<?php

namespace Tests\Feature;

use App\Filament\Pages\AuditLog;
use App\Filament\Pages\Memberships;
use App\Filament\Pages\PlatformStatus;
use App\Filament\Widgets\OperatorStats;
use App\Filament\Widgets\RecentAudit;
use App\Models\AdminAudit;
use App\Services\OperatorWork;
use App\Services\PlatformHealth;
use App\Support\AuditDictionary;
use App\Support\Zapara;
use Filament\Facades\Filament;
use Illuminate\Support\Facades\DB;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

class AuditStatusDashboardTest extends TestCase
{
    use PanelFixtures;

    /** @var list<object> */
    private array $savedStorage = [];

    public function test_every_audit_key_allowed_by_the_database_has_a_russian_label(): void
    {
        $checks = [
            'admin_audit_action_check' => AuditDictionary::ACTIONS,
            'admin_audit_object_type_check' => AuditDictionary::OBJECTS,
            'admin_audit_outcome_check' => AuditDictionary::OUTCOMES,
        ];
        foreach ($checks as $name => $dictionary) {
            $definition = (string) DB::table('pg_constraint')->where('conname', $name)->value(DB::raw('pg_get_constraintdef(oid)'));
            preg_match_all("/'([a-z_]+)'::text/", $definition, $matches);
            $this->assertNotEmpty($matches[1], $name);
            foreach ($matches[1] as $key) {
                $this->assertArrayHasKey($key, $dictionary, $name.': '.$key);
                $this->assertDoesNotMatchRegularExpression('/[a-z]/i', $dictionary[$key]);
            }
        }
    }

    public function test_audit_log_shows_who_what_when_in_russian_and_filters(): void
    {
        $admin = $this->makeUser(true, 'Оператор Журнала');
        $other = $this->makeUser(true);
        $this->actingAs($admin);
        $name = 'Группа '.$this->token('c');
        $id = app(OperatorWork::class)->createCommunity($admin, $name, '');
        $event = AdminAudit::query()->where('object_id', $id)->firstOrFail();

        $page = Livewire::test(AuditLog::class)
            ->assertCanSeeTableRecords([$event])
            ->assertSee('Когда (МСК)')
            ->assertSee('Создано сообщество')
            ->assertSee($admin->username)
            ->assertSee('Оператор Журнала')
            ->assertSee('«'.$name.'»')
            ->assertSee('Выполнено')
            ->assertSee($event->created_at->timezone('Europe/Moscow')->format('H:i'));
        $this->assertStringNotContainsString('community_created', strip_tags((string) $page->html()));
        $this->assertStringNotContainsString('>'.$id.'<', (string) $page->html());

        Livewire::test(AuditLog::class)->filterTable('actor_id', $other->user_id)->assertCanNotSeeTableRecords([$event]);
        Livewire::test(AuditLog::class)->filterTable('actor_id', $admin->user_id)->assertCanSeeTableRecords([$event]);
        Livewire::test(AuditLog::class)->filterTable('action', ['join_accepted'])->assertCanNotSeeTableRecords([$event]);
        Livewire::test(AuditLog::class)->filterTable('action', ['community_created'])->assertCanSeeTableRecords([$event]);
        $today = now('Europe/Moscow')->toDateString();
        Livewire::test(AuditLog::class)->filterTable('created_at', ['from' => $today, 'until' => $today])->assertCanSeeTableRecords([$event]);
        Livewire::test(AuditLog::class)->filterTable('created_at', ['from' => now('Europe/Moscow')->addDay()->toDateString()])->assertCanNotSeeTableRecords([$event]);
    }

    public function test_status_lists_problems_first_with_a_summary_and_treats_missing_s3_as_a_warning(): void
    {
        $this->clearStorageSettings();
        $this->actingAs($this->makeUser(true));
        $rows = app(PlatformHealth::class)->sorted();
        $levels = array_map(fn (array $row): int => PlatformHealth::LEVEL_ORDER[$row['level']], $rows);
        $sorted = $levels;
        sort($sorted);
        $this->assertSame($sorted, $levels);
        $storage = collect($rows)->firstWhere('name', 'Хранилище S3');
        $this->assertSame('warning', $storage['level']);
        $this->assertSame('Не настроено', $storage['badge']);
        $this->assertStringContainsString('диск сервера', $storage['detail']);

        $summary = PlatformHealth::summary($rows);
        $html = (string) Livewire::test(PlatformStatus::class)->assertOk()->assertSee($summary['text'])->assertSee('МСК')->html();
        $this->assertStringNotContainsString('UTC', $html);
        $this->assertStringNotContainsString('Каждая строка', $html);
        $this->assertLessThan(strpos($html, 'Аккаунты'), strpos($html, 'Хранилище S3'));
    }

    public function test_summary_uses_russian_plurals(): void
    {
        $row = fn (string $level): array => ['level' => $level];
        $this->assertSame('Всё работает', PlatformHealth::summary([$row('ok')])['text']);
        $this->assertSame('1 предупреждение', PlatformHealth::summary([$row('warning')])['text']);
        $this->assertSame('2 проблемы, 5 предупреждений', PlatformHealth::summary(array_merge(array_fill(0, 2, $row('error')), array_fill(0, 5, $row('warning'))))['text']);
        $this->assertSame('11 проблем', PlatformHealth::summary(array_fill(0, 11, $row('error')))['text']);
        $this->assertSame('21 проблема', PlatformHealth::summary(array_fill(0, 21, $row('error')))['text']);
    }

    public function test_dashboard_shows_four_linked_widgets_and_recent_actions(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        $communityId = $this->makeCommunity('Группа '.$this->token('c'));
        $this->makeJoinRequest($communityId, $this->makeUser(false));
        $pending = DB::table(Zapara::communities().'.join_requests')->where('status', 'pending')->count();

        Livewire::test(OperatorStats::class)
            ->assertSee('Заявки ожидают')
            ->assertSee((string) $pending)
            ->assertSee('Состояние')
            ->assertSee('Обращения')
            ->assertSee('Обновление расписания')
            ->assertSeeHtml('href="'.Memberships::getUrl().'"')
            ->assertSeeHtml('href="'.PlatformStatus::getUrl().'"');

        $name = 'Группа '.$this->token('c');
        app(OperatorWork::class)->createCommunity($admin, $name, '');
        Livewire::test(RecentAudit::class)
            ->assertSee('Последние действия')
            ->assertSee('Создано сообщество')
            ->assertSee('«'.$name.'»')
            ->assertSeeHtml('href="'.AuditLog::getUrl().'"');

        $this->get('/admin')->assertOk()->assertSee('Заявки ожидают')->assertSee('Последние действия');
        $widgets = Filament::getPanel('admin')->getWidgets();
        $this->assertContains(OperatorStats::class, $widgets);
        $this->assertContains(RecentAudit::class, $widgets);
    }

    public function test_r2_who_column_does_not_repeat_the_login_as_the_name(): void
    {
        $this->assertNull(AuditLog::actorNote('design.admin', 'design.admin'));
        $this->assertNull(AuditLog::actorNote('design.admin', ' Design.Admin '));
        $this->assertNull(AuditLog::actorNote('design.admin', ''));
        $this->assertNull(AuditLog::actorNote(null, null));
        $this->assertSame('Оператор Журнала', AuditLog::actorNote('op1', 'Оператор Журнала'));

        $admin = $this->makeUser(true);
        DB::table(Zapara::accounts().'.users')->where('user_id', $admin->user_id)->update(['display_name' => $admin->username]);
        $this->actingAs($admin->fresh());
        app(OperatorWork::class)->createCommunity($admin, 'Группа '.$this->token('c'), '');
        $html = (string) Livewire::test(RecentAudit::class)->assertSee($admin->username)->html();
        preg_match_all('~<p class="fi-ta-text-description">(.*?)</p>~s', $html, $notes);
        foreach ($notes[1] as $note) {
            $this->assertStringNotContainsString($admin->username, strip_tags($note));
        }
    }

    public function test_r2_recent_actions_show_ten_on_desktop_and_five_on_a_phone_with_a_link_to_the_log(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        $before = AdminAudit::query()->count();
        foreach (range(1, 12) as $n) {
            app(OperatorWork::class)->createCommunity($admin, 'Группа '.$this->token('c'.$n), '');
        }
        $this->assertGreaterThanOrEqual(12, AdminAudit::query()->count() - $before);
        $latest = AdminAudit::query()->orderByDesc('created_at')->orderByDesc('event_id')->limit(12)->get();

        $page = Livewire::test(RecentAudit::class)
            ->assertCanSeeTableRecords($latest->take(RecentAudit::DESKTOP_LIMIT))
            ->assertCanNotSeeTableRecords($latest->slice(RecentAudit::DESKTOP_LIMIT))
            ->assertSeeHtml('href="'.AuditLog::getUrl().'"')
            ->assertSee('Весь журнал');
        $this->assertSame(10, RecentAudit::DESKTOP_LIMIT);
        $this->assertSame(5, RecentAudit::PHONE_LIMIT);
        // Строки 6–10 помечены и на телефоне скрыты; первые пять — без пометки.
        $this->assertSame(RecentAudit::DESKTOP_LIMIT - RecentAudit::PHONE_LIMIT, substr_count((string) $page->html(), RecentAudit::EXTRA_CLASS));

        $css = (string) $this->get('/admin')->assertOk()->getContent();
        $this->assertMatchesRegularExpression('~@media \(max-width: 767px\) \{\s*\.zp-recent-audit-extra \{\s*display: none !important;~', $css);
    }

    protected function tearDown(): void
    {
        foreach ($this->savedStorage as $row) {
            DB::table(Zapara::settings().'.system_settings')->updateOrInsert(['key' => $row->key], (array) $row);
        }
        parent::tearDown();
    }

    private function clearStorageSettings(): void
    {
        $table = Zapara::settings().'.system_settings';
        $relation = DB::selectOne('select to_regclass(?)::text as name', [$table]);
        if (! is_object($relation) || ! is_string($relation->name) || $relation->name === '') {
            return;
        }
        $this->savedStorage = DB::table($table)->where('key', 'like', 's3\_%')->get()->all();
        DB::table($table)->where('key', 'like', 's3\_%')->delete();
    }
}
