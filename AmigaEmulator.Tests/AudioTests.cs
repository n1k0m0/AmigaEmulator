using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Audio;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests;

internal static class AudioTests
{
    public static IEnumerable<ChipTestCase> Build()
    {
        yield return Test("OFF preserves both channels exactly", Bypass);
        yield return Test("A500 preserves music detail with the LED off", FixedResponse);
        yield return Test("hardware profiles follow LED while LED mode forces filtering", LedResponse);
        yield return Test("cutoffs remain stable at different host sample rates", SampleRates);
        yield return Test("filter channels are isolated and reset clears their history", ResetAndStereo);
        yield return Test("LED switching uses warm filters without a silence transient", WarmLed);
        yield return Test("CIA-A LED respects direction, active-low output and restore", CiaLed);
        yield return Test("all four filter selections round-trip independently", FilterSelections);
        yield return Test("manual AUDDAT prefetch preserves the playing low byte", ManualLatches);
        yield return Test("DMA prefetch preserves every signed high and low byte", DmaLatches);
        yield return Test("AUDPER zero holds a sample for 65536 chip clocks", ZeroPeriod);
        yield return Test("PAL and NTSC each produce 48000 frames per second", ClockRates);
        yield return Test("48 kHz conversion integrates fast DAC transitions", Integration);
        yield return Test("mid-sample restore preserves buffered words and exact audio", Restore);
        yield return Test("legacy restore discards a running machine's integration history", LegacyRestore);
    }
    private static ChipTestCase Test(string name, Action run) => new("Paula/Audio", name, "Paula DAC latches; HRM CIA-A /LED; independently measured stereo/filter response", run);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double expected, double actual, double tolerance, string message) => Require(Math.Abs(expected - actual) <= tolerance, message + $" Expected {expected}, actual {actual}.");

    private static double Gain(AmigaPaulaFilter mode, double frequency, bool led = false, int rate = 48000)
    {
        var filter = new PaulaOutputFilter(rate) { Mode = mode };
        double input = 0, output = 0;
        for (int i = 0; i < rate / 4; i++)
        {
            float left = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / rate)), right = 0;
            float original = left;
            filter.Process(ref left, ref right, led);
            Require(right == 0 && float.IsFinite(left), "No cross-talk or unstable filter.");
            if (i > rate / 20) { input += (double)original * original; output += (double)left * left; }
        }
        return Math.Sqrt(output / input);
    }
    private static void Bypass()
    {
        var filter = new PaulaOutputFilter { Mode = AmigaPaulaFilter.Off };
        for (int i = 0; i < 1000; i++)
        {
            float l = (float)Math.Sin(i * .31), r = (float)Math.Cos(i * .14), expectedL = l, expectedR = r;
            filter.Process(ref l, ref r, (i & 1) == 0);
            Require(l == expectedL && r == expectedR, "OFF must bypass filtering, including the LED.");
        }
    }
    private static void FixedResponse()
    {
        Require(Gain(AmigaPaulaFilter.A500, 1000) > .98, "Music fundamentals must retain their level.");
        Require(Gain(AmigaPaulaFilter.A500, 4000) > .84, "4 kHz detail must survive instead of the old ~18 dB attenuation.");
        Near(Math.Sqrt(.5), Gain(AmigaPaulaFilter.A500, 6200), .012, "First fixed cutoff is around 6.2 kHz.");
        Require(Gain(AmigaPaulaFilter.A500, 12000) < .42, "The fixed stages must still attenuate high frequencies.");
    }
    private static void LedResponse()
    {
        Near(1, Gain(AmigaPaulaFilter.A1200, 7000), 1e-6, "A1200 bypasses fixed stages.");
        Near(Math.Pow(.5, 1.5), Gain(AmigaPaulaFilter.A1200, 7000, true), .001, "Three LED poles at 7 kHz.");
        Near(Gain(AmigaPaulaFilter.A500, 4000, true), Gain(AmigaPaulaFilter.Led, 4000), 1e-6, "Forced LED remains on when software bypasses it.");
        Require(Gain(AmigaPaulaFilter.A500, 4000, true) < Gain(AmigaPaulaFilter.A500, 4000) * .7, "Software LED selects an additional filter.");
    }
    private static void SampleRates()
    {
        foreach (int rate in new[] { 44100, 48000, 96000 })
            Near(Math.Pow(.5, 1.5), Gain(AmigaPaulaFilter.A1200, 7000, true, rate), .001, "LED cutoff must be independent of sample rate.");
    }
    private static void ResetAndStereo()
    {
        foreach (AmigaPaulaFilter mode in Enum.GetValues<AmigaPaulaFilter>())
        {
            var filter = new PaulaOutputFilter { Mode = mode };
            for (int i = 0; i < 2000; i++)
            {
                float left = -.4f, right = 0; filter.Process(ref left, ref right, true);
                Require(right == 0, "A silent right channel must stay silent.");
                if (i == 1999) Near(-.4, left, 1e-6, "Unity DC gain without clipping.");
            }
            filter.Reset(); float l = 0, r = 0; filter.Process(ref l, ref r, true);
            Require(l == 0 && r == 0, "Reset must not leak old music into a new session.");
        }
    }
    private static void WarmLed()
    {
        var filter = new PaulaOutputFilter { Mode = AmigaPaulaFilter.A500 };
        for (int i = 0; i < 4000; i++)
        {
            float l = .2f, r = -.3f; filter.Process(ref l, ref r, i >= 2000);
            if (i >= 1999) { Near(.2, l, 1e-6, "Left LED switch remains warm."); Near(-.3, r, 1e-6, "Right LED switch remains warm."); }
        }
    }
    private static void CiaLed()
    {
        var cia = new Cia8520("CIA-A");
        cia.WriteByte(0xBFE001, 0); Require(!cia.AudioFilterLedEnabled, "An input pin defaults high.");
        cia.WriteByte(0xBFE201, 2); Require(cia.AudioFilterLedEnabled, "Output low selects LED filter.");
        using var state = new MemoryStream(); using var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true);
        cia.SaveState(writer); cia.WriteByte(0xBFE001, 2); Require(!cia.AudioFilterLedEnabled, "Output high bypasses LED.");
        state.Position = 0; using var reader = new BinaryReader(state); cia.LoadState(reader);
        Require(cia.AudioFilterLedEnabled, "Restore must restore LED control too.");
        var other = new Cia8520("CIA-B"); other.WriteByte(0xBFD000, 0); other.WriteByte(0xBFD200, 2);
        Require(!other.AudioFilterLedEnabled, "CIA-B does not control the audio filter.");
    }
    private static void FilterSelections()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (AmigaPaulaFilter mode in Enum.GetValues<AmigaPaulaFilter>())
        {
            object selected = typeof(AmigaWindow).GetMethod("GetPaulaFilterFromSettings", flags).Invoke(null, new object[] { mode });
            object stored = typeof(AmigaWindow).GetMethod("GetSettingsPaulaFilter", flags).Invoke(null, new[] { selected });
            Require((AmigaPaulaFilter)stored == mode, "A500/A1200/LED must never collapse into another selection.");
        }
    }
    private static Paula Manual(ushort period = 300, int channel = 0)
    {
        var paula = new Paula(); uint address = 0xDFF0A0u + (uint)channel * 16;
        paula.NotifyCustomRegisterWordWrite(address + 6, period);
        paula.NotifyCustomRegisterWordWrite(address + 8, 64);
        return paula;
    }
    private static void Tick(Paula paula, int ticks, bool dma = false)
    {
        for (int i = 0; i < ticks; i++) paula.Tick(false, dma ? 0 : -1);
    }
    private static void ManualLatches()
    {
        var paula = Manual(); float sample = 0;
        paula.AudioSampleGenerated += (l, r) => sample = l;
        paula.NotifyCustomRegisterWordWrite(0xDFF0AA, 0x1122); Tick(paula, 100);
        paula.NotifyCustomRegisterWordWrite(0xDFF0AA, 0x3344); Tick(paula, 450);
        Near(0x22 / 256.0, sample, 1e-6, "Low byte belongs to the original word.");
        Tick(paula, 300); Near(0x33 / 256.0, sample, 1e-6, "Pending high byte follows the whole previous word.");
        Tick(paula, 300); Near(0x44 / 256.0, sample, 1e-6, "Pending low byte is retained.");
    }
    private static Paula Dma(ushort period, ushort first, ushort second)
    {
        var paula = Manual(period); int reads = 0;
        paula.AttachDisk(null, null, _ => (++reads & 1) != 0 ? first : second, _ => { }, () => 0x0201, () => 0);
        paula.NotifyCustomRegisterWordWrite(0xDFF0A4, 2);
        return paula;
    }
    private static void DmaLatches()
    {
        var paula = Dma(300, 0x10E0, 0x3040); float sample = 0;
        paula.AudioSampleGenerated += (l, r) => { sample = l; Require(r == 0, "Channel 0 routes left only."); };
        Tick(paula, 550, true); Near(-32 / 256.0, sample, 1e-6, "Signed low byte must survive early DMA prefetch.");
        Tick(paula, 300, true); Near(48 / 256.0, sample, 1e-6, "Next DMA high byte.");
        Tick(paula, 300, true); Near(64 / 256.0, sample, 1e-6, "Next DMA low byte.");
    }
    private static void ZeroPeriod()
    {
        var paula = Manual(0); float sample = 0;
        paula.AudioSampleGenerated += (l, r) => sample = l;
        paula.NotifyCustomRegisterWordWrite(0xDFF0AA, 0x7F81);
        Tick(paula, 65000); Near(127 / 256.0, sample, 1e-6, "Zero is the longest period, not a one-clock tone.");
        Tick(paula, 1000); Near(-127 / 256.0, sample, 1e-6, "Low byte appears after 65536 clocks.");
    }
    private static void ClockRates()
    {
        foreach (int clock in new[] { 3546895, 3579545 })
        {
            var paula = new Paula(512 * 1024, clock); Tick(paula, clock);
            Require(paula.AudioSamplesGenerated == 48000, "Output rate must match the machine clock.");
        }
    }
    private static void Integration()
    {
        var paula = Dma(10, 0x7F81, 0x7F81); var samples = new List<float>();
        paula.AudioSampleGenerated += (l, r) => samples.Add(l);
        Tick(paula, 20000, true);
        double rms = Math.Sqrt(samples.Skip(5).Average(s => (double)s * s));
        Require(rms < .06, "Ultrasonic DAC square waves must be averaged instead of aliased at full amplitude. RMS=" + rms);
    }
    private static void Restore()
    {
        var paula = Dma(300, 0x10E0, 0x3040); Tick(paula, 347, true);
        paula.NotifyCustomRegisterWordWrite(0xDFF0A8, 23); // save before the new volume reaches a chip clock
        using var state = new MemoryStream(); using var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true);
        paula.SaveState(writer);
        var first = new List<float>(); Action<float, float> collect = (l, r) => first.Add(l);
        paula.AudioSampleGenerated += collect; Tick(paula, 5000, true); paula.AudioSampleGenerated -= collect;
        state.Position = 0; using var reader = new BinaryReader(state); paula.LoadState(reader);
        var second = new List<float>(); paula.AudioSampleGenerated += (l, r) => second.Add(l); Tick(paula, 5000, true);
        Require(first.SequenceEqual(second), "Restore must reproduce every host sample exactly, including fractional integration and volume changes.");
    }
    private static void LegacyRestore()
    {
        var source = Manual(); source.NotifyCustomRegisterWordWrite(0xDFF0AA, 0x20E0); Tick(source, 347);
        using var state = new MemoryStream(); using var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true);
        // Reproduce the older root field layout without any integration state.
        StateSerializer.WriteObjectFields(writer, source, "_floppyController", "_writeChipWord", "_readChipWord", "_requestInterrupt",
            "_readDmaControl", "_readAdkControl", "_diskWordPeriodChipCycles", "_audioClockHz", "_audioIntegralLeft", "_audioIntegralRight",
            "_audioIntervalTicks", "_audioMixLeft", "_audioMixRight", "_audioMixDirty", "_audioIntegrationStateValid");
        var fresh = Manual(); var live = Manual(); live.NotifyCustomRegisterWordWrite(0xDFF0AA, 0x7777); Tick(live, 937);
        var freshAudio = new List<float>(); var liveAudio = new List<float>();
        fresh.AudioSampleGenerated += (l, r) => freshAudio.Add(l); live.AudioSampleGenerated += (l, r) => liveAudio.Add(l);
        byte[] bytes = state.ToArray();
        using (var reader = new BinaryReader(new MemoryStream(bytes))) fresh.LoadState(reader);
        using (var reader = new BinaryReader(new MemoryStream(bytes))) live.LoadState(reader);
        Tick(fresh, 5000); Tick(live, 5000);
        Require(freshAudio.SequenceEqual(liveAudio), "Old audio states must behave identically when loaded into a fresh or running machine.");
    }
}
