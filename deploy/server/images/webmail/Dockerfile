# Roundcube runs as www-data (uid 33) instead of starting Apache as root.
FROM roundcube/roundcubemail:1.6.x-apache
RUN mkdir -p /var/roundcube/db \
    && chown www-data:www-data /var/roundcube/db
USER www-data
