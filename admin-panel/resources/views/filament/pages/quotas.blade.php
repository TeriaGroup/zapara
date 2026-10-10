<x-filament-panels::page>
    <div class="operator-board">
        <article class="operator-row">
            <p class="operator-note">Сейчас лимит группы — {{ \App\Support\ByteSize::format($this->groupLimit()) }}, студента — {{ \App\Support\ByteSize::format($this->userLimit()) }}. Один файл считается в оба лимита. Изменить: Настройки системы → Квоты.</p>
        </article>
        @forelse ($this->rows() as $row)
            <article class="operator-row">
                <strong>{{ $row['scope'] }}</strong>
                <p>{{ $row['id'] }}: {{ \App\Support\ByteSize::format($row['bytes']) }} из {{ \App\Support\ByteSize::format($row['limit']) }}</p>
            </article>
        @empty
            <article class="operator-row">
                <p class="operator-note">Загрузок пока нет.</p>
            </article>
        @endforelse
    </div>
    <style>
        .operator-board { display: grid; gap: 12px; width: min(880px, 100%); }
        .operator-row { border: 1px solid var(--zp-border-subtle); border-radius: 12px; padding: 16px; background: var(--zp-surface-0); color: var(--zp-text-primary); overflow-wrap: anywhere; }
        .operator-row strong, .operator-row p, .operator-note { color: var(--zp-text-primary); -webkit-user-select: text; user-select: text; }
        .operator-row strong { font-size: 16px; font-weight: 650; }
        .operator-note { margin: 0; line-height: 1.45; }
        .operator-row p { margin: 8px 0 0; color: var(--zp-text-secondary); line-height: 1.45; }
        .operator-row .operator-note { margin: 0; color: var(--zp-text-primary); }
        @media (max-width: 640px) {
            .operator-board { width: 100%; }
            .operator-row { padding: 12px; }
        }
    </style>
</x-filament-panels::page>
