# Setup and network play

Both emulator frontends use `F10` → `SETTINGS`, `NETWORK`, `MEDIA`, `RESET`.
The Amiga frontend also has `RELEASE MOUSE`. Click its image to capture the
mouse and use `Ctrl` + `Alt` + `F8` or this menu action to release it. The
initial capture click is consumed locally. This applies to the host and to
clients with a mouse grant; observers and joystick-only clients remain free.
Menus retain capture within the emulator window and show a pointer for clicking
menu entries. Closing a menu restores guest mouse control without a new click.
Focus loss releases capture, and returning focus requires a new image click.
Settings are grouped as `HARDWARE`, `ROMS`, `RAM`, `GRAPHICS`, `SOUND`, `HARD DISK`.
Amiga hardware includes an `INPUT` submenu; ROM selection, RAM/PCMCIA, presentation,
audio and disk creation/mounting each have their own page. Escape follows the complete
menu hierarchy. F10 closes the menu family. Long pages scroll with the selection.
The Amiga's dark panels, gold borders and native resolution text are retained.
The C64 groups all its existing settings in the same six categories. EasyFlash lives
under ROMs, REU under RAM. Its hard disk page explicitly says `NOT EMULATED`.

## LAN

1. Host: `F10` → `NETWORK`, `MODE = LAN`, set `SERVER PORT` and optional
   `SERVER PASSWORD`, then `SERVER = START`.
2. Client: enter the host's LAN address in `CLIENT HOST`, the matching `CLIENT PORT`
   and password in `CLIENT PASSWORD`; choose a player name and `CLIENT = JOIN`.
3. Host: select the client and grant `CLIENT RIGHT`, `KEYBOARD RIGHT`, `MOUSE RIGHT`
   or use `CONTROL OWNER` to hand over the complete guest controls.

As in C64Net, a requested player role still joins as an observer. Only the host
grants input rights. `PORT1`, `PORT2`, `BOTH` refer to the actual Amiga control
ports. A two joystick game also needs `SETTINGS` → `HARDWARE` → `INPUT` configured
for two joysticks. One controller's state goes to the granted port(s); a second
player can use the other port. All three Amiga joystick buttons are transmitted.
Full control handover grants keyboard, mouse and both ports exclusively and
suppresses local host game input. Emulator hotkeys remain available to the host.
Revocation or disconnection restores host control and releases remote input.
The host's input layout is transmitted to clients, so different locally saved
input profiles cannot turn guest keyboard keys into unintended joystick input.
Changing the host layout or input grants releases held remote keys and buttons.

The mouse has one remote owner. It uses native relative motion, independent of
each viewer's window size, aspect/stretch setting, filter and upscaler. Button
transitions are queued so a quick click cannot lose its press or release.

Clients retain their parked local Amiga and media. Joining does not replace its
ROM, RAM, disks or CPU state. Leaving resumes that machine. Graphics and sound
settings remain local; remote media/reset/savestate/hardware actions are disabled.

## Relay and the existing proxy

Set `MODE = RELAY` on both sides. Set the same `RELAY HOST`, `RELAY PORT`
(default 6465), `CONNECTION ID`, and `RELAY PASSWORD`. Host and client also set
matching server/client game passwords. The settings names and meanings match
the C64 frontend; `CLIENT HOST` is the relay host field in relay mode.

The new [relay program](../../relay_server/amiga_relay_server.py) must be deployed
at the existing proxy before an Amiga can connect. The old C64-only binary does
not understand six-byte `AMIGAR` headers. The new program also accepts the old
four-byte identity and the unchanged C64 wire protocol on the same TLS listener.
It namespaces rooms by emulator, so identical connection IDs never pair a C64
host with an Amiga client. Keep the existing proxy certificate/private key and
listen port during replacement to retain the existing clients' certificate pins.
No public proxy has been modified by this implementation.

See [relay installation and shared-proxy migration](../../relay_server/README.md).

## Security and transmission

The implementation ports the C64 project's transport rather than adding a second
network library. LAN and relay connections use TLS 1.2/1.3 with SHA-256
certificate pinning on first use. A changed pin fails closed; the frontend shows
old/new fingerprints and offers explicit acceptance and reconnect.

Relay channels additionally use the same ephemeral ECDH P-256 construction and
AES-256-GCM records as C64Net. The Amiga domain separators are `AmigaE2E1`,
`AmigaRelay:<normalized connection ID>` and `AmigaEmulator Relay E2E v1`.
Each direction has a distinct 12-byte nonce with a monotonically increasing
64-bit counter, a 16-byte authentication tag and a four-byte record length.
Unexpected nonce sequences, replay, modified ciphertext and exhausted counters
are rejected. The relay forwards opaque encrypted channel bytes. The host and
client expose matching session fingerprints in the `SECURITY` row/status.

Native, unfiltered frames are sent before any local overlays or scaling. Amiga
frames use lossless ARGB32 instead of the C64's four-bit palette indices: this
retains HAM, AGA and raster colors. Full frames, Deflate, sparse deltas and
XOR/RLE deltas follow the original C64 codec selection. Each client references
its own last transmitted frame; unchanged frames are suppressed and a full
frame is periodically sent. A joining client receives a cached full frame even
when the host image is static. Current frontend frames are 704×288.

Paula sound uses 48 kHz stereo PCM16, packed with independently predicted IMA
ADPCM channel blocks and the same raw fallback as C64Net. Local volume and
filters do not alter the transmitted native picture or shared CPU state.
AudioChunk flag bit 0 carries the host game's CIA-A LED filter state, attached
to its samples. A switch starts a new chunk; the client's local filter follows
this state rather than its suspended local Amiga. Host output filtering is never
applied to the transmitted samples. See [Audio.md](Audio.md) for filter profiles.
Senders keep only the latest unsent video frame and eight audio chunks. Input,
control, decoded size and relay receive queues are bounded; server connections
and concurrent relay key exchanges are limited to 32.

Session header (26 bytes, little endian):

| Offset | Field |
| --- | --- |
| 0 | Six ASCII bytes `AMIGAR` |
| 6 | int32 payload length |
| 10 | uint16 message type |
| 12 | uint16 flags |
| 14 | uint32 sequence |
| 18 | int64 timestamp |

The video payload adds an int32 bottom-crop value before the C64-style 20-byte
width/height/frame-ID/codec header. Permission updates carry joystick, keyboard
and mouse grants; client lists also carry the E2E fingerprint. Message type 15
transmits the host's input layout (one byte: mouse/joystick,
joystick/mouse, or dual joystick). The client's own saved layout is preserved.
Protocol version 1 identifies this Amiga schema. Relay framing is independently version 1 with
an 18-byte header: six-byte magic, version/type bytes, uint16 flags, uint32
channel ID and int32 payload length. Payloads and encrypted records are bounded
to 16 MiB.

## Verification

```powershell
dotnet run --project AmigaEmulator.Tests -c Release -p:Platform=x64 -- --filter Network
python -m unittest discover -s relay_server -v
```

Eleven network/frontend regressions cover independent wire bytes, full and delta
colors/crops, malformed allocation lengths, stereo quality/duration, GCM
tampering/replay, real LAN and Python TLS/ECDH relay sessions, two peers,
independent input grants, quick clicks, revoke/disconnect/kick, wrong passwords,
changed pins, profile persistence, menu navigation, parked client CPU state and
local HQ4x/stretch. Profiles/certificates/images are created in fresh external
directories using `AMIGAEMULATOR_USER_DATA_DIRECTORY`; real user data is untouched.
The window test also covers mismatched local input profiles, a live host-layout
change, and the first key press when a grant arrives between frontend updates.
The complete Release suite passes 265 tests with zero failures/skips.
The window test can additionally boot a supplied ROM using
`AMIGAEMULATOR_NETWORK_TEST_ROM` (read only).

Five Python regressions independently check the relay framing, size limits,
protocol switching, shared-port namespace separation, room collisions and relay
passwords. An external C64 smoke harness also verifies all six menu categories
and the unchanged C64 TLS/ECDH/AES-GCM client/server, video and joystick against
the new Amiga relay. These checks do not claim a live public-proxy deployment or
complete compatibility of every emulated machine/game.
