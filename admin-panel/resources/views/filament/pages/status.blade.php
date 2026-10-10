<x-filament-panels::page>
    @php($summary = $this->summary())
    <div class="operator-board">
        <div @class([
            'operator-summary',
            'operator-summary-bad' => $summary['errors'] > 0,
            'operator-summary-warn' => $summary['errors'] === 0 && $summary['warnings'] > 0,
            'operator-summary-ok' => $summary['errors'] === 0 && $summary['warnings'] === 0,
        ]) role="status">
            <strong>{{ $summary['text'] }}</strong>
            <span>Проверено {{ \App\Filament\Support\MoscowTime::time(now()->toIso8601String()) }}</span>
        </div>
        @foreach ($this->rows() as $row)
            <article class="operator-row operator-row-{{ $row['level'] }}">
                <div class="operator-row-head">
                    <strong>{{ $row['name'] }}</strong>
                    <span class="operator-badge operator-badge-{{ $row['level'] }}">{{ $row['badge'] }}</span>
                </div>
                <p>{{ $row['detail'] }}</p>
            </article>
        @endforeach
    </div>
    <style>
        .operator-board { display: grid; gap: 12px; width: min(880px, 100%); }
        .operator-summary { display: flex; justify-content: space-between; gap: 8px 16px; flex-wrap: wrap; align-items: baseline; border-radius: 12px; padding: 14px 18px; border: 1px solid; }
        .operator-summary strong { font-size: 16px; font-weight: 650; }
        .operator-summary span { font-size: 13px; color: var(--zp-text-secondary); }
        .operator-summary-bad { background: var(--zp-danger-soft); border-color: var(--zp-danger); color: var(--zp-text-primary); }
        .operator-summary-warn { background: var(--zp-warning-surface); border-color: var(--zp-warning); color: var(--zp-warning-text); }
        .operator-summary-ok { background: var(--zp-surface-1); border-color: var(--zp-success); color: var(--zp-text-primary); }
        .operator-row { border: 1px solid var(--zp-border-subtle); border-left-width: 4px; border-radius: 12px; padding: 16px 18px; background: var(--zp-surface-0); color: var(--zp-text-primary); overflow-wrap: anywhere; }
        .operator-row-error { border-left-color: var(--zp-danger); }
        .operator-row-warning { border-left-color: var(--zp-warning); }
        .operator-row-ok { border-left-color: var(--zp-border-subtle); }
        .operator-row-head { display: flex; justify-content: space-between; gap: 12px; align-items: center; flex-wrap: wrap; }
        .operator-row strong, .operator-row p { color: var(--zp-text-primary); -webkit-user-select: text; user-select: text; }
        .operator-row strong { font-size: 16px; font-weight: 650; }
        .operator-row p { margin: 8px 0 0; color: var(--zp-text-secondary); line-height: 1.45; }
        .operator-badge { border-radius: 99px; padding: 6px 12px; font-size: 13px; font-weight: 650; white-space: nowrap; }
        .operator-badge-ok { background: var(--zp-surface-chip); color: var(--zp-text-primary); box-shadow: inset 0 0 0 1px var(--zp-success); }
        .operator-badge-warning { background: var(--zp-warning-surface); color: var(--zp-warning-text); }
        .operator-badge-error { background: var(--zp-surface-chip); color: var(--zp-text-primary); box-shadow: inset 0 0 0 1px var(--zp-danger); }
        @media (max-width: 640px) {
            .operator-board { width: 100%; }
            .operator-row, .operator-summary { padding: 12px; }
        }
    </style>
</x-filament-panels::page>
