<div class="operator-board">
    <p>По умолчанию группа — 1 ГиБ, студент — 500 МиБ. Один файл считается в оба лимита.</p>
    @forelse ($this->rows() as $row)
        <article class="operator-row">
            <strong>{{ $row['scope'] }}</strong>
            <p>{{ $row['id'] }}: {{ $row['bytes'] }} из {{ $row['limit'] }} байт</p>
        </article>
    @empty
        <p>Загрузок пока нет.</p>
    @endforelse
</div>
<style>
    .operator-board { display: grid; gap: 12px; width: min(880px, 100%); }
    .operator-row { border: 1px solid #e5e5e5; border-radius: 12px; padding: 16px; background: #fff; overflow-wrap: anywhere; }
    .operator-row p { margin: 8px 0 0; }
    @media (max-width: 640px) { .operator-board { width: 100%; } .operator-row { padding: 12px; } }
</style>
