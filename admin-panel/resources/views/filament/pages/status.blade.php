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
        .operator-summary span { font-size: 13px; color: #3f3f3f; }
        .operator-summary-bad { background: #fdecec; border-color: #f4b8b3; color: #8f1d14; }
        .operator-summary-warn { background: #fff6e0; border-color: #f2d48a; color: #7a4b00; }
        .operator-summary-ok { background: #e8f6ee; border-color: #b5e0c6; color: #11603a; }
        .operator-row { border: 1px solid #d8d8d8; border-left-width: 4px; border-radius: 12px; padding: 16px 18px; background: #fff; color: #161616; overflow-wrap: anywhere; }
        .operator-row-error { border-left-color: #d92d20; }
        .operator-row-warning { border-left-color: #dc8b05; }
        .operator-row-ok { border-left-color: #d8d8d8; }
        .operator-row-head { display: flex; justify-content: space-between; gap: 12px; align-items: center; flex-wrap: wrap; }
        .operator-row strong, .operator-row p { color: #161616; -webkit-user-select: text; user-select: text; }
        .operator-row strong { font-size: 16px; font-weight: 650; }
        .operator-row p { margin: 8px 0 0; color: #3f3f3f; line-height: 1.45; }
        .operator-badge { border-radius: 99px; padding: 6px 12px; font-size: 13px; font-weight: 650; white-space: nowrap; }
        .operator-badge-ok { background: #e8f6ee; color: #157347; }
        .operator-badge-warning { background: #fff2d6; color: #8a5300; }
        .operator-badge-error { background: #fdecec; color: #b42318; }
        @media (max-width: 640px) {
            .operator-board { width: 100%; }
            .operator-row, .operator-summary { padding: 12px; }
        }
    </style>
</x-filament-panels::page>
