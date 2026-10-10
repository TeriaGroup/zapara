{{-- Формы админки (#25): контраст границ полей. Подключается в AdminPanelProvider через render hook HEAD_END. --}}
<style>
    /*
     * Цвет границы — токен border-control из design/tokens.json (#11): переменную --zp-border-control
     * подключает design-tokens.blade.php, она своя для светлой и тёмной темы.
     */
    .fi-input-wrp:not(.fi-invalid):not(:focus-within):not(.fi-disabled),
    .fi-checkbox-input:not(:checked):not(.fi-invalid),
    .fi-radio-input:not(:checked):not(.fi-invalid) {
        --tw-ring-color: var(--zp-border-control);
    }

</style>
