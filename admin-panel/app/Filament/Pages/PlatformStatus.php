<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Services\PlatformHealth;
use Filament\Pages\Page;

class PlatformStatus extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Состояние';

    protected static ?string $title = 'Состояние';

    protected static ?string $slug = 'status';

    protected static ?int $navigationSort = 12;

    protected string $view = 'filament.pages.status';

    /** @var list<array{name: string, level: string, badge: string, detail: string}>|null */
    private ?array $checked = null;

    /**
     * Проблемы первыми.
     *
     * @return list<array{name: string, level: string, badge: string, detail: string}>
     */
    public function rows(): array
    {
        return $this->checked ??= app(PlatformHealth::class)->sorted();
    }

    /**
     * @return array{errors: int, warnings: int, text: string}
     */
    public function summary(): array
    {
        return PlatformHealth::summary($this->rows());
    }
}
