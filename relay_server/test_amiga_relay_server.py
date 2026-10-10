# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Framing and shared-proxy regressions. Run: python -m unittest discover -s relay_server."""
import asyncio
import struct
import unittest

import amiga_relay_server as relay


class WireTests(unittest.IsolatedAsyncioTestCase):
    async def test_amigar_header_matches_independent_bytes(self):
        # Six ASCII bytes followed by version, type, flags, channel, payload length.
        wire = b"AMIGAR\x01\x05\x00\x00\x07\x00\x00\x00\x03\x00\x00\x00abc"
        reader = asyncio.StreamReader()
        reader.feed_data(wire)
        reader.feed_eof()
        frame = await relay.read_frame(reader)
        self.assertEqual((frame.frame_type, frame.channel_id, frame.payload), (5, 7, b"abc"))
        self.assertEqual(reader.wire_magic, b"AMIGAR")

    async def test_legacy_header_and_mid_session_protocol_change(self):
        reader = asyncio.StreamReader()
        reader.feed_data(b"C64R" + struct.pack("<BBHIi", 1, 7, 0, 0, 0))
        reader.feed_data(b"AMIGAR" + struct.pack("<BBHIi", 1, 7, 0, 0, 0))
        reader.feed_eof()
        self.assertEqual((await relay.read_frame(reader)).frame_type, relay.PING)
        with self.assertRaises(ValueError):
            await relay.read_frame(reader)

    async def test_oversized_payload_rejected_before_read(self):
        reader = asyncio.StreamReader()
        reader.feed_data(b"AMIGAR" + struct.pack("<BBHIi", 1, 5, 0, 1, relay.MAX_PAYLOAD + 1))
        reader.feed_eof()
        with self.assertRaises(ValueError):
            await relay.read_frame(reader)

    async def test_clean_eof_and_truncated_frames_are_distinct(self):
        for wire in (b"A", b"AMIGAR\x01", b"AMIGAR" + struct.pack("<BBHIi", 1, 5, 0, 1, 3) + b"ab"):
            reader = asyncio.StreamReader()
            reader.feed_data(wire)
            reader.feed_eof()
            with self.assertRaises(ValueError):
                await relay.read_frame(reader)
        reader = asyncio.StreamReader()
        reader.feed_eof()
        self.assertIsNone(await relay.read_frame(reader))

    async def test_shared_proxy_keeps_rooms_separate_and_forwards_opaque_data(self):
        relay.sessions.clear()
        listener = await asyncio.start_server(lambda r, w: relay.handle_connection(r, w, "proxy-secret"), "127.0.0.1", 0)
        port = listener.sockets[0].getsockname()[1]
        peers = []

        async def join(magic, role, password="proxy-secret"):
            reader, writer = await asyncio.open_connection("127.0.0.1", port)
            writer.wire_magic = magic
            payload = struct.pack("<iB", 1, role) + relay.write_string("same-room") + relay.write_string(password)
            await relay.send_frame(writer, relay.REGISTER, 0, payload)
            response = await asyncio.wait_for(relay.read_frame(reader), 2)
            peers.append(writer)
            return reader, writer, response

        try:
            ar, aw, response = await join(relay.MAGIC, relay.ROLE_SERVER)
            self.assertEqual(response.frame_type, relay.REGISTER_OK)
            cr, cw, response = await join(relay.LEGACY_MAGIC, relay.ROLE_SERVER)
            self.assertEqual(response.frame_type, relay.REGISTER_OK)
            self.assertEqual(set(relay.sessions), {"amiga:same-room", "legacy:same-room"})
            acr, acw, response = await join(relay.MAGIC, relay.ROLE_CLIENT)
            self.assertEqual(response.frame_type, relay.REGISTER_OK)
            opened = await asyncio.wait_for(relay.read_frame(ar), 2)
            self.assertEqual(opened.frame_type, relay.CHANNEL_OPEN)
            ccr, ccw, response = await join(relay.LEGACY_MAGIC, relay.ROLE_CLIENT)
            self.assertEqual(response.frame_type, relay.REGISTER_OK)
            legacy_open = await asyncio.wait_for(relay.read_frame(cr), 2)
            self.assertEqual(legacy_open.frame_type, relay.CHANNEL_OPEN)
            await relay.send_frame(acw, relay.CHANNEL_DATA, 0, b"encrypted-amiga-record")
            received = await asyncio.wait_for(relay.read_frame(ar), 2)
            self.assertEqual(received.payload, b"encrypted-amiga-record")
            await relay.send_frame(cw, relay.CHANNEL_DATA, legacy_open.channel_id, b"encrypted-legacy-record")
            received = await asyncio.wait_for(relay.read_frame(ccr), 2)
            self.assertEqual(received.payload, b"encrypted-legacy-record")
            _, _, rejected = await join(relay.MAGIC, relay.ROLE_SERVER)
            self.assertEqual(rejected.frame_type, relay.REGISTER_REJECT)
            _, _, rejected = await join(relay.MAGIC, relay.ROLE_CLIENT, "wrong-secret")
            self.assertEqual(rejected.frame_type, relay.REGISTER_REJECT)
            self.assertEqual(len(relay.sessions), 2)
        finally:
            for writer in peers:
                await relay.close_writer(writer)
            listener.close()
            await listener.wait_closed()
            await asyncio.sleep(0.05)
            self.assertFalse(relay.sessions)


if __name__ == "__main__":
    unittest.main()
