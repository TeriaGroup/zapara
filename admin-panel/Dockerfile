FROM php:8.3-apache

RUN apt-get update \
    && apt-get install -y --no-install-recommends libpq-dev libicu-dev libzip-dev unzip curl \
    && docker-php-ext-install pdo_pgsql intl zip opcache \
    && a2enmod rewrite \
    && rm -rf /var/lib/apt/lists/*

ENV APACHE_DOCUMENT_ROOT=/var/www/html/public
RUN sed -ri 's!/var/www/html!${APACHE_DOCUMENT_ROOT}!g' /etc/apache2/sites-available/000-default.conf \
    && printf '%s\n' \
        '<Directory /var/www/html/public>' \
        '    AllowOverride All' \
        '    Require all granted' \
        '</Directory>' \
        > /etc/apache2/conf-available/laravel.conf \
    && a2enconf laravel \
    && printf '%s\n' 'memory_limit=128M' 'expose_php=0' > /usr/local/etc/php/conf.d/zapara.ini

WORKDIR /var/www/html
COPY . /var/www/html
RUN mkdir -p storage/framework/cache/data storage/framework/sessions storage/framework/views storage/logs bootstrap/cache \
    && chown -R www-data:www-data storage bootstrap/cache \
    && chmod -R ug+rwx storage bootstrap/cache

EXPOSE 80
