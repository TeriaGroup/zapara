<?php

return [
    'schemas' => [
        'accounts' => env('ZAPARA_ACCOUNTS_SCHEMA', 'accounts'),
        'admin' => env('ZAPARA_ADMIN_SCHEMA', 'admin'),
        'communities' => env('ZAPARA_COMMUNITIES_SCHEMA', 'communities'),
        'settings' => env('ZAPARA_SETTINGS_SCHEMA', 'operator'),
    ],
];
