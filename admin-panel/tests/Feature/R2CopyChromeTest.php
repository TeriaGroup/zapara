<?php

namespace Tests\Feature;

use App\Filament\Pages\PlatformStatus;
use App\Filament\Resources\StaffAssignments\StaffAssignmentResource;
use Livewire\Livewire;
use Tests\Concerns\PanelFixtures;
use Tests\TestCase;

/** Ревью раунда 2: R2-17, R2-20, R2-21 — тексты и оформление админки. */
class R2CopyChromeTest extends TestCase
{
    use PanelFixtures;

    public function test_r2_17_login_shows_the_product_name_once(): void
    {
        $html = $this->get('/admin/login')->assertOk()->getContent();
        $this->assertStringNotContainsString('Военмех · Администрирование', $html);
        $this->assertStringContainsString('Администрирование', $html);
    }

    public function test_r2_20_status_speaks_plain_russian(): void
    {
        $this->actingAs($this->makeUser(true));
        Livewire::test(PlatformStatus::class)->assertOk()
            ->assertDontSee('открываются')
            ->assertDontSee('Последняя попытка обновления прошла')
            ->assertSee('Данные доступны.');
    }

    public function test_r2_21_sort_placeholder_and_no_empty_band_above_search(): void
    {
        $this->assertNull(StaffAssignmentResource::nameUnlessLogin('design.student', 'design.student'));
        $this->assertNull(StaffAssignmentResource::nameUnlessLogin('design.student', ' Design.Student '));
        $this->assertNull(StaffAssignmentResource::nameUnlessLogin('kuznetsova', ''));
        $this->assertSame('Анна Кузнецова', StaffAssignmentResource::nameUnlessLogin('kuznetsova', 'Анна Кузнецова'));

        $this->actingAs($this->makeUser(true));
        $html = $this->get('/admin/staff')->assertOk()->getContent();
        $this->assertStringContainsString('.fi-ta-header-toolbar > .fi-ta-actions:not(:has(*))', $html);
        $this->assertStringContainsString('По умолчанию', $html);
    }
}
