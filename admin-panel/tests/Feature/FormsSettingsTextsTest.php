<?php

namespace Tests\Feature;

use App\Filament\Pages\Login;
use App\Filament\Pages\SystemSettings;
use App\Filament\Resources\AccountUsers\Pages\ListAccountUsers;
use App\Services\OperatorSettings;
use App\Support\ByteSize;
use App\Support\Zapara;
use Illuminate\Support\Facades\DB;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

class FormsSettingsTextsTest extends TestCase
{
    use PanelFixtures;

    /**
     * @var array<string, string>
     */
    private array $savedSettings = [];

    protected function tearDown(): void
    {
        if ($this->savedSettings !== []) {
            $table = Zapara::settings().'.system_settings';
            DB::table($table)->whereIn('key', ['vk_secret', 'yandex_secret', 'quota_group_bytes', 'quota_user_bytes'])->delete();
            foreach ($this->savedSettings as $key => $value) {
                DB::table($table)->insert(['key' => $key, 'value' => $value, 'updated_at' => now()]);
            }
        }
        parent::tearDown();
    }

    public function test_user_forms_use_correct_russian_and_status_is_a_badge(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);

        $create = (string) $this->get('/admin/users/create')->assertOk()->getContent();
        $this->assertStringContainsString('Создать пользователя', $create);
        $this->assertStringContainsString('Создать и добавить ещё', $create);
        $this->assertStringContainsString('12–128 символов.', $create);
        $this->assertStringNotContainsString('Создать Пользователь', $create);
        $this->assertStringNotContainsString('Создать и Создать', $create);
        $this->get('/admin/users')->assertOk()->assertSee('Создать пользователя');
        $this->get('/admin/content')->assertOk()->assertSee('Выберите сообщество')->assertDontSee('Выбрать вариант');

        $list = Livewire::test(ListAccountUsers::class)->searchTable($admin->username)->assertSee('Активен');
        $this->assertTrue($list->instance()->getTable()->getColumn('status')->isBadge());
    }

    public function test_quotas_are_entered_with_units_and_unchanged_bytes_survive_a_save(): void
    {
        $this->rememberSettings();
        OperatorSettings::save('quota_group_bytes', '1000');
        OperatorSettings::save('quota_user_bytes', '524288000');
        $this->actingAs($this->makeUser(true));

        Livewire::test(SystemSettings::class)
            ->assertSchemaStateSet([
                'quota_group_amount' => '0.001',
                'quota_group_unit' => 'mb',
                'quota_user_amount' => '500',
                'quota_user_unit' => 'mb',
            ])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertSame('1000', OperatorSettings::read('quota_group_bytes'));
        $this->assertSame('524288000', OperatorSettings::read('quota_user_bytes'));

        Livewire::test(SystemSettings::class)
            ->fillForm(['quota_group_amount' => '2', 'quota_group_unit' => 'gb', 'quota_user_amount' => '300', 'quota_user_unit' => 'mb'])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertSame('2147483648', OperatorSettings::read('quota_group_bytes'));
        $this->assertSame('314572800', OperatorSettings::read('quota_user_bytes'));
        Livewire::test(SystemSettings::class)->assertSchemaStateSet(['quota_group_amount' => '2', 'quota_group_unit' => 'gb']);

        $this->assertSame('1 ГБ', ByteSize::format(1073741824));
        $this->assertSame('500 МБ', ByteSize::format(524288000));
        $this->get('/admin/quotas')->assertOk()->assertSee('2 ГБ')->assertSee('300 МБ')->assertDontSee('ГиБ');
    }

    public function test_secret_fields_show_whether_a_key_is_set_and_change_only_on_request(): void
    {
        $this->rememberSettings();
        $secret = 'vk-'.bin2hex(random_bytes(12));
        OperatorSettings::save('vk_secret', $secret, true);
        DB::table(Zapara::settings().'.system_settings')->where('key', 'yandex_secret')->delete();
        $this->actingAs($this->makeUser(true));

        $page = Livewire::test(SystemSettings::class)
            ->assertSee('Задан')
            ->assertSee('Не задан')
            ->assertSee('Адрес скопирован', false)
            ->assertFormFieldIsReadOnly('vk_secret');
        $this->assertStringNotContainsString($secret, (string) $page->html());

        $replacement = 'vk-'.bin2hex(random_bytes(12));
        $page->set('data.vk_secret_edit', true)
            ->fillForm(['vk_secret' => $replacement])
            ->call('save')
            ->assertHasNoFormErrors();
        $this->assertSame($replacement, OperatorSettings::read('vk_secret'));

        Livewire::test(SystemSettings::class)->call('save')->assertHasNoFormErrors();
        $this->assertSame($replacement, OperatorSettings::read('vk_secret'));
    }

    public function test_login_says_administration_and_shows_the_error_above_the_form(): void
    {
        $member = $this->makeUser(false);
        $this->get('/admin/login')->assertOk()->assertSee('Военмех · Администрирование');

        Livewire::test(Login::class)
            ->fillForm(['username' => $member->username, 'password' => 'wrong-password-1'])
            ->call('authenticate')
            ->assertHasErrors(['data.credentials'])
            ->assertHasNoErrors(['data.username', 'data.password'])
            ->assertSee('Неверное имя пользователя или пароль.')
            ->assertSeeHtml('role="alert"');
        $this->assertGuest();
    }

    public function test_support_empty_state_and_field_border_contrast(): void
    {
        $this->actingAs($this->makeUser(true));
        $schema = Zapara::settings();
        $threads = DB::selectOne('select to_regclass(?)::text as name', [$schema.'.support_threads'])?->name
            ? DB::table($schema.'.support_threads')->count()
            : 0;
        $support = $this->get('/admin/support')->assertOk();
        if ($threads === 0) {
            $support->assertSee('Обращений пока нет.')->assertSee('Настройки → Помощь');
        }

        $page = (string) $this->get('/admin/settings')->assertOk()->getContent();
        $this->assertMatchesRegularExpression('/:root\s*\{\s*--zp-border-control-tmp:\s*(#[0-9a-f]{6})/i', $page);
        preg_match('/:root\s*\{\s*--zp-border-control-tmp:\s*(#[0-9a-f]{6})/i', $page, $light);
        preg_match('/\.dark\s*\{\s*--zp-border-control-tmp:\s*(#[0-9a-f]{6})/i', $page, $dark);
        // Фон поля и страницы в светлой теме; фон секции и поля в тёмной (gray-900 и white 5% поверх него).
        foreach (['#ffffff', '#fafafa'] as $background) {
            $this->assertGreaterThanOrEqual(3.0, self::contrast($light[1], $background), $light[1].' / '.$background);
        }
        foreach (['#18181b', '#242427'] as $background) {
            $this->assertGreaterThanOrEqual(3.0, self::contrast($dark[1], $background), $dark[1].' / '.$background);
        }
    }

    private function rememberSettings(): void
    {
        foreach (['vk_secret', 'yandex_secret', 'quota_group_bytes', 'quota_user_bytes'] as $key) {
            $value = OperatorSettings::read($key);
            if ($value !== null) {
                $this->savedSettings[$key] = $value;
            }
        }
        $this->savedSettings += ['quota_group_bytes' => '1073741824', 'quota_user_bytes' => '524288000'];
    }

    private static function contrast(string $a, string $b): float
    {
        $luminance = static function (string $hex): float {
            $channels = array_map(static function (string $pair): float {
                $c = hexdec($pair) / 255;

                return $c <= 0.03928 ? $c / 12.92 : (($c + 0.055) / 1.055) ** 2.4;
            }, str_split(ltrim($hex, '#'), 2));

            return 0.2126 * $channels[0] + 0.7152 * $channels[1] + 0.0722 * $channels[2];
        };
        [$hi, $lo] = [max($luminance($a), $luminance($b)), min($luminance($a), $luminance($b))];

        return ($hi + 0.05) / ($lo + 0.05);
    }
}
