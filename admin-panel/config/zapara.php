<?php

return [
    'schemas' => [
        'accounts' => env('ZAPARA_ACCOUNTS_SCHEMA', 'accounts'),
        'admin' => env('ZAPARA_ADMIN_SCHEMA', 'admin'),
        'communities' => env('ZAPARA_COMMUNITIES_SCHEMA', 'communities'),
        'settings' => env('ZAPARA_SETTINGS_SCHEMA', 'operator'),
    ],

    // TOTP for panel administrators. Off: admins may enroll from the «Безопасность» page and are then asked
    // for a code at login. On: an admin without TOTP is sent to enrollment right after the password step.
    // Turn it on only after every admin has enrolled (see admin-panel/README.md).
    'admin_mfa_required' => (bool) env('ZAPARA_ADMIN_MFA_REQUIRED', false),
];
