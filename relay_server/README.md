# Amiga Emulator Relay Server

This directory contains the optional public relay for Amiga Emulator Relay Mode.
It is not part of the Windows installer.

## Run

```bash
python3 amiga_relay_server.py --host 0.0.0.0 --port 6465
```

To restrict the relay to clients that know a shared relay password:

```bash
python3 amiga_relay_server.py --host 0.0.0.0 --port 6465 --password "change-me"
```

The script uses TLS. If `relay.crt` and `relay.key` do not exist, it creates a
self-signed certificate with Python `cryptography` when available, or falls back
to `openssl`.

## Ubuntu systemd service

Install and start the relay as a system service:

```bash
cd relay_server
bash install_service.sh
```

The installer creates:

```text
/opt/amiga-relay-server/amiga_relay_server.py
/etc/amiga-relay-server/amiga-relay-server.env
/etc/systemd/system/amiga-relay-server.service
/var/log/amiga-relay-server/relay.log
```

Change host, port, certificate path, key path, certificate common name, or relay
password in:

```bash
sudo nano /etc/amiga-relay-server/amiga-relay-server.env
sudo systemctl restart amiga-relay-server
```

Set `AMIGA_RELAY_PASSWORD=` to an empty value to leave the relay open. Set a value
to require the same `RELAY PASSWORD` in the emulator network menu before a host
or client can register with the relay. When a relay password is configured, the
relay tracks wrong passwords by remote IP address. After three wrong passwords,
that IP is ignored for 30 minutes. This throttle is in-memory and is cleared when
the relay process restarts.

Useful service commands:

```bash
sudo systemctl status amiga-relay-server
sudo systemctl restart amiga-relay-server
sudo systemctl stop amiga-relay-server
sudo systemctl start amiga-relay-server
sudo journalctl -u amiga-relay-server -f
sudo tail -f /var/log/amiga-relay-server/relay.log
```

Remove the service while keeping certificates/config/logs:

```bash
bash remove_service.sh
```

Remove everything, including certificates/config/logs and the service user:

```bash
bash remove_service.sh --purge
```

For a domain install, point the emulator's Relay Host to your domain and Relay
Port to the exposed TLS port. The emulator uses trust-on-first-use certificate
pinning for the relay certificate and still encrypts each Amiga session end to end
between the host emulator and the client emulator.

## Routing

Each Amiga host registers one `Connection ID`. Clients that use the same
`Connection ID` are bridged to that host. A second host trying to register the
same id is rejected until the first host disconnects.

## Share the existing C64 proxy

This server accepts the new six-byte `AMIGAR` identity (18-byte relay header)
and the existing four-byte C64 identity (16-byte relay header) on one TLS port.
Each connection retains its selected framing. Amiga rooms and legacy rooms use
separate namespaces, so the same connection ID can be used by both emulators
without connecting them to each other. Existing C64 clients require no update.
Channel bytes remain opaque: each emulator performs its own ECDH P-256 exchange
and AES-256-GCM encryption through the relay.

To replace an existing C64 proxy, stop its old relay process, replace the Python
program or update its service `ExecStart` to this program, and retain the current
listen address, port, password, certificate and key arguments. In particular,
retain `--cert` and `--key` paths so existing clients keep the same TLS pin.
The command-line options are compatible with the old relay. The provided new
service uses `AMIGA_RELAY_*` environment variables; transfer existing values
when choosing that service instead of the old unit. Run one relay process per
listen port and point both frontends to that same endpoint.

The installer creates a separate Amiga service; it does not modify or stop an
existing C64 service. No public proxy is deployed by building the emulator.

## Verification

```bash
python3 -m unittest discover -s relay_server -v
```

Run this from the repository root. The five tests check independently constructed
header bytes, payload limits, framing changes, room separation, duplicate hosts,
relay passwords, opaque forwarding and disconnect cleanup. The .NET network
tests additionally start this actual program with a fresh certificate and verify
TLS, ECDH/AES-GCM, native video, stereo audio and remote input. See
[`NetworkPlay.md`](../AmigaEmulator/Docs/NetworkPlay.md) for the session protocol
and frontend controls.
