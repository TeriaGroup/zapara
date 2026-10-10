<?php

namespace Tests\Feature;

use Filament\Support\Colors\Color;
use Filament\Support\Facades\FilamentColor;
use Tests\TestCase;

// #11: тема Filament берёт цвета из общих дизайн-токенов (design/tokens.json → config/design-tokens.php).
class DesignTokensTest extends TestCase
{
    public function test_filament_colors_come_from_design_tokens(): void
    {
        $html = $this->get('/admin/login')->assertOk()->getContent();
        $colors = FilamentColor::getColors();
        $tokens = config('design-tokens');

        $this->assertSame(Color::convertToOklch($tokens['light']['accent']), $colors['primary'][600]);
        foreach (['danger', 'warning', 'success', 'info'] as $role) {
            $this->assertSame(Color::convertToOklch($tokens['light'][$role]), $colors[$role][600], $role);
            $this->assertCount(11, $colors[$role], $role);
        }
        $this->assertNotSame(Color::Amber[600], $colors['primary'][600]);
        $this->assertStringContainsString('--primary-600:'.Color::convertToOklch($tokens['light']['accent']), $html);
    }

    public function test_panel_pages_expose_token_css_variables_for_both_themes(): void
    {
        $html = $this->get('/admin/login')->assertOk()->getContent();

        $this->assertStringContainsString('--zp-border-control: '.config('design-tokens.light.border-control'), $html);
        $this->assertStringContainsString('--zp-border-control: '.config('design-tokens.dark.border-control'), $html);
        $this->assertMatchesRegularExpression('/\.dark\s*\{[^}]*--zp-text-primary/s', $html);

        // В тёмной теме кнопка primary — светлый accent с тёмным текстом, а не почти чёрная на чёрном фоне.
        preg_match('/\.dark \.fi-btn\.fi-color-primary\s*\{([^}]*)\}/s', $html, $button);
        $this->assertStringContainsString('--dark-bg: '.config('design-tokens.dark.accent').';', $button[1]);
        $this->assertStringContainsString('--dark-text: '.config('design-tokens.dark.on-accent').';', $button[1]);
        // Активный пункт меню в тёмной теме — основной цвет текста, а не тусклый серый.
        $this->assertMatchesRegularExpression('/\.dark\s*\{[^}]*--primary-400: '.preg_quote(config('design-tokens.dark.text-primary'), '/').';/s', $html);
    }

    // #8 (G-1): страницы оператора («Состояние», «Квоты», «Поддержка») и стили форм не задают цвета текста, границ и
    // опасных состояний вручную — только переменные токенов, свои для светлой и тёмной темы.
    public function test_panel_views_use_token_variables_instead_of_hard_coded_colors(): void
    {
        $root = resource_path('views/filament');
        $files = new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($root, \FilesystemIterator::SKIP_DOTS));
        $hits = [];
        $used = [];
        foreach ($files as $file) {
            $path = $file->getPathname();
            if (! str_ends_with($path, '.blade.php') || basename($path) === 'design-tokens.blade.php') {
                continue; // design-tokens.blade.php генерирует scripts/design/tokens.mjs из design/tokens.json
            }
            foreach (file($path) as $number => $line) {
                if (preg_match('/#[0-9a-fA-F]{3,8}\b|rgba?\(/', $line)) {
                    $hits[] = substr($path, strlen($root) + 1).':'.($number + 1);
                }
                preg_match_all('/var\(--zp-([a-z0-9-]+)/', $line, $vars);
                $used = array_merge($used, $vars[1]);
            }
        }
        $this->assertSame([], $hits);
        $this->assertNotEmpty($used);
        foreach (array_unique($used) as $token) {
            $this->assertArrayHasKey($token, config('design-tokens.light'), $token);
            $this->assertArrayHasKey($token, config('design-tokens.dark'), $token);
        }
    }
}
