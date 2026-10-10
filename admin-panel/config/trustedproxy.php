<?php

/*
 * Proxies allowed to set X-Forwarded-For / X-Forwarded-Proto (read by Laravel's TrustProxies middleware).
 * TRUSTED_PROXIES: comma-separated addresses or CIDRs of the reverse proxy, e.g. the Docker network of Caddy
 * ("172.18.0.0/16"). Unset: loopback and private ranges, where Docker bridge networks live. Never use "*" here:
 * any client that reaches the container directly could then choose its own address.
 */
return [
    'proxies' => array_values(array_filter(array_map('trim', explode(',', (string) env(
        'TRUSTED_PROXIES',
        '127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7',
    ))))),
];
