<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use Filament\Pages\Page;
use App\Support\SupportBridge;
use Illuminate\Support\Facades\DB;

class SupportDesk extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Поддержка';

    protected static ?string $title = 'Поддержка';

    protected static ?string $slug = 'support';

    protected static ?int $navigationSort = 14;

    protected string $view = 'filament.pages.support';

    public string $reply = '';

    public string $thread = '';

    public function threads(): array
    {
        $this->ensure();
        $schema = \App\Support\Zapara::settings();
        $rows = DB::select("select t.thread_id, t.subject, m.author, m.body, m.created_at from {$schema}.support_threads t join {$schema}.support_messages m on m.thread_id = t.thread_id order by t.created_at, m.created_at");
        $threads = [];
        foreach ($rows as $row) {
            $id = (string) $row->thread_id;
            $threads[$id]['id'] = $id;
            $threads[$id]['subject'] = (string) $row->subject;
            $threads[$id]['messages'][] = ['author' => (string) $row->author, 'body' => (string) $row->body];
        }

        return array_values($threads);
    }

    public function send(string $threadId): void
    {
        $body = trim($this->reply);
        if ($threadId === '' || $body === '') {
            return;
        }
        SupportBridge::reply($threadId, $body);
        $this->reply = '';
    }

    private function ensure(): void
    {
        $schema = \App\Support\Zapara::settings();
        DB::statement('CREATE SCHEMA IF NOT EXISTS '.$schema);
        DB::statement("CREATE TABLE IF NOT EXISTS {$schema}.support_threads (thread_id uuid PRIMARY KEY, user_id uuid NOT NULL, subject text NOT NULL, created_at timestamptz NOT NULL)");
        DB::statement("CREATE TABLE IF NOT EXISTS {$schema}.support_messages (message_id uuid PRIMARY KEY, thread_id uuid NOT NULL, author text NOT NULL, body text NOT NULL, created_at timestamptz NOT NULL)");
    }
}
