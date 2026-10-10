<?php

namespace App\Support;

use App\Filament\Pages\Content;
use App\Filament\Pages\Memberships;
use App\Filament\Pages\Sessions;
use App\Filament\Resources\AccountUsers\AccountUserResource;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Route;
use Illuminate\Support\Str;

/**
 * Человеческое название и ссылка для объекта из журнала аудита.
 */
final class AuditObjects
{
    /** @var array<string, array{label: string, url: ?string, note: string}> */
    private array $cache = [];

    /**
     * @return array{label: string, url: ?string, note: string}
     */
    public function describe(string $type, string $id): array
    {
        return $this->cache[$type.':'.$id] ??= $this->resolve($type, $id);
    }

    /**
     * @return array{label: string, url: ?string, note: string}
     */
    private function resolve(string $type, string $id): array
    {
        $fallback = ['label' => AuditDictionary::object($type), 'url' => null, 'note' => 'Объекта уже нет'];
        $found = fn (string $label, ?string $url): array => ['label' => $label, 'url' => $url, 'note' => AuditDictionary::object($type)];
        if (! Str::isUuid($id)) {
            return $fallback;
        }
        $communities = Zapara::communities();
        $accounts = Zapara::accounts();
        try {
            switch ($type) {
                case 'community':
                    $name = DB::table($communities.'.communities')->where('community_id', $id)->value('name');

                    return $name === null ? $fallback : $found('«'.$name.'»', $this->communityUrl($id));
                case 'catalog_map':
                    $row = DB::table($communities.'.catalog_maps as m')
                        ->join($communities.'.communities as c', 'c.community_id', '=', 'm.community_id')
                        ->where('m.map_id', $id)
                        ->first(['m.group_id', 'c.name', 'c.community_id']);

                    return $row === null ? $fallback : $found($row->group_id.' → «'.$row->name.'»', $this->communityUrl((string) $row->community_id));
                case 'staff_assignment':
                    $row = DB::table($communities.'.staff_assignments as s')
                        ->join($communities.'.communities as c', 'c.community_id', '=', 's.community_id')
                        ->leftJoin($accounts.'.users as u', 'u.user_id', '=', 's.user_id')
                        ->where('s.assignment_id', $id)
                        ->first(['u.username', 'c.name', 'c.community_id']);

                    return $row === null ? $fallback : $found(($row->username ?? 'удалённый пользователь').' в «'.$row->name.'»', $this->communityUrl((string) $row->community_id));
                case 'join_request':
                    $row = DB::table($communities.'.join_requests as r')
                        ->join($communities.'.communities as c', 'c.community_id', '=', 'r.community_id')
                        ->leftJoin($accounts.'.users as u', 'u.user_id', '=', 'r.user_id')
                        ->where('r.request_id', $id)
                        ->first(['u.username', 'c.name']);

                    return $row === null ? $fallback : $found(($row->username ?? 'удалённый пользователь').' → «'.$row->name.'»', Memberships::getUrl());
                case 'account':
                    $username = DB::table($accounts.'.users')->where('user_id', $id)->value('username');

                    return $username === null ? $fallback : $found((string) $username, AccountUserResource::getUrl('edit', ['record' => $id]));
                case 'session_family':
                    $row = DB::table($accounts.'.session_families as f')
                        ->leftJoin($accounts.'.users as u', 'u.user_id', '=', 'f.user_id')
                        ->where('f.family_id', $id)
                        ->first(['u.username', 'f.device_name']);

                    return $row === null ? $fallback : $found(trim(($row->username ?? '').' · '.($row->device_name ?? ''), ' ·'), Sessions::getUrl());
                case 'shared_homework':
                case 'announcement':
                case 'poll':
                    return ['label' => AuditDictionary::object($type), 'url' => Content::getUrl(), 'note' => 'Удалено модератором'];
                default:
                    return $fallback;
            }
        } catch (\Throwable) {
            return $fallback;
        }
    }

    private function communityUrl(string $id): string
    {
        // Карточка сообщества появляется вместе с ресурсом «Сообщества» (#24); до этого ведём на список.
        if (Route::has('filament.admin.resources.communities.view')) {
            return route('filament.admin.resources.communities.view', ['record' => $id]);
        }

        return url('/admin/communities');
    }
}
