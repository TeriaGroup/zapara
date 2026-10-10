# Caddy and certsync run as an unprivileged user. Ports 80/443 are bound through the container sysctl
# net.ipv4.ip_unprivileged_port_start=0 (set in compose.yml). The upstream binary carries a file
# capability (cap_net_bind_service), which cannot be exec'd once compose drops all capabilities,
# so it is removed here.
FROM caddy:2-alpine
RUN apk add --no-cache --virtual .setcap libcap \
    && setcap -r /usr/bin/caddy \
    && apk del .setcap \
    && addgroup -S -g 10001 caddy \
    && adduser -S -D -H -u 10001 -G caddy -s /sbin/nologin caddy \
    && mkdir -p /data /config /certs \
    && chown -R caddy:caddy /data /config /certs
USER caddy
