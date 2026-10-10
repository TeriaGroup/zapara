# maddy runs as an unprivileged user; low SMTP/IMAP ports work through the compose sysctl.
# /data/tls is written by certsync (uid 10001) and only read here.
FROM foxcpp/maddy:0.7.1
RUN addgroup -S -g 10002 maddy \
    && adduser -S -D -H -u 10002 -G maddy -s /sbin/nologin maddy \
    && mkdir -p /data/tls \
    && chown -R maddy:maddy /data \
    && chown 10001:10001 /data/tls
USER maddy
