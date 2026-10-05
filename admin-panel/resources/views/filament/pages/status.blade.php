<x-filament-panels::page>
    <div class="operator-board">
        <article class="operator-row">
            <p class="operator-note">Каждая строка — отдельная часть. Справа отметка, под названием — что с ней сейчас.</p>
        </article>
        @foreach ($this->rows() as $row)
            <article class="operator-row">
                <div class="operator-row-head">
                    <strong>{{ $row['name'] }}</strong>
                    <span class="{{ $row['ok'] ? 'operator-ok' : 'operator-bad' }}">{{ $row['ok'] ? 'Работает' : 'Не работает' }}</span>
                </div>
                <p>{{ $row['detail'] }}</p>
            </article>
        @endforeach
    </div>
    <style>
        .operator-board { display: grid; gap: 12px; width: min(880px, 100%); }
        .operator-row { border: 1px solid #d8d8d8; border-radius: 12px; padding: 16px 18px; background: #fff; color: #161616; overflow-wrap: anywhere; }
        .operator-row-head { display: flex; justify-content: space-between; gap: 12px; align-items: center; flex-wrap: wrap; }
        .operator-row strong, .operator-row p, .operator-note { color: #161616; -webkit-user-select: text; user-select: text; }
        .operator-row strong { font-size: 16px; font-weight: 650; }
        .operator-note { margin: 0; line-height: 1.45; }
        .operator-row p { margin: 8px 0 0; color: #3f3f3f; line-height: 1.45; }
        .operator-row .operator-note { margin: 0; color: #161616; }
        .operator-ok, .operator-bad { border-radius: 99px; padding: 6px 12px; font-size: 13px; font-weight: 650; white-space: nowrap; }
        .operator-ok { background: #e8f6ee; color: #157347; }
        .operator-bad { background: #fdecec; color: #b42318; }
        @media (max-width: 640px) {
            .operator-board { width: 100%; }
            .operator-row { padding: 12px; }
        }
    </style>
</x-filament-panels::page>
