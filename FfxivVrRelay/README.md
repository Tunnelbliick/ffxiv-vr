# FFXIV VR pose sync relay

A small UDP relay that forwards VR pose packets between FFXIV VR plugin users in the same zone instance. The plugin's default server is `vr.tnnlb.dev:7850`.

## What it does

- Clients send a keepalive every second and pose packets about 30 times a second.
- The relay answers keepalives with a token derived from the client's IP and port (HMAC with a random secret made at startup). Pose packets are only forwarded when they carry the sender's own token, so spoofed source addresses can't be used to flood someone.
- Packets are forwarded only to clients in the same room. A room is a hash of world, zone, instance and housing ward; player ids are hashes of the character's content id salted with the room. The relay never sees raw content ids or names.
- Limits: 512-byte packets, 60 packets per second per client, 64 clients per room, 5000 clients total, clients dropped after 15 seconds of silence.
- Nothing is written to disk. Once a minute it logs the client and room counts.

## Run with Docker

From the repository root:

```sh
docker build -t ffxivvr-relay FfxivVrRelay
docker run -d --name ffxivvr-relay --restart unless-stopped -p 7850:7850/udp ffxivvr-relay
```

Or with compose:

```yaml
services:
  relay:
    build: ./FfxivVrRelay
    restart: unless-stopped
    ports:
      - "7850:7850/udp"
```

## Run without Docker

```sh
dotnet publish FfxivVrRelay -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o /opt/ffxivvr-relay
```

`/etc/systemd/system/ffxivvr-relay.service`:

```ini
[Unit]
Description=FFXIV VR pose sync relay
After=network-online.target

[Service]
ExecStart=/opt/ffxivvr-relay/FfxivVrRelay 7850
DynamicUser=yes
Restart=always

[Install]
WantedBy=multi-user.target
```

```sh
systemctl enable --now ffxivvr-relay
```

## Checklist

- Open UDP port 7850 in the server firewall (`ufw allow 7850/udp`) and any provider firewall.
- Point the DNS record for the relay host at the server. It must not go through an HTTP proxy such as Cloudflare's orange cloud, which doesn't carry UDP.
- The port is the first argument or the `RELAY_PORT` environment variable, default 7850. If you change it, players enter `host:port` in the plugin's Sync tab.
