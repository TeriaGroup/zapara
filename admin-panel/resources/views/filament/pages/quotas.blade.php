<x-filament-panels::page>
    <div class="operator-board">
        <article class="operator-row">
            <p class="operator-note">По умолчанию группа — 1 ГиБ, студент — 500 МиБ. Один файл считается в оба лимита.</p>
        </article>
        @forelse ($this->rows() as $row)
            <article class="operator-row">
                <strong>{{ $row['scope'] }}</strong>
                <p>{{ $row['id'] }}: {{ $row['bytes'] }} из {{ $row['limit'] }} байт</p>
            </article>
        @empty
            <article class="operator-row">
                <p class="operator-note">Загрузок пока нет.</p>
            </article>
        @endforelse
    </div>
    <style>
        .operator-board { display: grid; gap: 12px; width: min(880px, 100%); }
        .operator-row { border: 1px solid #d8d8d8; border-radius: 12px; padding: 16px; background: #fff; color: #161616; overflow-wrap: anywhere; }
        .operator-row strong, .operator-row p, .operator-note { color: #161616; -webkit-user-select: text; user-select: text; }
        .operator-row strong { font-size: 16px; font-weight: 650; }
        .operator-note { margin: 0; line-height: 1.45; }
        .operator-row p { margin: 8px 0 0; color: #3f3f3f; line-height: 1.45; }
        .operator-row .operator-note { margin: 0; color: #161616; }
        @media (max-width: 640px) {
            .operator-board { width: 100%; }
            .operator-row { padding: 12px; }
        }
    </style>
</x-filament-panels::page>
