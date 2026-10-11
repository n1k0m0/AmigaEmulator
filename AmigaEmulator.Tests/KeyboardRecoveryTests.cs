// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.IO;
using System.Text;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests;

internal static class KeyboardRecoveryTests
{
    // About 143 ms on the supported PAL/NTSC E clocks. Each recovery clock
    // carries one logical 1 (physical KDAT low), not an eight-bit retry burst.
    private const int RecoveryPeriod = 102_000;

    public static IEnumerable<ChipTestCase> Build()
    {
        yield return new ChipTestCase("Input/Keyboard", "lost acknowledgement resynchronizes and replays the last key before its release",
            "Amiga HRM Appendix G: one recovery bit per 143 ms, then F9 and the lost code", TestLostAcknowledgement);
        yield return new ChipTestCase("Input/Keyboard", "recovery timer and partial receiver resume at the exact saved edge",
            "Snapshot must retain the receiver's partial bits, last key and acknowledgement stage", TestRecoveryRestore);
        yield return new ChipTestCase("Input/Keyboard", "power-up synchronizes before FD and FE without a lost-sync notification",
            "Amiga HRM Appendix G: startup recovery must not transmit F9", TestPowerUp);
        yield return new ChipTestCase("CIA/Serial", "external recovery bits and legacy partial bytes share the input receiver",
            "Amiga HRM: external CNT edges shift SP into SDR; pre-receiver snapshots retain their partial byte", TestReceiver);
        yield return new ChipTestCase("Input/Keyboard", "legacy blocked keyboard snapshot recovers queued releases without inventing a press",
            "Older snapshots have no saved outstanding byte; retain existing queued events after synchronization", TestLegacyRecovery);
        yield return new ChipTestCase("Input/Keyboard", "lost-sync notification can itself recover without losing the original key",
            "Every transmitted code requires acknowledgement; repeated recovery retains the original replay", TestLostNotification);
    }

    private static void TestLostAcknowledgement()
    {
        var cia = new Cia8520("CIA-A");
        var input = new AmigaInputDevice(); input.AttachCiaA(cia);
        void Tick(int count) { for (int i = 0; i < count; i++) { cia.Tick(); input.TickEClock(); } }
        void Ack() { cia.WriteByte(0x0E00, 0x40); cia.WriteByte(0x0E00, 0); }
        input.KeyboardKeyDown(0x35); input.TickEClock(); Tick(344);
        Check.Equal((byte)0x95, cia.SerialData, "The ordinary B-down byte is delivered first.");
        Check.Equal(8, cia.ReadByte(0x0D00) & 8, "The guest clears ICR but loses the KDAT handshake.");
        // There are no queued events here. The keyboard still has to recover
        // its outstanding byte instead of waiting forever for another key.
        Tick(8 * RecoveryPeriod - 1);
        Check.Equal((byte)0x95, cia.SerialData, "Seven slow recovery bits do not publish a whole character.");
        Check.Equal(0, cia.PeekByte(0x0D00) & 8, "Recovery has not completed the eighth bit early.");
        Tick(1);
        Check.Equal((byte)0, cia.SerialData, "Eight physical low bits complete the harmless release character.");
        Check.Equal(8, cia.ReadByte(0x0D00) & 8, "The receiver requests a new handshake after synchronization.");
        input.KeyboardKeyUp(0x35);
        Ack(); Tick(400);
        Check.Equal((byte)0x0C, cia.SerialData, "F9 is sent with the ordinary inverted/rotated encoding.");
        Check.Equal(8, cia.ReadByte(0x0D00) & 8, "The lost-sync notification waits for its own acknowledgement.");
        Tick(1000); Check.Equal((byte)0x0C, cia.SerialData, "The replay must not overwrite unacknowledged F9.");
        Ack(); Tick(400);
        Check.Equal((byte)0x95, cia.SerialData, "The garbled B-down is retransmitted before newer events.");
        cia.ReadByte(0x0D00); Ack(); Tick(400);
        Check.Equal((byte)0x94, cia.SerialData, "B-up follows its replayed press in order.");
        cia.ReadByte(0x0D00); Ack(); Tick(400);
        Check.Equal(0, input.QueuedKeyboardEvents, "Recovery drains the complete key sequence.");
    }

    private static void TestRecoveryRestore()
    {
        var cia = new Cia8520("CIA-A");
        var input = new AmigaInputDevice(); input.AttachCiaA(cia);
        void Tick(int count) { for (int i = 0; i < count; i++) { cia.Tick(); input.TickEClock(); } }
        void Ack() { cia.ReadByte(0x0D00); cia.WriteByte(0x0E00, 0x40); cia.WriteByte(0x0E00, 0); }
        byte[] Save()
        {
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, Encoding.UTF8, true)) { cia.SaveState(writer); input.SaveState(writer); }
            return state.ToArray();
        }
        void Load(byte[] state)
        {
            using var reader = new BinaryReader(new MemoryStream(state)); cia.LoadState(reader); input.LoadState(reader);
        }
        input.KeyboardKeyDown(0x35); input.KeyboardKeyUp(0x35); input.TickEClock(); Tick(344); cia.ReadByte(0x0D00);
        Tick(7 * RecoveryPeriod + RecoveryPeriod / 2);
        byte[] partial = Save();
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Tick(RecoveryPeriod / 2 - 1); Check.Equal(0, cia.PeekByte(0x0D00) & 8, "The restored eighth edge is not early.");
            Tick(1); Check.Equal((byte)0, cia.SerialData, "The same recovery character finishes at the saved deadline.");
            Ack(); Tick(400); Check.Equal((byte)0x0C, cia.SerialData, "F9 follows synchronization.");
            byte[] notice = Save();
            Ack(); Tick(400); Check.Equal((byte)0x95, cia.SerialData, "The pending press survives recovery.");
            Load(notice); Ack(); Tick(400); Check.Equal((byte)0x95, cia.SerialData, "Restore during F9 retains the original byte to replay.");
            Ack(); Tick(400); Check.Equal((byte)0x94, cia.SerialData, "The queued release stays after the press.");
            Ack(); Tick(400); Check.Equal(0, input.QueuedKeyboardEvents, "Restored recovery completes.");
            Load(partial);
        }
    }

    private static void TestPowerUp()
    {
        var cia = new Cia8520("CIA-A");
        var input = new AmigaInputDevice(); input.AttachCiaA(cia); input.Reset();
        void Tick(int count) { for (int i = 0; i < count; i++) { cia.Tick(); input.TickEClock(); } }
        void Ack() { cia.ReadByte(0x0D00); cia.WriteByte(0x0E00, 0x40); cia.WriteByte(0x0E00, 0); }
        Tick(7 * RecoveryPeriod);
        Check.Equal(0, cia.PeekByte(0x0D00) & 8, "Seven startup bits have not completed synchronization.");
        Tick(1); Check.Equal(8, cia.PeekByte(0x0D00) & 8, "The eighth slow bit requests acknowledgement.");
        Ack(); Tick(400); Check.Equal((byte)4, cia.SerialData, "FD starts the power-up key stream directly, without F9.");
        Ack(); Tick(400); Check.Equal((byte)2, cia.SerialData, "FE terminates the startup stream.");
        Ack(); Tick(400); Check.Equal(0, input.QueuedKeyboardEvents, "Startup completes after both special codes.");
    }

    private static void TestReceiver()
    {
        var cia = new Cia8520("CIA-A");
        cia.ClockSerialInputBit(true); cia.ClockSerialInputBit(false); cia.ClockSerialInputBit(true);
        using var state = new MemoryStream();
        using (var writer = new BinaryWriter(state, Encoding.UTF8, true)) cia.SaveState(writer);
        for (int repeat = 0; repeat < 2; repeat++)
        {
            for (int i = 0; i < 4; i++) cia.ClockSerialInputBit(false);
            Check.Equal(0, cia.PeekByte(0x0D00) & 8, "Seven external bits do not raise SDR's interrupt.");
            cia.ClockSerialInputBit(false); Check.Equal((byte)0xA0, cia.SerialData, "Recovery preserves the already shifted prefix.");
            Check.Equal(8, cia.ReadByte(0x0D00) & 8, "Only the eighth edge publishes the character.");
            state.Position = 0; using var reader = new BinaryReader(state, Encoding.UTF8, true); cia.LoadState(reader);
        }
        cia.WriteByte(0x0E00, 0x40);
        for (int i = 0; i < 16; i++) cia.ClockSerialInputBit(false);
        Check.Equal(0, cia.PeekByte(0x0D00) & 8, "External input edges are ignored in output mode.");
        cia.WriteByte(0x0E00, 0); cia.QueueSerialInputByte(0x95);
        for (int i = 0; i < 3; i++) cia.Tick();
        state.SetLength(0); state.Position = 0;
        using (var writer = new BinaryWriter(state, Encoding.UTF8, true))
            StateSerializer.WriteObjectFields(writer, cia, "_serialInputReceiver", "_serialInputReceivedBits", "_serialInputReceiverStateSaved");
        for (int i = 0; i < 5; i++) cia.Tick(); cia.ReadByte(0x0D00);
        state.Position = 0; using (var reader = new BinaryReader(state, Encoding.UTF8, true)) cia.LoadState(reader);
        for (int i = 0; i < 4; i++) cia.Tick();
        Check.Equal(0, cia.PeekByte(0x0D00) & 8, "A legacy partial byte has the original five edges left.");
        cia.Tick(); Check.Equal((byte)0x95, cia.SerialData, "The legacy receiver prefix is reconstructed correctly.");
    }

    private static void TestLegacyRecovery()
    {
        var cia = new Cia8520("CIA-A");
        var input = new AmigaInputDevice(); input.AttachCiaA(cia);
        void Tick(int count) { for (int i = 0; i < count; i++) { cia.Tick(); input.TickEClock(); } }
        void Ack() { cia.ReadByte(0x0D00); cia.WriteByte(0x0E00, 0x40); cia.WriteByte(0x0E00, 0); }
        input.KeyboardKeyDown(0x35); input.KeyboardKeyUp(0x35); input.TickEClock(); Tick(344); cia.ReadByte(0x0D00);
        using var state = new MemoryStream();
        using (var writer = new BinaryWriter(state, Encoding.UTF8, true))
        {
            cia.SaveState(writer);
            StateSerializer.WriteObjectFields(writer, input, "_ciaA", "_keyboardHandshakeTimeout", "_keyboardLastByte",
                "_keyboardLastByteValid", "_keyboardResynchronizing", "_keyboardNotifyLostSync", "_keyboardLostSyncPending", "_keyboardSendingLostSync");
        }
        // Loading into a used instance must also clear new recovery fields.
        Tick(8 * RecoveryPeriod); Ack(); Tick(400);
        state.Position = 0; using (var reader = new BinaryReader(state, Encoding.UTF8, true)) { cia.LoadState(reader); input.LoadState(reader); }
        Tick(8 * RecoveryPeriod); Check.Equal((byte)0, cia.SerialData, "A legacy blocked stream resumes synchronization.");
        Ack(); Tick(400); Check.Equal((byte)0x0C, cia.SerialData, "The legacy recovery announces lost sync.");
        Ack(); Tick(400); Check.Equal((byte)0x94, cia.SerialData, "The known queued release resumes; no unavailable old press is invented.");
        Ack(); Tick(400); Check.Equal(0, input.QueuedKeyboardEvents, "The migrated queue drains.");
    }

    private static void TestLostNotification()
    {
        var cia = new Cia8520("CIA-A");
        var input = new AmigaInputDevice(); input.AttachCiaA(cia);
        void Tick(int count) { for (int i = 0; i < count; i++) { cia.Tick(); input.TickEClock(); } }
        void Ack() { cia.ReadByte(0x0D00); cia.WriteByte(0x0E00, 0x40); cia.WriteByte(0x0E00, 0); }
        input.KeyboardKeyDown(0x35); input.TickEClock(); Tick(344); cia.ReadByte(0x0D00);
        Tick(8 * RecoveryPeriod); Ack(); Tick(400);
        Check.Equal((byte)0x0C, cia.SerialData, "The first F9 arrives.");
        cia.ReadByte(0x0D00); // Lose its KDAT handshake, too.
        Tick(8 * RecoveryPeriod); Check.Equal((byte)0, cia.SerialData, "Another slow synchronization recovers the notification.");
        Ack(); Tick(400); Check.Equal((byte)0x0C, cia.SerialData, "Recovery announces the fault again.");
        Ack(); Tick(400); Check.Equal((byte)0x95, cia.SerialData, "The original key, rather than F9, is still the replay byte.");
        Ack(); Tick(2 * RecoveryPeriod);
        Check.Equal(0, cia.PeekByte(0x0D00) & 8, "An acknowledged final key does not cause idle recovery interrupts.");
    }
}
