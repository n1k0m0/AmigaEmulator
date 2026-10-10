using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AmigaEmulator.Core;

namespace AmigaEmulator.Startup;

/// <summary>Uses only user-owned ROMs. Runs before the emulator or its updater is created.</summary>
public static class RomStartup
{
    public static bool EnsureRequiredRom(AmigaEmulatorSettings settings,
        Func<string, string> choosePath = null, Action<string> showError = null,
        IEnumerable<string> searchDirectories = null)
    {
        choosePath ??= ShowRomPathDialog;
        showError ??= message => MessageBox.Show(message, "Amiga Emulator – Kickstart-ROM",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        try
        {
            string available = FindStartupRom(settings, searchDirectories);
            if (available != null)
            {
                if (!string.Equals(settings.CustomKickstartPath, available, StringComparison.OrdinalIgnoreCase))
                {
                    settings.CustomKickstartPath = available;
                    AmigaEmulatorSettingsStore.Save(settings);
                }
                return true;
            }

            string path = choosePath(settings.RomSearchDirectory);
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!TryUseRomPath(settings, path, out string error))
            {
                showError(error + "\n\nOhne gültiges Kickstart-ROM wird der Emulator beendet.");
                return false;
            }
            AmigaEmulatorSettingsStore.Save(settings);
            return true;
        }
        catch (Exception ex)
        {
            showError("Das Kickstart-ROM konnte nicht eingerichtet werden:\n" + ex.Message
                + "\n\nDer Emulator wird beendet.");
            return false;
        }
    }

    public static string FindStartupRom(AmigaEmulatorSettings settings, IEnumerable<string> searchDirectories = null)
    {
        if (KickstartRomResolver.Probe(settings.CustomKickstartPath).IsUsable) return Path.GetFullPath(settings.CustomKickstartPath);
        // Diagnostic tests supply explicit search roots, independent of installed user ROMs.
        if (searchDirectories == null)
        {
            string environmentRom = Environment.GetEnvironmentVariable("AMIGA_KICKSTART");
            if (KickstartRomResolver.Probe(environmentRom).IsUsable) return Path.GetFullPath(environmentRom);
        }
        var candidates = KickstartRomResolver.FindAvailableKickstartRoms(searchDirectories ?? KickstartRomResolver.GetSearchDirectories());
        return ChooseRom(settings, candidates)?.Path;
    }

    public static bool TryUseRomPath(AmigaEmulatorSettings settings, string path, out string error)
    {
        error = string.Empty;
        path = path?.Trim().Trim('"');
        KickstartRomInfo rom;
        string directory;
        if (File.Exists(path))
        {
            rom = KickstartRomResolver.Probe(path);
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        else if (Directory.Exists(path))
        {
            directory = Path.GetFullPath(path);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true,
                MaxRecursionDepth = 8, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };
            var directories = new[] { directory }.Concat(Directory.EnumerateDirectories(directory, "*", options));
            rom = ChooseRom(settings, KickstartRomResolver.FindAvailableKickstartRoms(directories), explicitSelection: true);
        }
        else
        {
            error = "Der ausgewählte ROM-Pfad ist nicht vorhanden oder nicht erreichbar.";
            return false;
        }
        if (rom?.IsUsable != true)
        {
            error = "Im ausgewählten Pfad wurde kein gültiges Kickstart-ROM für " + settings.MachineModel + " gefunden."
                + (rom?.Error == null ? "" : "\n" + rom.Error)
                + "\nUnterstützt werden unverschlüsselte Kickstart-Dateien mit 256 KB, 512 KB oder 1 MB."
                + "\nEin unbekanntes, selbst beschafftes ROM kann auch einzeln ausgewählt werden.";
            return false;
        }
        settings.CustomKickstartPath = Path.GetFullPath(rom.Path);
        // Remember the actual ROM's directory, also when the selected folder had subfolders.
        settings.RomSearchDirectory = Path.GetDirectoryName(settings.CustomKickstartPath);
        settings.Kickstart = AmigaKickstart.Custom;
        return true;
    }

    private static KickstartRomInfo ChooseRom(AmigaEmulatorSettings settings, IEnumerable<KickstartRomInfo> roms, bool explicitSelection = false)
    {
        var usable = roms.Where(r => r.IsUsable).ToList();
        if (settings.MachineModel is AmigaMachineModel.A1200 or AmigaMachineModel.A3000 or AmigaMachineModel.A4000Ec030)
            return usable.FirstOrDefault(r => KickstartModelCompatibility.IsKnownCompatible(settings.MachineModel, r.Sha1))
                ?? (explicitSelection ? usable.FirstOrDefault() : null);
        string preferred = settings.MachineModel is AmigaMachineModel.A500 or AmigaMachineModel.A2000Ocs
            ? "Kickstart13" : "Kickstart204";
        return usable.FirstOrDefault(r => r.Kind == preferred) ?? usable.FirstOrDefault();
    }

    private static string ShowRomPathDialog(string initialPath)
    {
        using var dialog = new RomPathDialog(initialPath);
        return dialog.ShowDialog() == DialogResult.OK ? dialog.RomPath : null;
    }

    private sealed class RomPathDialog : Form
    {
        private readonly TextBox _path;
        public string RomPath => _path.Text;
        public RomPathDialog(string initialPath)
        {
            Text = "Amiga Emulator – eigenes Kickstart-ROM auswählen";
            ClientSize = new Size(660, 240); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(10, 13, 24); ForeColor = Color.FromArgb(255, 230, 150);
            Controls.Add(new Label { Text = "Kein gültiges Kickstart-ROM gefunden.\n\nROMs werden nicht mitgeliefert. Wähle eine eigene ROM-Datei oder den Ordner mit deinen ROMs. Bei Abbrechen wird der Emulator beendet.",
                Location = new Point(20, 18), Size = new Size(620, 76) });
            _path = new TextBox { Text = initialPath ?? "", Location = new Point(20, 110), Size = new Size(620, 26),
                BackColor = Color.FromArgb(25, 30, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var folder = new Button { Text = "Ordner …", Location = new Point(20, 150), Size = new Size(120, 30) };
            var file = new Button { Text = "ROM-Datei …", Location = new Point(150, 150), Size = new Size(120, 30) };
            var accept = new Button { Text = "Starten", DialogResult = DialogResult.OK, Location = new Point(390, 195), Size = new Size(120, 30) };
            var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(520, 195), Size = new Size(120, 30) };
            foreach (var button in new[] { folder, file, accept, cancel })
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Color.FromArgb(25, 30, 45);
                button.ForeColor = Color.FromArgb(255, 230, 150);
            }
            folder.Click += (_, _) =>
            {
                using var browser = new FolderBrowserDialog { Description = "Ordner mit deinen eigenen Kickstart-ROMs auswählen", UseDescriptionForTitle = true, ShowNewFolderButton = false };
                if (Directory.Exists(_path.Text)) browser.SelectedPath = _path.Text;
                if (browser.ShowDialog(this) == DialogResult.OK) _path.Text = browser.SelectedPath;
            };
            file.Click += (_, _) =>
            {
                using var browser = new OpenFileDialog { Title = "Eigenes Kickstart-ROM auswählen", Filter = "Kickstart-ROM (*.rom;*.bin)|*.rom;*.bin|Alle Dateien (*.*)|*.*", CheckFileExists = true };
                if (browser.ShowDialog(this) == DialogResult.OK) _path.Text = browser.FileName;
            };
            Controls.AddRange(new Control[] { _path, folder, file, accept, cancel });
            AcceptButton = accept; CancelButton = cancel;
        }
    }
}
