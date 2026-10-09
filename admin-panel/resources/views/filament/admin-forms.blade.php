{{-- Формы админки (#25): контраст границ полей. Подключается в AdminPanelProvider через render hook HEAD_END. --}}
<style>
    /*
     * TODO(#11): временная переменная. Заменить на токен border/control из tokens.json, когда #11 появится в develop.
     * #8C8C8C: 3,36:1 к белому полю и 3,22:1 к фону страницы #FAFAFA; тёмная тема #7A7A7A: ≥3,6:1 к фону поля и секции.
     */
    :root {
        --zp-border-control-tmp: #8c8c8c;
    }

    .dark {
        --zp-border-control-tmp: #7a7a7a;
    }

    /* Обычное состояние; фокус и ошибка остаются цветами Filament. */
    .fi-input-wrp:not(.fi-invalid):not(:focus-within):not(.fi-disabled),
    .fi-checkbox-input:not(:checked):not(.fi-invalid),
    .fi-radio-input:not(:checked):not(.fi-invalid) {
        --tw-ring-color: var(--zp-border-control-tmp);
    }

</style>
