{{-- Правки темы панели без сборки Vite. Подключается в AdminPanelProvider через render hook HEAD_END. --}}
<style>
    /* Телефон (ниже md): кнопки и поля не ниже 44px, подпись чекбокса — часть области нажатия. */
    @media (max-width: 767px) {
        .fi-btn,
        .fi-icon-btn,
        .fi-dropdown-list-item,
        .fi-pagination-item-btn {
            min-height: 44px;
        }

        .fi-icon-btn {
            min-width: 44px;
        }

        /* Действия-ссылки в строках таблиц: та же высота нажатия и зазор между ними. */
        .fi-ta-actions {
            gap: 8px 16px;
        }

        .fi-ta-actions .fi-link {
            min-height: 44px;
            display: inline-flex;
            align-items: center;
        }

        .fi-ac-btn-group,
        .fi-ac {
            gap: 12px;
        }

        .fi-fo-field-label:has(> .fi-checkbox-input),
        label:has(> .fi-checkbox-input) {
            min-height: 44px;
            align-items: center;
            cursor: pointer;
        }

        .fi-checkbox-input {
            width: 20px;
            height: 20px;
        }

        /* Заголовок секции (например, карточка заявки): действия уходят под текст, а не сжимают его. */
        .fi-section-header {
            flex-wrap: wrap;
            row-gap: 12px;
        }

        .fi-section-header > .fi-section-header-text-ctn,
        .fi-section-header-heading,
        .fi-section-header-description {
            min-width: 0;
            flex: 1 1 100%;
        }
    }

    /* Длинные идентификаторы — в одну строку с многоточием, полностью видны в подсказке или копировании. */
    .zp-id {
        display: inline-block;
        max-width: 100%;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        vertical-align: bottom;
        font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    }
</style>
