<?php

namespace App\Support;

/**
 * Русские подписи событий журнала администратора (admin.admin_audit).
 * Набор ключей совпадает с CHECK-ограничениями таблицы; тест следит, чтобы новые ключи не остались без перевода.
 */
final class AuditDictionary
{
    public const ACTIONS = [
        'bootstrap' => 'Назначен первый администратор',
        'login' => 'Вход в панель',
        'logout' => 'Выход из панели',
        'reauth' => 'Повторный ввод пароля',
        'community_created' => 'Создано сообщество',
        'catalog_mapped' => 'Привязана группа',
        'staff_assigned' => 'Назначен персонал',
        'staff_revoked' => 'Персонал снят',
        'join_accepted' => 'Заявка принята',
        'join_rejected' => 'Заявка отклонена',
        'account_disabled' => 'Пользователь отключён',
        'session_revoked' => 'Сеанс завершён',
        'content_moderated' => 'Материал удалён',
    ];

    public const OBJECTS = [
        'platform_admin' => 'Администратор',
        'admin_session' => 'Сеанс панели',
        'community' => 'Сообщество',
        'catalog_map' => 'Группа',
        'staff_assignment' => 'Назначение',
        'join_request' => 'Заявка',
        'account' => 'Пользователь',
        'session_family' => 'Сеанс',
        'shared_homework' => 'Задание',
        'announcement' => 'Объявление',
        'poll' => 'Опрос',
    ];

    public const OUTCOMES = [
        'success' => 'Выполнено',
        'denied' => 'Отказано',
        'conflict' => 'Конфликт',
        'invalid' => 'Неверные данные',
    ];

    public const OUTCOME_COLORS = [
        'success' => 'success',
        'denied' => 'danger',
        'conflict' => 'warning',
        'invalid' => 'danger',
    ];

    public static function action(string $key): string
    {
        return self::ACTIONS[$key] ?? 'Другое событие';
    }

    public static function object(string $key): string
    {
        return self::OBJECTS[$key] ?? 'Объект';
    }

    public static function outcome(string $key): string
    {
        return self::OUTCOMES[$key] ?? 'Неизвестно';
    }

    public static function outcomeColor(string $key): string
    {
        return self::OUTCOME_COLORS[$key] ?? 'gray';
    }
}
