<?php

namespace Tests\Feature;

use App\Filament\Pages\AuditLog;
use App\Filament\Support\MoscowTime;
use App\Services\OperatorWork;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

/** G-3: даты в админке — в общем формате глоссария, как на web и desktop: «9 окт., 18:49». */
class GlossaryDatesTest extends TestCase
{
    use PanelFixtures;

    public function test_short_dates_match_web_and_desktop(): void
    {
        $this->assertSame('9 окт., 18:49', MoscowTime::short('2026-10-09T15:49:00Z'));
        $this->assertSame('9 окт. 2026, 18:49', MoscowTime::short('2026-10-09T15:49:00Z', year: true));
        $this->assertSame('9 окт. 2026', MoscowTime::short('2026-10-09T15:49:00Z', year: true, time: false));
        $this->assertSame('9 сент., 03:00', MoscowTime::short('2026-09-09T00:00:00Z'));
        $this->assertSame('9 мая', MoscowTime::short('2026-05-09T10:00:00Z', time: false));
        $this->assertSame('9 окт., 18:31 МСК', MoscowTime::dateTime('2026-10-09T15:31:00Z'));
        $this->assertSame('', MoscowTime::short(null));
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
