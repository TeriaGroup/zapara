<x-filament-panels::page>
    <div class="operator-board">
        @forelse ($this->threads() as $thread)
            <article class="operator-row">
                <h2>{{ $thread['subject'] }}</h2>
                <div class="operator-chat">
                    @foreach ($thread['messages'] as $message)
                        <div class="{{ $message['author'] === 'operator' ? 'operator-mine' : 'operator-theirs' }}">
                            <p>
                                <strong>{{ $message['author'] === 'operator' ? 'Поддержка' : 'Студент' }}.</strong>
                                {{ $message['body'] }}
                            </p>
                            @foreach ($message['files'] ?? [] as $file)
                                @if ($file['kind'] === 'photo')
                                    <img class="operator-photo" src="{{ route('filament.admin.support-file', ['id' => $file['id']]) }}" alt="{{ $file['name'] }}">
                                @else
                                    <pre class="operator-log">{{ $file['name'] }}{{ $file['preview'] !== '' ? "\n".$file['preview'] : '' }}</pre>
                                    <a href="{{ route('filament.admin.support-file', ['id' => $file['id']]) }}">Скачать лог</a>
                                @endif
                            @endforeach
                        </div>
                    @endforeach
                </div>
                <form wire:submit="send('{{ $thread['id'] }}')" class="operator-reply">
                    <textarea wire:model="reply" rows="3" placeholder="Ответ" aria-label="Ответ"></textarea>
                    <button type="submit">Ответить</button>
                </form>
            </article>
        @empty
            <article class="operator-row">
                <p class="operator-note">Обращений пока нет.</p>
            </article>
        @endforelse
    </div>
    <style>
        .operator-board { display: grid; gap: 16px; width: min(880px, 100%); }
        .operator-row { border: 1px solid #d8d8d8; border-radius: 12px; padding: 16px; background: #fff; color: #161616; overflow-wrap: anywhere; }
        .operator-row h2, .operator-row p, .operator-row strong, .operator-note { color: #161616; -webkit-user-select: text; user-select: text; }
        .operator-row h2 { margin: 0 0 12px; font-size: 18px; }
        .operator-note { margin: 0; line-height: 1.45; }
        .operator-chat { display: grid; gap: 8px; }
        .operator-chat p { margin: 0; line-height: 1.45; }
        .operator-theirs, .operator-mine { padding: 10px 12px; border-radius: 12px; }
        .operator-theirs { background: #f5f5f5; }
        .operator-mine { background: #fff7e8; }
        .operator-theirs p, .operator-mine p { margin: 0; }
        .operator-photo { display: block; width: min(100%, 360px); margin-top: 8px; border-radius: 8px; }
        .operator-log { margin: 8px 0 0; white-space: pre-wrap; color: #161616; font: 14px/1.4 ui-monospace, monospace; }
        .operator-row a { color: #161616; }
        .operator-reply { display: grid; gap: 8px; margin-top: 12px; }
        .operator-reply textarea { width: 100%; box-sizing: border-box; border: 1px solid #d0d0d0; border-radius: 10px; padding: 12px; min-height: 96px; background: #fff; color: #161616; caret-color: #161616; -webkit-text-fill-color: #161616; }
        .operator-reply textarea::placeholder { color: #6b6b6b; -webkit-text-fill-color: #6b6b6b; opacity: 1; }
        .operator-reply button { justify-self: start; border: 0; border-radius: 8px; padding: 0 16px; background: #111; color: #fff; -webkit-text-fill-color: #fff; min-height: 48px; }
        @media (max-width: 640px) {
            .operator-board { width: 100%; }
            .operator-row { padding: 12px; }
            .operator-reply button { justify-self: stretch; }
        }
    </style>
</x-filament-panels::page>
