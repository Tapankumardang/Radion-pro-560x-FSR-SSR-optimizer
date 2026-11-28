using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WhereWindsMeetLoginFix
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            if (!IsRunningAsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Administrator privileges are required to modify Epic Overlay files and close elevated game processes.\n\n" + ex.Message,
                        "Where Winds Meet Login Fix",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool IsRunningAsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const string EosRegPath = @"Software\Epic Games\EOS";
        private const string WwmEpicUri = "com.epicgames.launcher://apps/a7d28f0e0935490b9d6f92dda1a7a75b?action=launch&silent=true";
        private static readonly string[] TargetProcesses = { "wwm", "EOSOverlayRenderer-Win64-Shipping", "UniCrashReporter" };
        private static readonly string[] OverlayBinaries = { "EOSOverlayRenderer-Win64-Shipping.exe", "EOSOVH-Win64-Shipping.dll" };

        private readonly Label _lblStatus;
        private readonly CheckBox _chkPersistent;
        private readonly CheckBox _chkClearCache;
        private readonly Button _btnApply;
        private readonly Button _btnRestore;
        private readonly Button _btnLaunchGame;
        private readonly TextBox _txtLog;

        public MainForm()
        {
            Text = "Where Winds Meet - Epic Blank Login Fix";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 440);
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(235, 238, 245);
            Font = new Font("Segoe UI", 9.5f);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            var lblTitle = new Label
            {
                Text = "Where Winds Meet - Blank Login Screen Fix",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 200, 95),
                Location = new Point(20, 16),
                AutoSize = true
            };

            var lblDesc = new Label
            {
                Text = "Bypasses the broken Chromium 71 Epic Overlay (EOSOverlayRenderer) so Epic account\nauthorization opens cleanly in your default web browser (Google Chrome / Edge).",
                ForeColor = Color.FromArgb(185, 192, 208),
                Location = new Point(20, 48),
                Size = new Size(520, 38)
            };

            _lblStatus = new Label
            {
                Location = new Point(20, 94),
                Size = new Size(520, 44),
                BackColor = Color.FromArgb(34, 37, 46),
                ForeColor = Color.FromArgb(220, 225, 235),
                Padding = new Padding(10, 8, 10, 8),
                Text = "Checking status..."
            };

            _chkPersistent = new CheckBox
            {
                Text = "Persistent bypass (rename EOSOverlayRenderer & EOSOVH to .bak across restarts)",
                Checked = true,
                Location = new Point(20, 148),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 225, 235)
            };

            _chkClearCache = new CheckBox
            {
                Text = "Clear stale EOSOverlay BrowserCache folder",
                Checked = true,
                Location = new Point(20, 174),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 225, 235)
            };

            _btnApply = new Button
            {
                Text = "Apply Login Fix",
                Location = new Point(20, 210),
                Size = new Size(165, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 139, 87),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnApply.FlatAppearance.BorderSize = 0;
            _btnApply.Click += (s, e) => ApplyFix();

            _btnRestore = new Button
            {
                Text = "Restore Overlay",
                Location = new Point(195, 210),
                Size = new Size(155, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(65, 72, 88),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnRestore.FlatAppearance.BorderSize = 0;
            _btnRestore.Click += (s, e) => RestoreOverlay();

            _btnLaunchGame = new Button
            {
                Text = "Launch Game",
                Location = new Point(360, 210),
                Size = new Size(180, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 108, 184),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnLaunchGame.FlatAppearance.BorderSize = 0;
            _btnLaunchGame.Click += (s, e) => LaunchGame();

            _txtLog = new TextBox
            {
                Location = new Point(20, 260),
                Size = new Size(520, 162),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(16, 18, 22),
                ForeColor = Color.FromArgb(180, 225, 190),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f)
            };

            Controls.AddRange(new Control[]
            {
                lblTitle, lblDesc, _lblStatus, _chkPersistent, _chkClearCache,
                _btnApply, _btnRestore, _btnLaunchGame, _txtLog
            });

            RefreshStatus();
        }

        private void Log(string message)
        {
            _txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine));
        }

        private static string GetBrowserCacheDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Epic Games\EOSOverlay\BrowserCache");
        }

        private static string ResolveOverlayDirectory()
        {
            var candidates = new List<string>();
            using (var key = Registry.CurrentUser.OpenSubKey(EosRegPath))
            {
                if (key != null)
                {
                    AddCandidate(candidates, key.GetValue("OverlayPath") as string);
                    AddCandidate(candidates, key.GetValue("OverlayPath_Backup") as string);
                }
            }

            foreach (var drive in new[] { "E", "C", "D" })
            {
                candidates.Add(drive + @":\Program Files (x86)\Epic Games\Launcher\Portal\Extras\Overlay");
                candidates.Add(drive + @":\Program Files\Epic Games\Launcher\Portal\Extras\Overlay");
            }

            foreach (var dir in candidates)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var bin in OverlayBinaries)
                {
                    if (File.Exists(Path.Combine(dir, bin)) || File.Exists(Path.Combine(dir, bin + ".bak")))
                        return dir;
                }
            }
            return null;
        }

        private static void AddCandidate(List<string> list, string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return;
            list.Add(rawPath.Replace('/', '\\').TrimEnd('\\'));
        }

        private void RefreshStatus()
        {
            string regOverlayPath = null;
            string regBackup = null;
            using (var key = Registry.CurrentUser.OpenSubKey(EosRegPath))
            {
                if (key != null)
                {
                    regOverlayPath = key.GetValue("OverlayPath") as string;
                    regBackup = key.GetValue("OverlayPath_Backup") as string;
                }
            }

            var overlayDir = ResolveOverlayDirectory();
            bool filesDisabled = false;
            if (overlayDir != null)
            {
                foreach (var bin in OverlayBinaries)
                {
                    if (File.Exists(Path.Combine(overlayDir, bin + ".bak")) && !File.Exists(Path.Combine(overlayDir, bin)))
                    {
                        filesDisabled = true;
                        break;
                    }
                }
            }

            bool regDisabled = string.IsNullOrEmpty(regOverlayPath) && !string.IsNullOrEmpty(regBackup);
            bool gameRunning = Process.GetProcessesByName("wwm").Length > 0;
            bool cacheExists = Directory.Exists(GetBrowserCacheDir());

            string mode = (filesDisabled || regDisabled)
                ? "FIX ACTIVE (Browser fallback enabled)"
                : "DEFAULT (EOS Overlay active)";
            _lblStatus.Text = string.Format(
                "Mode: {0}\r\nOverlay Dir: {1} | Game Running: {2} | Cache Present: {3}",
                mode,
                overlayDir ?? "Not Found",
                gameRunning ? "Yes" : "No",
                cacheExists ? "Yes" : "No");
        }

        private void StopGameProcesses()
        {
            foreach (var procName in TargetProcesses)
            {
                var procs = Process.GetProcessesByName(procName);
                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(4000);
                        Log("Stopped " + procName + " (PID " + p.Id + ")");
                    }
                    catch (Exception ex)
                    {
                        Log("Warning stopping " + procName + ": " + ex.Message);
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            Thread.Sleep(500);
        }

        private void ToggleOverlayBinaries(string overlayDir, bool disable)
        {
            if (string.IsNullOrEmpty(overlayDir))
            {
                Log("Epic Overlay folder not found; skipping binary rename.");
                return;
            }

            foreach (var bin in OverlayBinaries)
            {
                var normalPath = Path.Combine(overlayDir, bin);
                var bakPath = normalPath + ".bak";
                var src = disable ? normalPath : bakPath;
                var dst = disable ? bakPath : normalPath;

                if (File.Exists(src))
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                    Log((disable ? "Disabled " : "Restored ") + bin);
                }
                else if (File.Exists(dst))
                {
                    Log(bin + (disable ? " already disabled (.bak)." : " already active."));
                }
                else
                {
                    Log("Skipped missing file: " + bin);
                }
            }
        }

        private void ApplyFix()
        {
            try
            {
                Log("--- Applying Where Winds Meet Login Fix ---");
                StopGameProcesses();

                if (_chkClearCache.Checked)
                {
                    var cacheDir = GetBrowserCacheDir();
                    if (Directory.Exists(cacheDir))
                    {
                        Directory.Delete(cacheDir, true);
                        Log("Cleared EOSOverlay BrowserCache.");
                    }
                    else
                    {
                        Log("EOSOverlay BrowserCache already clean.");
                    }
                }

                var overlayDir = ResolveOverlayDirectory();
                using (var key = Registry.CurrentUser.CreateSubKey(EosRegPath))
                {
                    var current = key.GetValue("OverlayPath") as string;
                    if (!string.IsNullOrEmpty(current))
                    {
                        key.SetValue("OverlayPath_Backup", current, RegistryValueKind.String);
                    }
                    key.SetValue("OverlayPath", "", RegistryValueKind.String);
                    Log("Cleared HKCU OverlayPath (browser fallback active).");
                }

                if (_chkPersistent.Checked)
                {
                    ToggleOverlayBinaries(overlayDir, true);
                }

                RefreshStatus();
                Log("Done! Click 'Launch Game' and allow login in your web browser.");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                MessageBox.Show(ex.Message, "Apply Fix Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RestoreOverlay()
        {
            try
            {
                Log("--- Restoring Default Epic Overlay ---");
                StopGameProcesses();

                var overlayDir = ResolveOverlayDirectory();
                ToggleOverlayBinaries(overlayDir, false);

                using (var key = Registry.CurrentUser.CreateSubKey(EosRegPath))
                {
                    var backup = key.GetValue("OverlayPath_Backup") as string;
                    var restored = !string.IsNullOrEmpty(backup)
                        ? backup
                        : (overlayDir != null ? overlayDir.Replace('\\', '/') : "");

                    if (!string.IsNullOrEmpty(restored))
                    {
                        key.SetValue("OverlayPath", restored, RegistryValueKind.String);
                        Log("Restored HKCU OverlayPath -> " + restored);
                    }
                    key.DeleteValue("OverlayPath_Backup", false);
                }

                RefreshStatus();
                Log("Default Epic Overlay restored.");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                MessageBox.Show(ex.Message, "Restore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LaunchGame()
        {
            try
            {
                Log("Launching Where Winds Meet via Epic Games...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = WwmEpicUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log("Launch error: " + ex.Message);
                MessageBox.Show("Could not launch via Epic Games URI:\n" + ex.Message, "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
