<?php

namespace App\Http\Controllers;

use App\Support\Zapara;
use Illuminate\Support\Facades\DB;
use Symfony\Component\HttpFoundation\Response;

class SupportFileController
{
    public function show(string $id): Response
    {
        if (! preg_match('/\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z/i', $id)) {
            abort(404);
        }
        $schema = Zapara::settings();
        $row = DB::selectOne(
            "select file_name, content_type, kind, encode(payload, 'base64') as payload from {$schema}.support_attachments where attachment_id = ?::uuid",
            [$id]
        );
        if (! is_object($row) || ! is_string($row->payload) || $row->payload === '') {
            abort(404);
        }
        $bytes = base64_decode($row->payload, true);
        if ($bytes === false) {
            abort(404);
        }
        $name = str_replace(["\r", "\n", '"'], '', (string) $row->file_name);
        $disposition = (string) $row->kind === 'photo' ? 'inline' : 'attachment';

        return response($bytes, 200, [
            'Content-Type' => (string) $row->content_type,
            'Content-Disposition' => $disposition.'; filename*=UTF-8\'\''.rawurlencode($name),
            'Cache-Control' => 'private, no-store',
            'X-Content-Type-Options' => 'nosniff',
        ]);
    }
}
