{{-- Формы админки (#25): контраст границ полей. Подключается в AdminPanelProvider через render hook HEAD_END. --}}
<style>
    /*
     * Цвет границы — токен border-control из design/tokens.json (#11): переменную --zp-border-control
     * подключает design-tokens.blade.php, она своя для светлой и тёмной темы.
     * Запасной #8C8C8C — пока #11 не в develop: 3,36:1 к белому полю, 3,22:1 к фону #FAFAFA, ≥4,8:1 к тёмным фонам.
     */
    .fi-input-wrp:not(.fi-invalid):not(:focus-within):not(.fi-disabled),
    .fi-checkbox-input:not(:checked):not(.fi-invalid),
    .fi-radio-input:not(:checked):not(.fi-invalid) {
        --tw-ring-color: var(--zp-border-control, #8c8c8c);
    }

</style>
