<div class="operator-board">
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
    .operator-row { border: 1px solid #e5e5e5; border-radius: 12px; padding: 16px; background: #fff; overflow-wrap: anywhere; }
    .operator-row-head { display: flex; justify-content: space-between; gap: 12px; align-items: center; flex-wrap: wrap; }
    .operator-row p { margin: 8px 0 0; }
    .operator-ok, .operator-bad { border-radius: 99px; padding: 4px 10px; font-size: 13px; }
    .operator-ok { background: #e8f6ee; color: #157347; }
    .operator-bad { background: #fdecec; color: #b42318; }
    @media (max-width: 640px) {
        .operator-board { width: 100%; }
        .operator-row { padding: 12px; }
    }
</style>
