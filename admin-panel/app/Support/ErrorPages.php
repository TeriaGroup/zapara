<?php

namespace App\Support;

use Illuminate\Http\Request;

/**
 * #149: ссылки страниц ошибок. Только запрос, без сессии: страница 500 должна открываться и тогда,
 * когда упала сама сессия (StartSession), поэтому url()->previous() здесь нельзя.
 */
final class ErrorPages
{
    /** Куда вернуться: Referer того же происхождения (после POST — страница с формой), иначе инфопанель. */
    public static function backUrl(?Request $request = null): string
    {
        $request ??= request();
        $home = url('/admin');
        $referer = (string) $request->headers->get('referer', '');
        if ($referer === '') {
            return $home;
        }
        $parts = parse_url($referer);
        if ($parts === false || ! isset($parts['scheme'], $parts['host'])) {
            return $home;
        }
        $origin = strtolower($parts['scheme']).'://'.strtolower($parts['host']).(isset($parts['port']) ? ':'.$parts['port'] : '');
        $own = strtolower($request->getSchemeAndHttpHost());

        return $origin === $own ? $referer : $home;
    }

    /** Повторить тот же запрос: текущий адрес вместе со строкой запроса. */
    public static function retryUrl(?Request $request = null): string
    {
        return ($request ?? request())->fullUrl();
    }
}
