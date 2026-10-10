<?php

namespace Tests\Feature;

use App\Filament\Pages\AuditLog;
use App\Filament\Support\MoscowTime;
use App\Models\Community;
use App\Services\OperatorWork;
use Illuminate\Support\Carbon;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

/** G-3: даты в админке — в общем формате глоссария, как на web и desktop: «9 окт., 18:49». */
class GlossaryDatesTest extends TestCase
{
    use PanelFixtures;

    public function test_short_dates_match_web_and_desktop(): void
    {
        $this->travelTo(Carbon::parse('2026-10-10 12:00:00', 'Europe/Moscow'));
        $this->assertSame('9 окт., 18:49', MoscowTime::short('2026-10-09T15:49:00Z'));
        $this->assertSame('9 окт.', MoscowTime::short('2026-10-09T15:49:00Z', time: false));
        $this->assertSame('9 сент., 03:00', MoscowTime::short('2026-09-09T00:00:00Z'));
        $this->assertSame('9 мая', MoscowTime::short('2026-05-09T10:00:00Z', time: false));
        $this->assertSame('9 окт., 18:31 МСК', MoscowTime::dateTime('2026-10-09T15:31:00Z'));
        $this->assertSame('', MoscowTime::short(null));
        $this->assertSame('', MoscowTime::dateTime(null));
    }

    public function test_the_year_is_shown_only_when_it_is_not_the_current_one(): void
    {
        $this->travelTo(Carbon::parse('2026-01-01 00:30:00', 'Europe/Moscow'));
        $this->assertSame('30 дек. 2025, 18:49', MoscowTime::short('2025-12-30T15:49:00Z'));
        $this->assertSame('30 дек. 2025', MoscowTime::short('2025-12-30T15:49:00Z', time: false));
        $this->assertSame('30 дек. 2025, 18:49 МСК', MoscowTime::dateTime('2025-12-30T15:49:00Z'));
        // 31 дек. 21:30 UTC — уже 1 янв. по Москве: текущий год, без года.
        $this->assertSame('1 янв., 00:30', MoscowTime::short('2025-12-31T21:30:00Z'));
        $this->assertSame('5 мар. 2027', MoscowTime::short('2027-03-05T09:00:00Z', time: false));
    }

    public function test_every_admin_date_goes_through_the_shared_helper(): void
    {
        $root = dirname(__DIR__, 2).'/app';
        $files = new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($root, \FilesystemIterator::SKIP_DOTS));
        $hits = [];
        foreach ($files as $file) {
            if ($file->getExtension() !== 'php') {
                continue;
            }
            foreach (file($file->getPathname()) as $i => $line) {
                // Filament ->dateTime()/->date() с форматом и форматы с названием месяца обходят глоссарий (длинный месяц, год всегда).
                // «дд.мм.гггг» (d.m.Y) — формат ввода даты по глоссарию, он разрешён.
                if (preg_match("/->(dateTime|date|since)\\(\\s*'|isoFormat\\('[^']*MMM|format\\('[^']*[FM]/", $line)) {
                    $hits[] = str_replace($root.'/', '', $file->getPathname()).':'.($i + 1);
                }
            }
        }
        $this->assertSame([], $hits);
    }

    public function test_community_view_shows_the_short_creation_date(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        $this->travelTo(Carbon::parse('2026-10-10 12:00:00', 'Europe/Moscow'));
        $id = app(OperatorWork::class)->createCommunity($admin, 'Группа '.$this->token('g'), '');
        Community::query()->whereKey($id)->update(['created_at' => Carbon::parse('2026-10-09 15:49:00', 'UTC')]);
        $this->get('/admin/communities/'.$id)->assertOk()->assertSee('9 окт., 18:49 МСК')->assertDontSee('9 октября 2026');
    }

    public function test_audit_log_dates_use_the_glossary_format(): void
    {
        $admin = $this->makeUser(true);
        $this->actingAs($admin);
        app(OperatorWork::class)->createCommunity($admin, 'Группа '.$this->token('g'), ''); // пишет событие аудита
        $html = (string) Livewire::test(AuditLog::class)->assertOk()->html();
        $this->assertMatchesRegularExpression('/\d{1,2} (янв\.|февр\.|мар\.|апр\.|мая|июн\.|июл\.|авг\.|сент\.|окт\.|нояб\.|дек\.), \d{2}:\d{2}/u', $html);
        $this->assertDoesNotMatchRegularExpression('/\d{1,2} (окт|сен|ноя|дек), \d{2}:\d{2}/u', $html);
    }
}
