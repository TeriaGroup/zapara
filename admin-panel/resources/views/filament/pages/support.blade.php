<div class="operator-board">
    @forelse ($this->threads() as $thread)
        <article class="operator-row">
            <h2>{{ $thread['subject'] }}</h2>
            <div class="operator-chat">
                @foreach ($thread['messages'] as $message)
                    <p class="{{ $message['author'] === 'operator' ? 'operator-mine' : 'operator-theirs' }}">
                        <strong>{{ $message['author'] === 'operator' ? 'Поддержка' : 'Студент' }}.</strong>
                        {{ $message['body'] }}
                    </p>
                @endforeach
            </div>
            <form wire:submit="send('{{ $thread['id'] }}')" class="operator-reply">
                <textarea wire:model="reply" rows="3" placeholder="Ответ" aria-label="Ответ"></textarea>
                <button type="submit">Ответить</button>
            </form>
        </article>
    @empty
        <p>Обращений пока нет.</p>
    @endforelse
</div>
<style>
    .operator-board { display: grid; gap: 16px; width: min(880px, 100%); }
    .operator-row { border: 1px solid #e5e5e5; border-radius: 12px; padding: 16px; background: #fff; overflow-wrap: anywhere; }
    .operator-row h2 { margin: 0 0 12px; font-size: 18px; }
    .operator-chat { display: grid; gap: 8px; }
    .operator-chat p { margin: 0; padding: 10px 12px; border-radius: 12px; }
    .operator-theirs { background: #f5f5f5; }
    .operator-mine { background: #fff7e8; }
    .operator-reply { display: grid; gap: 8px; margin-top: 12px; }
    .operator-reply textarea { width: 100%; box-sizing: border-box; border: 1px solid #e5e5e5; border-radius: 10px; padding: 10px; }
    .operator-reply button { justify-self: start; border: 0; border-radius: 8px; padding: 0 16px; background: #111; color: #fff; min-height: 48px; }
    @media (max-width: 640px) {
        .operator-board { width: 100%; }
        .operator-row { padding: 12px; }
        .operator-reply button { justify-self: stretch; }
    }
</style>
