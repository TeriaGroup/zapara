<?php

// Переопределения перевода Filament (#25). Общий плейсхолдер — запасной: у селектов админки свои, конкретные.
return [
    'select' => [
        'actions' => [
            'create_option' => [
                'modal' => [
                    'actions' => [
                        'create_another' => [
                            'label' => 'Создать и добавить ещё',
                        ],
                    ],
                ],
            ],
        ],
        'placeholder' => 'Выберите значение',
    ],
];
