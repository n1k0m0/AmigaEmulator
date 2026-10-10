using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Startup;
using AmigaEmulator.Updates;

namespace AmigaEmulator.Tests;

internal static class StartupDistributionTests
{
    public static IEnumerable<ChipTestCase> Build()
    {
        yield return Test("Missing ROM cancellation exits startup before creating settings or machine", MissingRom);
        yield return Test("User ROM folder persists external paths, skips invalid files and never copies ROMs", UserRom);
        yield return Test("Invalid, removed and inaccessible ROM selections reject startup without changing settings", InvalidRom);
        yield return Test("Explicit ROM override and saved hardware survive startup validation", ModelRom);
        yield return Test("GitHub release parsing selects only stable Amiga setup assets and trusted download URLs", ReleaseMetadata);
        yield return Test("Updater compares versions and preserves safe installer/helper names", Versions);
        yield return Test("Real HTTP transfer verifies SHA256 and preserves old installer on bad or truncated downloads", Download);
        yield return Test("Cancelled, empty and failed HTTP downloads never produce executable updates", DownloadFailures);
        yield return Test("Closing finished update dialog does not authorize installation", () => Sta(CloseDialog));
    }
    private static ChipTestCase Test(string name, Action run) => new("Distribution", name, "User-owned ROM startup and C64-derived GitHub updater", run);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static object Call(string name, params object[] args) => typeof(StartupUpdateChecker).GetMethod(name, StaticPrivate).Invoke(null, args);
    private static string Field(object o, string name) => (string)o.GetType().GetProperty(name).GetValue(o);

    private static void MissingRom()
    {
        using var p = new Profile(); var settings = new AmigaEmulatorSettings(); int prompts = 0;
        bool ok = RomStartup.EnsureRequiredRom(settings, _ => { prompts++; return null; }, _ => throw new Exception("Cancellation is not an error"), new[] { p.Directory });
        Require(!ok && prompts == 1 && !File.Exists(UserDataPaths.GetSettingsPath()), "No ROM: one prompt, clean cancellation, no emulator settings side effects.");
    }
    private static void UserRom()
    {
        using var p = new Profile(); var settings = new AmigaEmulatorSettings { RamProfile = AmigaRamProfile.Chip2MbFast8Mb };
        string root = Path.Combine(p.Directory, "owned-roms"); Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "bad.rom"), new byte[512]);
        string nested = Path.Combine(root, "kickstarts"); Directory.CreateDirectory(nested);
        string rom = Path.Combine(nested, "my-original.rom"); byte[] source = MakeRom(); File.WriteAllBytes(rom, source);
        Require(RomStartup.EnsureRequiredRom(settings, _ => root, _ => throw new Exception("Unexpected ROM error"), Array.Empty<string>()), "Folder with nested valid ROM starts.");
        var loaded = AmigaEmulatorSettingsStore.Load();
        Require(loaded.CustomKickstartPath == rom && loaded.RomSearchDirectory == nested && loaded.RamProfile == settings.RamProfile, "External ROM and hardware preferences persist.");
        Require(!Directory.Exists(UserDataPaths.GetRomDirectory()) && File.ReadAllBytes(rom).SequenceEqual(source), "No import/download/overwrite of user ROM.");
        Require(KickstartRomResolver.FindAvailableKickstartRoms().Any(r => r.Path == rom && r.IsUsable), "Remembered directory appears in ROM menu.");
        Require(RomStartup.EnsureRequiredRom(loaded, _ => throw new Exception("Existing ROM must not ask again"), _ => throw new Exception("Existing ROM error"), Array.Empty<string>()), "Subsequent startup does not ask.");
    }
    private static void InvalidRom()
    {
        using var p = new Profile(); var settings = new AmigaEmulatorSettings { CustomKickstartPath = "original-selection" };
        string file = Path.Combine(p.Directory, "broken.rom"); File.WriteAllBytes(file, new byte[256 * 1024]);
        int errors = 0;
        Require(!RomStartup.EnsureRequiredRom(settings, _ => file, _ => errors++, new[] { p.Directory }) && errors == 1, "Invalid vectors reject GUI startup.");
        Require(settings.CustomKickstartPath == "original-selection" && !File.Exists(UserDataPaths.GetSettingsPath()), "Invalid selection preserves settings.");
        Require(!RomStartup.TryUseRomPath(settings, Path.Combine(p.Directory, "missing"), out _), "Missing path rejects.");
        Require(!RomStartup.TryUseRomPath(settings, p.Directory, out _), "Folder containing only invalid ROM rejects.");
        Require(!RomStartup.EnsureRequiredRom(settings, _ => throw new IOException("Disconnected share"), _ => errors++, Array.Empty<string>()), "Disconnected share fails cleanly.");
        File.Delete(file);
        Require(!RomStartup.EnsureRequiredRom(settings, _ => file, _ => errors++, Array.Empty<string>()), "Removed file requires a new valid selection.");
    }
    private static void ModelRom()
    {
        using var p = new Profile(); string path = Path.Combine(p.Directory, "owned-custom.bin"); File.WriteAllBytes(path, MakeRom());
        var settings = new AmigaEmulatorSettings { MachineModel = AmigaMachineModel.A4000Ec030 };
        Require(RomStartup.FindStartupRom(settings, new[] { p.Directory }) == null, "Unknown motherboard ROM is not silently auto-selected.");
        Require(RomStartup.TryUseRomPath(settings, p.Directory, out _), "Explicit folder accepts a user ROM whose hash is not in the motherboard catalog.");
        Require(RomStartup.TryUseRomPath(settings, path, out _), "Explicit user selection accepts a structurally valid custom ROM.");
        Require(RomStartup.FindStartupRom(settings, Array.Empty<string>()) == path && settings.MachineModel == AmigaMachineModel.A4000Ec030, "Startup does not silently replace hardware selection.");
        settings.ApplyStartupModel(AmigaModel.A500Pal);
        Require(RomStartup.FindStartupRom(settings, new[] { p.Directory }) == path, "Command-line model resets stale selection and finds available ROM.");
    }
    private static byte[] MakeRom()
    {
        var bytes = new byte[256 * 1024]; bytes[1] = 0x08; bytes[5] = 0xF8; bytes[7] = 8;
        bytes[8] = 0x60; bytes[9] = 0xFE; // Valid reset vectors and a synthetic BRA.S self loop.
        return bytes;
    }

    private static string Json(string name = "AmigaEmulator-0.2.0-win-x64-setup.exe", string url = "https://github.com/n1k0m0/AmigaEmulator/releases/download/v0.2.0/AmigaEmulator-0.2.0-win-x64-setup.exe", string digest = null, bool draft = false, bool prerelease = false)
        => JsonSerializer.Serialize(new { tag_name = "v0.2.0", draft, prerelease, assets = new[] { new { name, browser_download_url = url, digest } } });
    private static void ReleaseMetadata()
    {
        object release = Call("ParseReleaseInfo", Json()); Require(Field(release, "InstallerName").StartsWith("AmigaEmulator-"), "Amiga setup selected.");
        Require(Call("ParseReleaseInfo", Json(draft: true)) == null && Call("ParseReleaseInfo", Json(prerelease: true)) == null, "Only stable releases.");
        foreach (string url in new[] { "http://github.com/n1k0m0/AmigaEmulator/releases/download/v0.2.0/file.exe", "https://evil.example/file.exe", "https://github.com/n1k0m0/C64Emulator/releases/download/v0.2.0/file.exe" })
            Require(string.IsNullOrEmpty(Field(Call("ParseReleaseInfo", Json(url: url)), "InstallerDownloadUrl")), "Untrusted setup URL ignored.");
        Require(string.IsNullOrEmpty(Field(Call("ParseReleaseInfo", Json(name: "C64Emulator-0.2.0-win-x64-setup.exe")), "InstallerName")), "C64 setup ignored.");
        Require(Call("ParseReleaseInfo", Json(digest: "sha256:invalid")) == null, "Malformed supplied digest fails closed.");
        Require(Call("ParseReleaseInfo", "[]") == null && Call("ParseReleaseInfo", " ") == null, "Empty/non-object metadata ignored.");
    }
    private static void Versions()
    {
        Require((Version)Call("ParseVersion", "v1.2.3") == new Version(1, 2, 3), "GitHub v prefix.");
        Require(Call("ParseVersion", "v1.2.3-rc1") == null, "Prerelease version does not look stable.");
        Require((int)Call("CompareVersions", new Version(1, 2, 3), new Version(1, 2, 3, 0)) == 0, "Three/four component versions agree.");
        Require((int)Call("CompareVersions", new Version(0, 2, 0), new Version(0, 1, 0, 0)) > 0, "New release recognized.");
        Require((string)Call("QuotePowerShellString", "C:\\My Games\\Nils' setup.exe") == "'C:\\My Games\\Nils'' setup.exe'", "Helper cannot interpret path apostrophes as code.");
        Require(!(bool)Call("IsWindowsSetupAsset", "../AmigaEmulator-0.2.0-win-x64-setup.exe"), "Asset names cannot escape staging folder.");
        Require((Version)Call("GetCurrentApplicationVersion") == typeof(StartupUpdateChecker).Assembly.GetName().Version, "Updater compares emulator assembly version.");
    }
    private static Task<string> Transfer(HttpClient http, object release, string path, CancellationToken cancellationToken = default, object progress = null)
        => (Task<string>)Call("DownloadVerifiedInstallerAsync", http, release, path, progress, cancellationToken);
    private static void Download()
    {
        using var p = new Profile(); string target = Path.Combine(p.Directory, "verified-setup.exe");
        byte[] data = Enumerable.Range(0, 200003).Select(i => (byte)(i * 13)).ToArray();
        object release = Call("ParseReleaseInfo", Json(digest: "sha256:" + Convert.ToHexString(SHA256.HashData(data))));
        using (var http = new HttpClient(new Handler(_ => Response(data))))
            Require(Transfer(http, release, target).GetAwaiter().GetResult() == target && File.ReadAllBytes(target).SequenceEqual(data), "Streamed installer is complete and verified.");
        byte[] previous = File.ReadAllBytes(target);
        using (var http = new HttpClient(new Handler(_ => Response(new byte[] { 1, 2, 3 }))))
            Reject(() => Transfer(http, release, target).GetAwaiter().GetResult());
        Require(File.ReadAllBytes(target).SequenceEqual(previous) && !File.Exists(target + ".download"), "Hash mismatch cleans partial file and preserves previous installer.");
        using (var http = new HttpClient(new Handler(_ => { var response = Response(data); response.Content.Headers.ContentLength = data.Length + 10; return response; })))
            Reject(() => Transfer(http, release, target).GetAwaiter().GetResult());
        Require(File.ReadAllBytes(target).SequenceEqual(previous) && !File.Exists(target + ".download"), "Truncation cannot replace executable update.");
    }
    private static void DownloadFailures()
    {
        using var p = new Profile(); string target = Path.Combine(p.Directory, "rejected.exe"); object release = Call("ParseReleaseInfo", Json());
        foreach (var response in new Func<HttpResponseMessage>[] {
            () => Response(Array.Empty<byte>()),
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
            () => { var r = Response(new byte[] { 1 }); r.Content.Headers.ContentLength = 513L * 1024 * 1024; return r; } })
        {
            using var http = new HttpClient(new Handler(_ => response()));
            Reject(() => Transfer(http, release, target).GetAwaiter().GetResult());
            Require(!File.Exists(target) && !File.Exists(target + ".download"), "Failed download never becomes executable.");
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var cancelled = new HttpClient(new Handler(_ => Response(new byte[] { 1, 2, 3 })));
        Reject(() => Transfer(cancelled, release, target, cancellation.Token).GetAwaiter().GetResult());
        Require(!File.Exists(target) && !File.Exists(target + ".download"), "Cancellation cleans staging file.");
        using var halfway = new CancellationTokenSource(); int reports = 0;
        Type progressType = typeof(StartupUpdateChecker).GetNestedType("SetupDownloadProgress", BindingFlags.NonPublic);
        object observer = Activator.CreateInstance(typeof(RecordingProgress<>).MakeGenericType(progressType), new object[] { (Action<object>)(_ => { reports++; halfway.Cancel(); }) });
        using var streaming = new HttpClient(new Handler(_ => Response(new byte[200000])));
        Reject(() => Transfer(streaming, release, target, halfway.Token, observer).GetAwaiter().GetResult());
        Require(reports == 1 && !File.Exists(target) && !File.Exists(target + ".download"), "Cancellation after a written chunk removes the partial download.");
    }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Handler(Func<CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(cancellationToken)); }
    }
    private sealed class RecordingProgress<T>(Action<object> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Invalid download was accepted.");
    }
    private static void CloseDialog()
    {
        Type type = typeof(StartupUpdateChecker).GetNestedType("SetupDownloadForm", BindingFlags.NonPublic);
        using var dialog = (Form)Activator.CreateInstance(type, new[] { Call("ParseReleaseInfo", Json()) });
        type.GetField("_downloadSucceeded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dialog, true);
        type.GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { new FormClosingEventArgs(CloseReason.UserClosing, false) });
        Require(dialog.DialogResult == DialogResult.Cancel, "Closing a finished download does not mean Install.");
    }
    private static void Sta(Action action)
    {
        Exception error = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error != null) throw error;
    }
    private sealed class Profile : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AmigaDistributionTests", Guid.NewGuid().ToString("N"));
        private readonly string _previous = Environment.GetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY");
        public Profile() { System.IO.Directory.CreateDirectory(Directory); Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", Directory); }
        public void Dispose() => Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", _previous);
    }
}
