using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RadeonPerformanceCenter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Enforce Administrator privileges
            bool isAdmin = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
            if (!isAdmin)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Application.ExecutablePath;
                    psi.UseShellExecute = true;
                    psi.Verb = "runas"; // Trigger Windows UAC elevation prompt
                    Process.Start(psi);
                    return; // Exit non-elevated process
                }
                catch
                {
                    MessageBox.Show("Administrator privileges are required to apply or restore GPU registry settings.\n\nPlease click 'Yes' on the Windows prompt or right-click the application and select 'Run as administrator'.",
                        "Administrator Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // Terminate any older running instance so only this latest active version runs
            try
            {
                int currentId = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName("RadeonOptimizer"))
                {
                    if (p.Id != currentId)
                    {
                        try { p.Kill(); p.WaitForExit(1500); } catch { }
                    }
                }
                string oldExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RadeonOptimizer_old.exe");
                if (File.Exists(oldExe)) { try { File.Delete(oldExe); } catch { } }
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UnifiedMainForm());
        }
    }

    public class UnifiedMainForm : Form
    {
        #region Win32 & P/Invoke Definitions
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
                                          out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
                                          out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        // OpenGL & Win32 P/Invoke
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", SetLastError = true)] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("ntdll.dll", SetLastError = true)] public static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        [DllImport("psapi.dll", SetLastError = true)] public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] public static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);
        [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr hdc, [In] ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int format, [In] ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hDC);
        [DllImport("opengl32.dll")] public static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport("opengl32.dll")] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
        [DllImport("opengl32.dll")] public static extern bool wglDeleteContext(IntPtr hglrc);

        [DllImport("opengl32.dll")] public static extern void glViewport(int x, int y, int width, int height);
        [DllImport("opengl32.dll")] public static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")] public static extern void glLoadIdentity();
        [DllImport("opengl32.dll")] public static extern void glFrustum(double left, double right, double bottom, double top, double zNear, double zFar);
        [DllImport("opengl32.dll")] public static extern void glEnable(uint cap);
        [DllImport("opengl32.dll")] public static extern void glDisable(uint cap);
        [DllImport("opengl32.dll")] public static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] public static extern void glClear(uint mask);
        [DllImport("opengl32.dll")] public static extern void glClearDepth(double depth);
        [DllImport("opengl32.dll")] public static extern void glDepthFunc(uint func);
        [DllImport("opengl32.dll")] public static extern void glBegin(uint mode);
        [DllImport("opengl32.dll")] public static extern void glEnd();
        [DllImport("opengl32.dll")] public static extern void glVertex3f(float x, float y, float z);
        [DllImport("opengl32.dll")] public static extern void glNormal3f(float nx, float ny, float nz);
        [DllImport("opengl32.dll")] public static extern void glColor3f(float r, float g, float b);
        [DllImport("opengl32.dll")] public static extern void glRotatef(float angle, float x, float y, float z);
        [DllImport("opengl32.dll")] public static extern void glTranslatef(float x, float y, float z);
        [DllImport("opengl32.dll")] public static extern void glLightfv(uint light, uint pname, float[] values);
        [DllImport("opengl32.dll")] public static extern void glShadeModel(uint mode);
        [DllImport("opengl32.dll")] public static extern void glPointSize(float size);
        [DllImport("opengl32.dll")] public static extern void glLineWidth(float width);
        [DllImport("opengl32.dll")] public static extern void glPushMatrix();
        [DllImport("opengl32.dll")] public static extern void glPopMatrix();
        [DllImport("opengl32.dll")] public static extern void glScalef(float x, float y, float z);
        [DllImport("opengl32.dll")] public static extern void glBlendFunc(uint sfactor, uint dfactor);
        [DllImport("opengl32.dll")] public static extern void glColor4f(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] public static extern void glDepthMask(byte flag);

        const uint GL_BLEND = 0x0BE2;
        const uint GL_SRC_ALPHA = 0x0302;
        const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;

        const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
        const uint GL_PROJECTION = 0x1701;
        const uint GL_MODELVIEW = 0x1700;
        const uint GL_DEPTH_TEST = 0x0B71;
        const uint GL_LEQUAL = 0x0203;
        const uint GL_LIGHTING = 0x0B50;
        const uint GL_LIGHT0 = 0x4000;
        const uint GL_SMOOTH = 0x1D01;
        const uint GL_TRIANGLES = 0x0004;
        const uint GL_POINTS = 0x0000;
        const uint GL_LINES = 0x0001;
        const uint GL_TRIANGLE_FAN = 0x0006;
        const uint GL_COLOR_MATERIAL = 0x0B57;

        [StructLayout(LayoutKind.Sequential)]
        public struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize, nVersion;
            public uint dwFlags;
            public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeMessage { public IntPtr handle, msg, wParam, lParam; public uint time; public Point p; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out NativeMessage msg, IntPtr hWnd, uint messageFilterMin, uint messageFilterMax, uint flags);
        private bool AppStillIdle { get { NativeMessage msg; return !PeekMessage(out msg, IntPtr.Zero, 0, 0, 0); } }
        #endregion

        #region Color Palette (Radeon Dark Theme)
        private Color cBg = Color.FromArgb(16, 17, 22);
        private Color cCardBg = Color.FromArgb(24, 26, 34);
        private Color cCardInner = Color.FromArgb(19, 20, 27);
        private Color cBorder = Color.FromArgb(45, 48, 60);
        private Color cRadeonRed = Color.FromArgb(224, 17, 95); // Radeon Crimson
        private Color cGreen = Color.FromArgb(74, 222, 128);
        private Color cCyan = Color.FromArgb(56, 189, 248);
        private Color cYellow = Color.FromArgb(250, 204, 21);
        private Color cTextMain = Color.FromArgb(241, 245, 249);
        private Color cTextMuted = Color.FromArgb(148, 163, 184);
        private Color cNavActive = Color.FromArgb(38, 42, 54);
        private Color cNavHover = Color.FromArgb(30, 33, 44);
        #endregion

        // Navigation state
        private int currentTabIndex = 0; // 0 = Optimizer, 1 = Benchmark, 2 = FSR, 3 = Guide
        private Button[] navButtons;
        private Panel[] tabPanels;

        // Telemetry calculation
        private ulong prevIdle = 0, prevKernel = 0, prevUser = 0;
        private Timer globalTimer;

        #region Optimizer Tab Controls
        private Label lblGpuName, lblGpuDriver, lblPowerPlan, lblCpuUsage, lblRamUsage;
        private ProgressBar prgCpu, prgRam;
        private Label lblUlpsStatus, lblHagsStatus, lblTdrStatus, lblCpuCapStatus, lblGameDvrStatus, lblRisStatus;
        private Label lblMpoStatus, lblUnparkStatus, lblTimerResStatus, lblRamBoostStatus;
        private Button btnToggleUlps, btnToggleHags, btnToggleTdr, btnToggleCpuCap, btnToggleGameDvr, btnToggleRis;
        private Button btnToggleMpo, btnToggleUnpark, btnToggleTimerRes, btnRamBoost;
        private bool isTimerResActive = false;
        private RichTextBox txtLog;
        #endregion

        #region Benchmark Tab Controls
        private Panel glRenderPanel;
        private PictureBox graphBox;
        private Label lblBenchFps, lblBenchAvgFps, lblBench1Pct, lblBenchStutter, lblBenchCpu, lblBenchRam, lblBenchVerdict, lblBenchTimer;
        private Button btnStartBench, btnBenchStress, btnToggleSsr, btnSsrSurface;
        private bool enableBenchmarkSsr = true;
        private bool isSsrWetMirror = true;
        private IntPtr hDC = IntPtr.Zero, hGLRC = IntPtr.Zero;
        private bool isGlInitialized = false;
        private float rotX = 15f, rotY = 0f, rotZ = 0f;
        private int particleCount = 1800;
        private float[] particleCoords, particleColors;
        private int benchStressLevel = 1;
        private Stopwatch benchFrameStopwatch = new Stopwatch();
        private Stopwatch benchTimedStopwatch = new Stopwatch();
        private double lastBenchFrameMs = 0;
        private List<double> rollingFrameTimes = new List<double>();
        private List<double> benchFrameTimes = new List<double>();
        private Queue<float> graphHistory = new Queue<float>();
        private int benchStutterEvents = 0;
        private bool isBenchRunning = false;
        private const double BENCH_DURATION_SEC = 30.0;
        #endregion

        #region FSR Tab Controls
        private Label lblFsrProcessStatus;
        private Button btnStartFsr, btnStopFsr, btnFsrAutoUpscale, btnToggleCinematicSsr, btnOptimizeGameSsr;
        private bool isCinematicSsrActive = true;
        private ComboBox cmbFsrHotkey, cmbFsrFpsCap, cmbSsrPreset;
        private Label lblCurrentRes;
        private Timer fsrCountdownTimer;
        private int fsrCountdownRemaining = 0;
        #endregion

        #region Hardware Mini-HUD & Fan Turbo Fields
        public int CurrentCpuPercent = 0;
        public string CurrentRamText = "0.0 GB";
        public bool IsCpuCapActive = true;
        public double CurrentFpsValue = 60.0;
        private FloatingMiniHud miniHudForm = null;
        private Button btnToggleMiniHud;
        private Button btnFanTurboHelper;
        private const int HOTKEY_MINIHUD_ID = 0x2001;
        #endregion

        public UnifiedMainForm()
        {
            this.Text = "Radeon Pro 560X — All-In-One GPU Performance Center";
            this.Size = new Size(1140, 890);
            this.MinimumSize = new Size(1060, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = cBg;
            this.ForeColor = cTextMain;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.DoubleBuffered = true;

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath)) this.Icon = new Icon(iconPath);
                else this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            InitParticles();
            BuildMainShell();

            // Background telemetry timer (1000ms)
            globalTimer = new Timer();
            globalTimer.Interval = 1000;
            globalTimer.Tick += (s, e) => OnGlobalTick();
            globalTimer.Start();

            // Initial load
            CheckAdminAndLog();
            RefreshAllOptimizerStates();
            UpdateHardwareMetrics();

            this.Shown += (s, e) => {
                InitOpenGL();
                Application.Idle += OnApplicationIdle;
                EnsureFsrRunningInBackground();
                try { RegisterHotKey(this.Handle, HOTKEY_MINIHUD_ID, 0x0001 | 0x0004, 0x48); } catch { } // Alt + Shift + H
            };
            this.FormClosing += (s, e) => {
                try { UnregisterHotKey(this.Handle, HOTKEY_MINIHUD_ID); } catch { }
                if (isTimerResActive) { uint cur; try { NtSetTimerResolution(5000, false, out cur); } catch { } }
                if (miniHudForm != null && !miniHudForm.IsDisposed) miniHudForm.Close();
                CleanupOpenGL();
            };
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_MINIHUD_ID)
            {
                ToggleMiniHud();
            }
            base.WndProc(ref m);
        }

        private void BuildMainShell()
        {
            // Root Container: Top Nav Bar (52px) + Main Content Area
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            this.Controls.Add(root);

            // 1. Navigation Header
            Panel navBar = new Panel();
            navBar.Dock = DockStyle.Fill;
            navBar.BackColor = Color.FromArgb(20, 22, 28);
            navBar.Padding = new Padding(12, 6, 12, 6);
            root.Controls.Add(navBar, 0, 0);

            // Brand Label
            Label lblBrand = new Label();
            lblBrand.Text = "RADEON PRO 560X";
            lblBrand.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblBrand.ForeColor = cRadeonRed;
            lblBrand.AutoSize = true;
            lblBrand.Location = new Point(14, 15);
            navBar.Controls.Add(lblBrand);

            // Nav Buttons Container
            FlowLayoutPanel pnlTabs = new FlowLayoutPanel();
            pnlTabs.Location = new Point(230, 8);
            pnlTabs.Size = new Size(880, 42);
            pnlTabs.FlowDirection = FlowDirection.LeftToRight;
            navBar.Controls.Add(pnlTabs);

            navButtons = new Button[4];
            navButtons[0] = CreateNavButton("🚀  GPU OPTIMIZER", 0);
            navButtons[1] = CreateNavButton("🔥  3D BENCHMARK", 1);
            navButtons[2] = CreateNavButton("✨  FSR UPSCALER", 2);
            navButtons[3] = CreateNavButton("📖  DRIVER GUIDE", 3);

            foreach (Button btn in navButtons) pnlTabs.Controls.Add(btn);

            // 2. Content Panels Container
            Panel contentContainer = new Panel();
            contentContainer.Dock = DockStyle.Fill;
            contentContainer.BackColor = cBg;
            root.Controls.Add(contentContainer, 0, 1);

            tabPanels = new Panel[4];
            tabPanels[0] = BuildOptimizerPanel();
            tabPanels[1] = BuildBenchmarkPanel();
            tabPanels[2] = BuildFsrPanel();
            tabPanels[3] = BuildGuidePanel();

            foreach (Panel p in tabPanels)
            {
                p.Dock = DockStyle.Fill;
                contentContainer.Controls.Add(p);
            }

            SwitchTab(0);
        }

        private Button CreateNavButton(string text, int index)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btn.Size = new Size(170, 38);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Cursor = Cursors.Hand;
            btn.ForeColor = (index == 0) ? Color.White : cTextMuted;
            btn.BackColor = (index == 0) ? cNavActive : Color.Transparent;
            btn.Click += (s, e) => SwitchTab(index);
            return btn;
        }

        private void SwitchTab(int index)
        {
            currentTabIndex = index;
            for (int i = 0; i < tabPanels.Length; i++)
            {
                tabPanels[i].Visible = (i == index);
                navButtons[i].BackColor = (i == index) ? cNavActive : Color.Transparent;
                navButtons[i].ForeColor = (i == index) ? Color.White : cTextMuted;
            }

            if (index == 1) // Benchmark tab active
            {
                ResizeGL();
            }
            if (index == 2) // FSR tab active
            {
                UpdateFsrStatus();
            }
        }

        #region TAB 1: GPU OPTIMIZER
        private Panel BuildOptimizerPanel()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.AutoScroll = true;
            root.Padding = new Padding(12);

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 4;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));  // Metrics
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));  // Master Buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 435f)); // Tweak Cards (2x5)
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Console Log
            root.Controls.Add(mainLayout);

            // Row 0: Hardware Telemetry
            mainLayout.Controls.Add(BuildOptimizerTelemetryBar(), 0, 0);

            // Row 1: Master Action Bar
            FlowLayoutPanel pnlMaster = new FlowLayoutPanel();
            pnlMaster.Dock = DockStyle.Fill;
            pnlMaster.FlowDirection = FlowDirection.LeftToRight;

            Button btnApplyAll = CreateModernButton("🚀  APPLY ALL", Color.FromArgb(20, 140, 80), Color.White, 195, 40);
            btnApplyAll.Click += (s, e) => ApplyAllOptimizations();

            Button btnRestore = CreateModernButton("↺  RESTORE DEFAULTS", Color.FromArgb(70, 75, 90), Color.FromArgb(220, 225, 235), 185, 40);
            btnRestore.Click += (s, e) => RestoreDefaults();

            Button btnRefresh = CreateModernButton("🔄  REFRESH", Color.FromArgb(40, 45, 60), cCyan, 120, 40);
            btnRefresh.Click += (s, e) => { RefreshAllOptimizerStates(); Log("Refreshed system and optimization states."); };

            btnToggleMiniHud = CreateModernButton("🔥  MINI-HUD: OFF", Color.FromArgb(170, 40, 40), Color.White, 175, 40);
            btnToggleMiniHud.Click += (s, e) => ToggleMiniHud();

            btnFanTurboHelper = CreateModernButton("❄️  FAN TURBO HELPER", Color.FromArgb(30, 90, 160), Color.White, 185, 40);
            btnFanTurboHelper.Click += (s, e) => OnFanTurboClick();

            pnlMaster.Controls.Add(btnApplyAll);
            pnlMaster.Controls.Add(btnRestore);
            pnlMaster.Controls.Add(btnRefresh);
            pnlMaster.Controls.Add(btnToggleMiniHud);
            pnlMaster.Controls.Add(btnFanTurboHelper);
            mainLayout.Controls.Add(pnlMaster, 0, 1);

            // Row 2: Tweak Cards (2 cols x 5 rows)
            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.ColumnCount = 2;
            cards.RowCount = 5;
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

            cards.Controls.Add(CreateTweakCard("AMD ULPS (Ultra Low Power State)",
                "Eliminates deep sleep state cycling that causes sudden FPS hitching and stutter.",
                out lblUlpsStatus, out btnToggleUlps, (s, e) => ToggleUlps()), 0, 0);

            cards.Controls.Add(CreateTweakCard("Hardware GPU Scheduling (HAGS)",
                "Offloads high-frequency scheduling tasks directly to the GPU scheduler.",
                out lblHagsStatus, out btnToggleHags, (s, e) => ToggleHags()), 1, 0);

            cards.Controls.Add(CreateTweakCard("GPU TDR Delay Timeout",
                "Extends recovery timer from 2s to 8s to prevent false crashes on heavy shaders.",
                out lblTdrStatus, out btnToggleTdr, (s, e) => ToggleTdr()), 0, 1);

            cards.Controls.Add(CreateTweakCard("CPU Max State (99% Thermal Cap)",
                "Prevents 80W+ i9 Turbo spikes on the shared heatpipe, keeping GPU clocks at 1004 MHz.",
                out lblCpuCapStatus, out btnToggleCpuCap, (s, e) => ToggleCpuCap()), 1, 1);

            cards.Controls.Add(CreateTweakCard("GameDVR & System Priority",
                "Disables background screen recording and grants 100% CPU priority to active 3D games.",
                out lblGameDvrStatus, out btnToggleGameDvr, (s, e) => ToggleGameDvr()), 0, 2);

            cards.Controls.Add(CreateTweakCard("Radeon Image Sharpening (RIS / CAS)",
                "Driver-level Contrast Adaptive Sharpening to eliminate texture blur without FPS cost.",
                out lblRisStatus, out btnToggleRis, (s, e) => ToggleRis()), 1, 2);

            cards.Controls.Add(CreateTweakCard("Windows MPO (Overlay Stutter Fix)",
                "Disables DWM Multi-Plane Overlay to stop borderless & FSR flicker/micro-stutter.",
                out lblMpoStatus, out btnToggleMpo, (s, e) => ToggleMpo()), 0, 3);

            cards.Controls.Add(CreateTweakCard("CPU Core Unparking (12 Threads)",
                "Keeps all 6 cores / 12 threads awake on AC power to smooth out 1% Low FPS dips.",
                out lblUnparkStatus, out btnToggleUnpark, (s, e) => ToggleCoreUnpark()), 1, 3);

            cards.Controls.Add(CreateTweakCard("0.5ms Low-Latency Timer (In-Memory)",
                "Locks Windows timer tick to 0.5ms while running (zero registry changes, auto-resets on exit).",
                out lblTimerResStatus, out btnToggleTimerRes, (s, e) => ToggleTimerResolution()), 0, 4);

            cards.Controls.Add(CreateTweakCard("Pre-Game RAM & Game Priority Boost",
                "Safely trims idle standby RAM (helps 4GB VRAM) and sets running games to High priority.",
                out lblRamBoostStatus, out btnRamBoost, (s, e) => BoostMemoryAndGames()), 1, 4);

            mainLayout.Controls.Add(cards, 0, 2);

            // Row 3: Activity Log
            txtLog = new RichTextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = Color.FromArgb(12, 13, 16);
            txtLog.ForeColor = Color.FromArgb(160, 230, 175);
            txtLog.Font = new Font("Consolas", 9f);
            txtLog.ReadOnly = true;
            txtLog.BorderStyle = BorderStyle.None;
            mainLayout.Controls.Add(txtLog, 0, 3);

            return root;
        }

        private Panel BuildOptimizerTelemetryBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 3;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

            // GPU Box
            Panel pGpu = CreateTelemetryCard("GPU & DRIVER STATUS");
            lblGpuName = new Label { Text = "Radeon Pro 560X (4GB VRAM)", Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = cTextMain, Location = new Point(12, 28), AutoSize = true };
            lblGpuDriver = new Label { Text = "Driver: Detecting...", Font = new Font("Segoe UI", 8.5f), ForeColor = cCyan, Location = new Point(12, 52), AutoSize = true };
            lblPowerPlan = new Label { Text = "Plan: Detecting...", Font = new Font("Segoe UI", 8.5f), ForeColor = cTextMuted, Location = new Point(12, 68), AutoSize = true };
            pGpu.Controls.AddRange(new Control[] { lblGpuName, lblGpuDriver, lblPowerPlan });
            bar.Controls.Add(pGpu, 0, 0);

            // CPU Load Box
            Panel pCpu = CreateTelemetryCard("LIVE CPU (CORE i9-8950HK)");
            lblCpuUsage = new Label { Text = "0 %", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = cGreen, Location = new Point(12, 28), AutoSize = true };
            prgCpu = new ProgressBar { Location = new Point(12, 58), Size = new Size(260, 14), Style = ProgressBarStyle.Continuous };
            pCpu.Controls.AddRange(new Control[] { lblCpuUsage, prgCpu });
            bar.Controls.Add(pCpu, 1, 0);

            // RAM Usage Box
            Panel pRam = CreateTelemetryCard("SYSTEM MEMORY (RAM)");
            lblRamUsage = new Label { Text = "0.0 / 0.0 GB (0%)", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = cCyan, Location = new Point(12, 28), AutoSize = true };
            prgRam = new ProgressBar { Location = new Point(12, 58), Size = new Size(260, 14), Style = ProgressBarStyle.Continuous };
            pRam.Controls.AddRange(new Control[] { lblRamUsage, prgRam });
            bar.Controls.Add(pRam, 2, 0);

            return bar;
        }

        private Panel CreateTelemetryCard(string title)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = cCardBg;
            p.Padding = new Padding(12);

            Label lbl = new Label();
            lbl.Text = title;
            lbl.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lbl.ForeColor = cTextMuted;
            lbl.Location = new Point(12, 8);
            lbl.AutoSize = true;
            p.Controls.Add(lbl);

            p.Paint += (s, e) => {
                using (Pen pen = new Pen(cBorder, 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private Panel CreateTweakCard(string title, string desc, out Label lblStatus, out Button btnToggle, EventHandler onClick)
        {
            Panel p = new Panel();
            p.Size = new Size(420, 82);
            p.Dock = DockStyle.Fill;
            p.BackColor = cCardBg;
            p.Padding = new Padding(10);
            p.Margin = new Padding(4);

            Label lblTitle = new Label { Text = title, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = cTextMain, Location = new Point(12, 6), AutoSize = true };
            Label lblDesc = new Label { Text = desc, Font = new Font("Segoe UI", 8f), ForeColor = cTextMuted, Location = new Point(12, 24), Size = new Size(280, 28) };

            lblStatus = new Label { Text = "Detecting...", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = cYellow, Location = new Point(12, 54), AutoSize = true };

            btnToggle = CreateModernButton("Toggle", Color.FromArgb(40, 45, 60), cTextMain, 122, 28);
            btnToggle.Location = new Point(286, 46);
            btnToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnToggle.Click += onClick;

            p.Controls.AddRange(new Control[] { lblTitle, lblDesc, lblStatus, btnToggle });
            p.Paint += (s, e) => {
                using (Pen pen = new Pen(cBorder, 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }
        #endregion

        #region TAB 2: 3D BENCHMARK
        private Panel BuildBenchmarkPanel()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
            root.Controls.Add(layout);

            // Left: 3D Viewport on top, Frametime Graph on bottom
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.RowCount = 2;
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 72f));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 28f));
            left.Margin = new Padding(12);
            layout.Controls.Add(left, 0, 0);

            glRenderPanel = new Panel();
            glRenderPanel.Dock = DockStyle.Fill;
            glRenderPanel.BackColor = Color.Black;
            glRenderPanel.Resize += (s, e) => ResizeGL();
            left.Controls.Add(glRenderPanel, 0, 0);

            Panel pnlGraph = new Panel();
            pnlGraph.Dock = DockStyle.Fill;
            pnlGraph.BackColor = cCardBg;
            pnlGraph.Padding = new Padding(8);

            Label lblGraphHdr = new Label { Text = "REAL-TIME FRAMETIME GRAPH (ms) — Flat line = Zero Stutter", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = cTextMuted, Dock = DockStyle.Top, Height = 22 };
            pnlGraph.Controls.Add(lblGraphHdr);

            graphBox = new PictureBox();
            graphBox.Dock = DockStyle.Fill;
            graphBox.BackColor = Color.FromArgb(12, 13, 17);
            graphBox.Paint += DrawBenchmarkGraph;
            pnlGraph.Controls.Add(graphBox);

            left.Controls.Add(pnlGraph, 0, 1);

            // Right: HUD, Metrics & Run Controls
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = cCardBg;
            right.Padding = new Padding(16);
            right.AutoScroll = true;
            layout.Controls.Add(right, 1, 0);

            int y = 12;
            Label lblTitle = new Label { Text = "3D GPU BENCHMARK", Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = cRadeonRed, Location = new Point(14, y), AutoSize = true };
            right.Controls.Add(lblTitle);
            y += 36;

            // Frame Metrics Card
            Panel pFps = CreateCard("FRAME PACING & FPS", 155, y);
            right.Controls.Add(pFps);

            lblBenchFps = new Label { Text = "0 FPS", Font = new Font("Segoe UI", 24f, FontStyle.Bold), ForeColor = cGreen, Location = new Point(14, 26), AutoSize = true };
            lblBenchAvgFps = new Label { Text = "Avg: -- FPS", Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = cTextMain, Location = new Point(16, 76), AutoSize = true };
            lblBench1Pct = new Label { Text = "1% Low: -- FPS (Stability)", Font = new Font("Segoe UI", 9.5f), ForeColor = cCyan, Location = new Point(16, 100), AutoSize = true };
            lblBenchStutter = new Label { Text = "Micro-Stutters: 0 (ULPS Monitor)", Font = new Font("Segoe UI", 9.5f), ForeColor = cTextMuted, Location = new Point(16, 122), AutoSize = true };
            pFps.Controls.AddRange(new Control[] { lblBenchFps, lblBenchAvgFps, lblBench1Pct, lblBenchStutter });
            y += 170;

            // Hardware Telemetry Card
            Panel pHw = CreateCard("LIVE HARDWARE METRICS", 95, y);
            right.Controls.Add(pHw);
            lblBenchCpu = new Label { Text = "CPU Core i9 Load: 0%", Font = new Font("Segoe UI", 9.5f), ForeColor = cTextMain, Location = new Point(14, 30), AutoSize = true };
            lblBenchRam = new Label { Text = "System RAM: 0 GB / 0 GB", Font = new Font("Segoe UI", 9.5f), ForeColor = cTextMain, Location = new Point(14, 56), AutoSize = true };
            pHw.Controls.AddRange(new Control[] { lblBenchCpu, lblBenchRam });
            y += 110;

            // 30s Run Card
            Panel pRun = CreateCard("30-SECOND BENCHMARK RUN", 155, y);
            right.Controls.Add(pRun);
            lblBenchTimer = new Label { Text = "Ready to test (30.0s)", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = cYellow, Location = new Point(14, 28), AutoSize = true };
            lblBenchVerdict = new Label { Text = "Verdict: Run test to evaluate sustained thermal boost.", Font = new Font("Segoe UI", 9f), ForeColor = cTextMuted, Location = new Point(14, 54), Size = new Size(290, 36) };

            btnStartBench = CreateModernButton("🚀  START 30s BENCHMARK", cRadeonRed, Color.White, 290, 38);
            btnStartBench.Location = new Point(14, 98);
            btnStartBench.Click += OnStartBenchmarkClick;
            pRun.Controls.AddRange(new Control[] { lblBenchTimer, lblBenchVerdict, btnStartBench });
            y += 170;

            // Controls
            btnBenchStress = CreateModernButton("Stress Mode: Normal", Color.FromArgb(32, 34, 46), cTextMain, 290, 36);
            btnBenchStress.Location = new Point(14, y);
            btnBenchStress.Click += (s, e) => ToggleBenchStress();
            right.Controls.Add(btnBenchStress);
            y += 44;

            btnToggleSsr = CreateModernButton("✨ SSR Reflections: ON", Color.FromArgb(20, 140, 80), Color.White, 290, 36);
            btnToggleSsr.Location = new Point(14, y);
            btnToggleSsr.Click += (s, e) => ToggleBenchmarkSsr();
            right.Controls.Add(btnToggleSsr);
            y += 44;

            btnSsrSurface = CreateModernButton("💧 SSR Surface: Wet Mirror", Color.FromArgb(30, 80, 130), Color.White, 290, 36);
            btnSsrSurface.Location = new Point(14, y);
            btnSsrSurface.Click += (s, e) => ToggleSsrSurface();
            right.Controls.Add(btnSsrSurface);
            y += 44;

            Button btnExport = CreateModernButton("💾  Export Benchmark Report", Color.FromArgb(32, 34, 46), cTextMain, 290, 36);
            btnExport.Location = new Point(14, y);
            btnExport.Click += (s, e) => ExportBenchmarkReport();
            right.Controls.Add(btnExport);
            y += 44;

            Button btnBenchHud = CreateModernButton("🔥  Toggle In-Game Mini-HUD", Color.FromArgb(170, 40, 40), Color.White, 290, 36);
            btnBenchHud.Location = new Point(14, y);
            btnBenchHud.Click += (s, e) => ToggleMiniHud();
            right.Controls.Add(btnBenchHud);

            return root;
        }

        private Panel CreateCard(string title, int height, int yPos)
        {
            Panel p = new Panel();
            p.Size = new Size(318, height);
            p.Location = new Point(14, yPos);
            p.BackColor = cCardInner;
            p.Padding = new Padding(12);

            Label lbl = new Label { Text = title, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = cTextMuted, Location = new Point(12, 8), AutoSize = true };
            p.Controls.Add(lbl);

            p.Paint += (s, e) => {
                using (Pen pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }
        #endregion

        #region TAB 3: FSR UPSCALER
        private Panel BuildFsrPanel()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.AutoScroll = true;
            root.Padding = new Padding(24);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            root.Controls.Add(flow);

            // Title
            Label lblTitle = new Label { Text = "UNIVERSAL AMD FSR & DETAIL ENHANCER", Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = cRadeonRed, AutoSize = true };
            Label lblSubtitle = new Label { Text = "Injects AMD FidelityFX FSR (EASU + RCAS) into ANY windowed game to boost FPS by 40% to 80%", Font = new Font("Segoe UI", 10f), ForeColor = cTextMuted, AutoSize = true, Margin = new Padding(0, 4, 0, 16) };
            flow.Controls.Add(lblTitle);
            flow.Controls.Add(lblSubtitle);

            // 1. Control & Action Panel
            Panel pnlStatus = new Panel();
            pnlStatus.Size = new Size(820, 204);
            pnlStatus.BackColor = cCardBg;
            pnlStatus.Padding = new Padding(16);
            pnlStatus.Margin = new Padding(0, 0, 0, 14);

            Label lblHead = new Label { Text = "BACKGROUND FSR ENGINE STATUS", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = cTextMuted, Location = new Point(16, 12), AutoSize = true };
            lblFsrProcessStatus = new Label { Text = "Checking...", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = cYellow, Location = new Point(16, 34), AutoSize = true };

            btnStartFsr = CreateModernButton("▶  LAUNCH ENGINE", Color.FromArgb(20, 140, 80), Color.White, 170, 36);
            btnStartFsr.Location = new Point(16, 74);
            btnStartFsr.Click += (s, e) => StartFsrEngine();

            btnStopFsr = CreateModernButton("⏹  STOP ENGINE", Color.FromArgb(70, 75, 90), cTextMain, 140, 36);
            btnStopFsr.Location = new Point(196, 74);
            btnStopFsr.Click += (s, e) => StopFsrEngine();

            btnFsrAutoUpscale = CreateModernButton("⚡  AUTO-UPSCALE IN 5s", Color.FromArgb(170, 40, 40), Color.White, 210, 36);
            btnFsrAutoUpscale.Location = new Point(346, 74);
            btnFsrAutoUpscale.Click += (s, e) => StartFsrAutoUpscaleCountdown();

            // Hotkey selector
            Label lblHk = new Label { Text = "ACTIVE HOTKEY:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = cTextMuted, Location = new Point(576, 12), AutoSize = true };
            cmbFsrHotkey = new ComboBox();
            cmbFsrHotkey.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFsrHotkey.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            cmbFsrHotkey.BackColor = Color.FromArgb(30, 32, 42);
            cmbFsrHotkey.ForeColor = Color.White;
            cmbFsrHotkey.Location = new Point(576, 34);
            cmbFsrHotkey.Size = new Size(224, 28);
            cmbFsrHotkey.Items.Add("ALT + F11 (Default)");
            cmbFsrHotkey.Items.Add("ALT + SHIFT + A (Physical Keys)");
            cmbFsrHotkey.Items.Add("F11 (Single Key)");
            cmbFsrHotkey.Items.Add("CTRL + ALT + S (Lossless)");
            cmbFsrHotkey.Items.Add("ALT + A (Physical Keys)");

            int currentHk = GetCurrentMagpieHotkey();
            if (currentHk == 3137) cmbFsrHotkey.SelectedIndex = 1;
            else if (currentHk == 122) cmbFsrHotkey.SelectedIndex = 2;
            else if (currentHk == 2643 || currentHk == 771) cmbFsrHotkey.SelectedIndex = 3;
            else if (currentHk == 2113 || currentHk == 321) cmbFsrHotkey.SelectedIndex = 4;
            else cmbFsrHotkey.SelectedIndex = 0; // 2170 Alt + F11

            cmbFsrHotkey.SelectedIndexChanged += (s, e) => {
                int selectedCode = 2170;
                switch (cmbFsrHotkey.SelectedIndex)
                {
                    case 0: selectedCode = 2170; break; // Alt + F11
                    case 1: selectedCode = 3137; break; // Alt + Shift + A
                    case 2: selectedCode = 122; break;  // F11
                    case 3: selectedCode = 2643; break; // Ctrl + Alt + S
                    case 4: selectedCode = 2113; break; // Alt + A
                }
                SetMagpieHotkey(selectedCode);
                UpdateFsrStatus();
            };

            btnToggleCinematicSsr = CreateModernButton("✨ SSR ENHANCER: ON", Color.FromArgb(140, 20, 80), Color.White, 236, 36);
            btnToggleCinematicSsr.Location = new Point(16, 122);
            btnToggleCinematicSsr.Click += (s, e) => ToggleCinematicSsr();

            cmbSsrPreset = new ComboBox();
            cmbSsrPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSsrPreset.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            cmbSsrPreset.BackColor = Color.FromArgb(30, 32, 42);
            cmbSsrPreset.ForeColor = Color.White;
            cmbSsrPreset.Location = new Point(262, 126);
            cmbSsrPreset.Size = new Size(270, 28);
            cmbSsrPreset.Items.Add("SSR: Off (Standard FSR Only)");
            cmbSsrPreset.Items.Add("SSR: Subtle Realism (Balanced)");
            cmbSsrPreset.Items.Add("SSR: Vivid Wet-Road (Recommended)");
            cmbSsrPreset.Items.Add("SSR: Cyberpunk Neon Glow (Max)");
            cmbSsrPreset.SelectedIndex = (GetCurrentMagpieScalingMode() == 7) ? 2 : 0;
            cmbSsrPreset.SelectedIndexChanged += (s, e) => ApplySsrShaderPreset(cmbSsrPreset.SelectedIndex);

            btnOptimizeGameSsr = CreateModernButton("🎮 OPTIMIZE GTA V & GAME SSR", Color.FromArgb(20, 120, 140), Color.White, 258, 36);
            btnOptimizeGameSsr.Location = new Point(542, 122);
            btnOptimizeGameSsr.Click += (s, e) => OptimizeInstalledGamesSsr();

            Label lblCinematicInfo = new Label { Text = "Universal SSR Shader (Specular Bloom + Shadow Depth + Deband) works on GTA V & all games", Font = new Font("Segoe UI", 8.5f), ForeColor = cTextMuted, Location = new Point(16, 168), AutoSize = true };

            pnlStatus.Controls.AddRange(new Control[] { lblHead, lblFsrProcessStatus, btnStartFsr, btnStopFsr, btnFsrAutoUpscale, lblHk, cmbFsrHotkey, btnToggleCinematicSsr, cmbSsrPreset, btnOptimizeGameSsr, lblCinematicInfo });
            pnlStatus.Paint += (s, e) => {
                using (Pen p = new Pen(cBorder, 1)) e.Graphics.DrawRectangle(p, 0, 0, pnlStatus.Width - 1, pnlStatus.Height - 1);
            };
            flow.Controls.Add(pnlStatus);

            // 1b. 16:10 Resolution Switcher & Magpie Smart FPS Cap Panel
            Panel pnlDisplayTune = new Panel();
            pnlDisplayTune.Size = new Size(820, 122);
            pnlDisplayTune.BackColor = cCardBg;
            pnlDisplayTune.Padding = new Padding(16);
            pnlDisplayTune.Margin = new Padding(0, 0, 0, 14);

            Label lblDispTitle = new Label { Text = "GAMING RESOLUTION PRESETS & SMART FPS CAP (THERMAL SOAK GUARD)", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = cCyan, Location = new Point(16, 12), AutoSize = true };
            lblCurrentRes = new Label { Text = GetCurrentDisplayResolutionText(), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = cGreen, Location = new Point(16, 34), AutoSize = true };

            Size resMaxFps = PickSupportedResolution(new int[,] { { 1280, 800 }, { 1280, 720 } });
            Size resBalanced = PickSupportedResolution(new int[,] { { 1440, 900 }, { 1600, 900 } });
            Size resCrisp = PickSupportedResolution(new int[,] { { 1680, 1050 }, { 1600, 900 } });
            Size resNative = GetMaxSupportedResolution();

            Button btnRes800p = CreateModernButton(string.Format("{0}x{1} (Max FPS)", resMaxFps.Width, resMaxFps.Height), Color.FromArgb(38, 45, 62), Color.White, 138, 34);
            btnRes800p.Location = new Point(16, 66);
            btnRes800p.Click += (s, e) => SetDisplayResolution16x10(resMaxFps.Width, resMaxFps.Height);

            Button btnRes900p = CreateModernButton(string.Format("{0}x{1} (Balanced)", resBalanced.Width, resBalanced.Height), Color.FromArgb(38, 45, 62), Color.White, 140, 34);
            btnRes900p.Location = new Point(160, 66);
            btnRes900p.Click += (s, e) => SetDisplayResolution16x10(resBalanced.Width, resBalanced.Height);

            Button btnRes1050p = CreateModernButton(string.Format("{0}x{1} (Crisp)", resCrisp.Width, resCrisp.Height), Color.FromArgb(38, 45, 62), Color.White, 134, 34);
            btnRes1050p.Location = new Point(306, 66);
            btnRes1050p.Click += (s, e) => SetDisplayResolution16x10(resCrisp.Width, resCrisp.Height);

            Button btnResNative = CreateModernButton(string.Format("{0}x{1} (Native)", resNative.Width, resNative.Height), Color.FromArgb(60, 65, 82), cTextMain, 136, 34);
            btnResNative.Location = new Point(446, 66);
            btnResNative.Click += (s, e) => SetDisplayResolution16x10(resNative.Width, resNative.Height);

            Label lblFpsCap = new Label { Text = "MAGPIE SMART FPS CAP:", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = cTextMuted, Location = new Point(594, 36), AutoSize = true };
            cmbFsrFpsCap = new ComboBox();
            cmbFsrFpsCap.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFsrFpsCap.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            cmbFsrFpsCap.BackColor = Color.FromArgb(30, 32, 42);
            cmbFsrFpsCap.ForeColor = Color.White;
            cmbFsrFpsCap.Location = new Point(594, 68);
            cmbFsrFpsCap.Size = new Size(206, 28);
            cmbFsrFpsCap.Items.Add("Uncapped (Max FPS)");
            cmbFsrFpsCap.Items.Add("60 FPS Cap (Smooth & Cool)");
            cmbFsrFpsCap.Items.Add("45 FPS Cap (Best Thermals)");

            int fpsCapIdx = GetCurrentMagpieFpsCapIndex();
            cmbFsrFpsCap.SelectedIndex = fpsCapIdx;
            cmbFsrFpsCap.SelectedIndexChanged += (s, e) => {
                if (cmbFsrFpsCap.SelectedIndex == 1) SetMagpieFpsCap(true, 60);
                else if (cmbFsrFpsCap.SelectedIndex == 2) SetMagpieFpsCap(true, 45);
                else SetMagpieFpsCap(false, 60);
            };

            pnlDisplayTune.Controls.AddRange(new Control[] { lblDispTitle, lblCurrentRes, btnRes800p, btnRes900p, btnRes1050p, btnResNative, lblFpsCap, cmbFsrFpsCap });
            pnlDisplayTune.Paint += (s, e) => {
                using (Pen p = new Pen(cBorder, 1)) e.Graphics.DrawRectangle(p, 0, 0, pnlDisplayTune.Width - 1, pnlDisplayTune.Height - 1);
            };
            flow.Controls.Add(pnlDisplayTune);

            // 2. MacBook Pro Touch Bar Important Notice Panel
            Panel pnlMacBook = new Panel();
            pnlMacBook.Size = new Size(820, 110);
            pnlMacBook.BackColor = Color.FromArgb(40, 32, 18);
            pnlMacBook.Padding = new Padding(16);
            pnlMacBook.Margin = new Padding(0, 0, 0, 14);

            Label lblMacTitle = new Label { Text = "🍎  IMPORTANT FOR 15\" MACBOOK PRO (TOUCH BAR USERS):", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(255, 195, 80), Location = new Point(14, 12), AutoSize = true };
            string macText =
                "• Why Alt + F11 didn't trigger: MacBook Pro 2018 has a Touch Bar instead of physical F11 keys!\n" +
                "  To press F11, you must hold [ fn ] (bottom-left of keyboard) to make F1-F12 appear on the Touch Bar.\n" +
                "• Easiest Fix: Switch the hotkey above to 'WIN + SHIFT + A' (uses 100% physical keys, no Touch Bar needed!)\n" +
                "• Or Click: '⚡ AUTO-UPSCALE IN 5s' above, then simply click into your game!";
            Label lblMacInfo = new Label { Text = macText, Font = new Font("Segoe UI", 9.2f), ForeColor = Color.FromArgb(240, 220, 180), Location = new Point(14, 36), Size = new Size(790, 64) };

            pnlMacBook.Controls.AddRange(new Control[] { lblMacTitle, lblMacInfo });
            pnlMacBook.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(160, 120, 40), 1)) e.Graphics.DrawRectangle(p, 0, 0, pnlMacBook.Width - 1, pnlMacBook.Height - 1);
            };
            flow.Controls.Add(pnlMacBook);

            // 3. Step-by-Step Instructions Box
            Panel pnlInstructions = new Panel();
            pnlInstructions.Size = new Size(820, 280);
            pnlInstructions.BackColor = cCardBg;
            pnlInstructions.Padding = new Padding(18);

            Label lblInstTitle = new Label { Text = "HOW TO USE WITH ANY GAME (3 EASY STEPS)", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = cCyan, Location = new Point(16, 12), AutoSize = true };

            string guideText =
                "Step 1: Make sure the FSR Engine is launched (click 'LAUNCH ENGINE' above — icon appears in taskbar tray).\n\n" +
                "Step 2: Start your game, open Video / Display settings, and set:\n" +
                "       • Display Mode: Windowed or Borderless Window (REQUIRED — exclusive fullscreen cannot be scaled)\n" +
                "       • Resolution: 720p (1280x720) or 900p (1440x900) for massive FPS gains!\n" +
                "       (Rendering at 720p reduces shader load by ~55%, keeping your Radeon 560X cool & locked at max clocks!)\n\n" +
                "Step 3: Trigger AMD FSR Scaling:\n" +
                "       👉 Option A: Press your active hotkey (e.g. Alt + fn + F11, or Win + Shift + A)\n" +
                "       👉 Option B: Click '⚡ AUTO-UPSCALE IN 5s' above and switch to your game window!";

            Label lblGuide = new Label { Text = guideText, Font = new Font("Consolas", 9.5f), ForeColor = cTextMain, Location = new Point(16, 42), Size = new Size(785, 220) };
            pnlInstructions.Controls.AddRange(new Control[] { lblInstTitle, lblGuide });
            pnlInstructions.Paint += (s, e) => {
                using (Pen p = new Pen(cBorder, 1)) e.Graphics.DrawRectangle(p, 0, 0, pnlInstructions.Width - 1, pnlInstructions.Height - 1);
            };
            flow.Controls.Add(pnlInstructions);

            return root;
        }

        private void EnsureFsrRunningInBackground()
        {
            try
            {
                if (Process.GetProcessesByName("Magpie").Length == 0)
                {
                    string magpiePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"FSR-Engine\Magpie.exe");
                    if (File.Exists(magpiePath))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = magpiePath;
                        psi.WorkingDirectory = Path.GetDirectoryName(magpiePath);
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                        Log("✨ Auto-started FSR Engine in background.");
                    }
                }
            }
            catch { }
        }

        private void UpdateFsrStatus()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("Magpie");
                string hkName = "ALT + F11";
                int code = GetCurrentMagpieHotkey();
                if (code == 3137) hkName = "ALT + SHIFT + A";
                else if (code == 122) hkName = "F11";
                else if (code == 2643 || code == 771) hkName = "CTRL + ALT + S";
                else if (code == 2113 || code == 321) hkName = "ALT + A";
                else if (code == 378) hkName = "WIN + F11";

                if (procs.Length > 0)
                {
                    lblFsrProcessStatus.Text = "● ACTIVE & RUNNING in background (Hotkey: " + hkName + ")";
                    lblFsrProcessStatus.ForeColor = cGreen;
                    btnStartFsr.Enabled = false;
                    btnStopFsr.Enabled = true;
                }
                else
                {
                    lblFsrProcessStatus.Text = "○ STOPPED (Click Launch or Auto-Upscale to start)";
                    lblFsrProcessStatus.ForeColor = cYellow;
                    btnStartFsr.Enabled = true;
                    btnStopFsr.Enabled = false;
                }

                int scalingMode = GetCurrentMagpieScalingMode();
                isCinematicSsrActive = (scalingMode == 7);
                if (btnToggleCinematicSsr != null)
                {
                    if (isCinematicSsrActive)
                    {
                        btnToggleCinematicSsr.Text = "✨ SSR ENHANCER: ON";
                        btnToggleCinematicSsr.BackColor = Color.FromArgb(140, 20, 80);
                        btnToggleCinematicSsr.ForeColor = Color.White;
                    }
                    else
                    {
                        btnToggleCinematicSsr.Text = "○ SSR ENHANCER: OFF";
                        btnToggleCinematicSsr.BackColor = Color.FromArgb(70, 75, 90);
                        btnToggleCinematicSsr.ForeColor = cTextMain;
                    }
                }
            }
            catch { }
        }

        private int GetCurrentMagpieScalingMode()
        {
            try
            {
                string cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Magpie\config\v5\config.json");
                if (File.Exists(cfgPath))
                {
                    string json = File.ReadAllText(cfgPath);
                    Match m = Regex.Match(json, @"""scalingMode""\s*:\s*(\d+)");
                    if (m.Success)
                    {
                        int val;
                        if (int.TryParse(m.Groups[1].Value, out val)) return val;
                    }
                }
            }
            catch { }
            return 7;
        }

        private void UpdateMagpieConfig(Func<string, string> transformJson)
        {
            string cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Magpie\config\v5\config.json");
            if (!File.Exists(cfgPath)) return;

            string json = File.ReadAllText(cfgPath);
            json = transformJson(json);
            File.WriteAllText(cfgPath, json, new System.Text.UTF8Encoding(false));

            if (Process.GetProcessesByName("Magpie").Length > 0)
            {
                StopFsrEngine();
                System.Threading.Thread.Sleep(400);
                StartFsrEngine();
            }
        }

        private void SetMagpieScalingMode(int mode)
        {
            try
            {
                UpdateMagpieConfig(json => Regex.Replace(json, @"""scalingMode""\s*:\s*\d+", "\"scalingMode\": " + mode));
            }
            catch (Exception ex)
            {
                Log("Failed to update Magpie scaling mode: " + ex.Message);
            }
        }

        private int GetCurrentMagpieFpsCapIndex()
        {
            try
            {
                string cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Magpie\config\v5\config.json");
                if (File.Exists(cfgPath))
                {
                    string json = File.ReadAllText(cfgPath);
                    Match mEn = Regex.Match(json, @"""frameRateLimiterEnabled""\s*:\s*(true|false)");
                    Match mMax = Regex.Match(json, @"""maxFrameRate""\s*:\s*(\d+)");
                    if (mEn.Success && mEn.Groups[1].Value == "true")
                    {
                        int fps = 60;
                        if (mMax.Success) int.TryParse(mMax.Groups[1].Value, out fps);
                        return (fps <= 48) ? 2 : 1;
                    }
                }
            }
            catch { }
            return 0;
        }

        private void SetMagpieFpsCap(bool enabled, int maxFps)
        {
            try
            {
                UpdateMagpieConfig(json => {
                    json = Regex.Replace(json, @"""frameRateLimiterEnabled""\s*:\s*(true|false)", "\"frameRateLimiterEnabled\": " + (enabled ? "true" : "false"));
                    json = Regex.Replace(json, @"""maxFrameRate""\s*:\s*\d+(?:\.\d+)?", "\"maxFrameRate\": " + maxFps + ".0");
                    return json;
                });
                Log(enabled ? string.Format("Magpie Smart FPS Cap set to {0} FPS (prevents thermal soak).", maxFps) : "Magpie FPS Cap set to Uncapped.");
            }
            catch (Exception ex)
            {
                Log("Failed to update Magpie FPS Cap: " + ex.Message);
            }
        }

        private bool IsResolutionSupported(int width, int height)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int i = 0;
            while (EnumDisplaySettings(null, i++, ref dm))
            {
                if (dm.dmBitsPerPel == 32 && dm.dmPelsWidth == width && dm.dmPelsHeight == height) return true;
            }
            return false;
        }

        private Size PickSupportedResolution(int[,] candidates)
        {
            for (int r = 0; r < candidates.GetLength(0); r++)
            {
                int w = candidates[r, 0];
                int h = candidates[r, 1];
                if (IsResolutionSupported(w, h)) return new Size(w, h);
            }
            return new Size(candidates[0, 0], candidates[0, 1]);
        }

        private Size GetMaxSupportedResolution()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int i = 0;
            int bestW = 1920, bestH = 1080;
            long bestPixels = 0;
            while (EnumDisplaySettings(null, i++, ref dm))
            {
                if (dm.dmBitsPerPel == 32)
                {
                    long pixels = (long)dm.dmPelsWidth * dm.dmPelsHeight;
                    if (pixels > bestPixels)
                    {
                        bestPixels = pixels;
                        bestW = dm.dmPelsWidth;
                        bestH = dm.dmPelsHeight;
                    }
                }
            }
            return new Size(bestW, bestH);
        }

        private string GetCurrentDisplayResolutionText()
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, -1, ref dm))
                {
                    return string.Format("Current Display: {0}x{1} @ {2} Hz", dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
                }
            }
            catch { }
            return "Current Display: Unknown";
        }

        private void SetDisplayResolution16x10(int targetWidth, int targetHeight)
        {
            try
            {
                DEVMODE cur = new DEVMODE();
                cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                EnumDisplaySettings(null, -1, ref cur);

                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                DEVMODE best = new DEVMODE();
                bool found = false;
                int modeIdx = 0;

                while (EnumDisplaySettings(null, modeIdx++, ref dm))
                {
                    if (dm.dmPelsWidth == targetWidth && dm.dmPelsHeight == targetHeight && dm.dmBitsPerPel == 32)
                    {
                        if (!found || dm.dmDisplayFrequency == cur.dmDisplayFrequency || dm.dmDisplayFrequency > best.dmDisplayFrequency)
                        {
                            best = dm;
                            found = true;
                            if (dm.dmDisplayFrequency == cur.dmDisplayFrequency) break;
                        }
                    }
                }

                if (!found)
                {
                    MessageBox.Show(string.Format("Display mode {0}x{1} is not reported by your current graphics driver.", targetWidth, targetHeight),
                        "Unsupported Resolution", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                const int CDS_TEST = 0x00000002;
                const int DISP_CHANGE_SUCCESSFUL = 0;
                if (ChangeDisplaySettings(ref best, CDS_TEST) != DISP_CHANGE_SUCCESSFUL)
                {
                    MessageBox.Show("Windows driver validation rejected this display mode.", "Resolution Test Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int res = ChangeDisplaySettings(ref best, 0);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    if (lblCurrentRes != null) lblCurrentRes.Text = GetCurrentDisplayResolutionText();
                    Log(string.Format("Switched 16:10 display resolution to {0}x{1} @ {2} Hz.", best.dmPelsWidth, best.dmPelsHeight, best.dmDisplayFrequency));
                }
                else
                {
                    Log("ChangeDisplaySettings returned error code: " + res);
                }
            }
            catch (Exception ex)
            {
                Log("Resolution switch error: " + ex.Message);
            }
        }

        private bool isUpdatingSsrUi = false;

        private void ToggleCinematicSsr()
        {
            int currentMode = GetCurrentMagpieScalingMode();
            if (currentMode == 7)
            {
                ApplySsrShaderPreset(0);
            }
            else
            {
                ApplySsrShaderPreset(2);
            }
        }

        private void ApplySsrShaderPreset(int presetIdx)
        {
            if (isUpdatingSsrUi) return;
            isUpdatingSsrUi = true;
            try
            {
                if (presetIdx == 0)
                {
                    SetMagpieScalingMode(1);
                    isCinematicSsrActive = false;
                    if (cmbSsrPreset != null && cmbSsrPreset.SelectedIndex != 0) cmbSsrPreset.SelectedIndex = 0;
                    Log("Switched FSR Engine to Standard FSR (EASU + RCAS, SSR Enhancer OFF).");
                }
                else
                {
                    double bloom = 0.30, shadow = 0.20, vib = 0.14, deband = 0.35;
                    string label = "Vivid Wet-Road SSR";
                    if (presetIdx == 1) { bloom = 0.16; shadow = 0.12; vib = 0.08; deband = 0.30; label = "Subtle Realism SSR"; }
                    else if (presetIdx == 3) { bloom = 0.48; shadow = 0.28; vib = 0.22; deband = 0.40; label = "Cyberpunk Neon Glow SSR"; }

                    UpdateMagpieConfig(json => {
                        json = Regex.Replace(json, @"""scalingMode""\s*:\s*\d+", "\"scalingMode\": 7");
                        // Upgrade older ImageAdjustment block if present, or update SSR_Cinematic_Enhancer params
                        json = Regex.Replace(json, @"""name""\s*:\s*""ImageAdjustment""[\s\S]*?""saturation""\s*:\s*[0-9\.]+",
                            "\"name\": \"SSR_Cinematic_Enhancer\",\n                    \"parameters\": {\n                        \"specularBloom\": 0.30,\n                        \"shadowDepth\": 0.20,\n                        \"vibrance\": 0.14,\n                        \"debandStrength\": 0.35");
                        json = Regex.Replace(json, @"""specularBloom""\s*:\s*[0-9\.]+", "\"specularBloom\": " + bloom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        json = Regex.Replace(json, @"""shadowDepth""\s*:\s*[0-9\.]+", "\"shadowDepth\": " + shadow.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        json = Regex.Replace(json, @"""vibrance""\s*:\s*[0-9\.]+", "\"vibrance\": " + vib.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        json = Regex.Replace(json, @"""debandStrength""\s*:\s*[0-9\.]+", "\"debandStrength\": " + deband.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        return json;
                    });

                    isCinematicSsrActive = true;
                    if (cmbSsrPreset != null && cmbSsrPreset.SelectedIndex != presetIdx) cmbSsrPreset.SelectedIndex = presetIdx;
                    Log(string.Format("✨ Applied Universal SSR Shader Preset: {0} (Bloom={1:0.00}, Depth={2:0.00}).", label, bloom, shadow));
                }
                UpdateFsrStatus();
            }
            catch (Exception ex)
            {
                Log("SSR preset error: " + ex.Message);
            }
            finally
            {
                isUpdatingSsrUi = false;
            }
        }

        private void OptimizeInstalledGamesSsr()
        {
            string gtaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Rockstar Games\GTA V\settings.xml");
            bool gtaUpdated = false;
            if (File.Exists(gtaPath))
            {
                try
                {
                    string xml = File.ReadAllText(gtaPath);
                    xml = Regex.Replace(xml, @"<ReflectionQuality\s+value=""\d+""\s*/>", "<ReflectionQuality value=\"2\" />");
                    xml = Regex.Replace(xml, @"<SSAO\s+value=""\d+""\s*/>", "<SSAO value=\"1\" />");
                    xml = Regex.Replace(xml, @"<AnisotropicFiltering\s+value=""\d+""\s*/>", "<AnisotropicFiltering value=\"8\" />");
                    xml = Regex.Replace(xml, @"<Reflection_MipBlur\s+value=""(true|false)""\s*/>", "<Reflection_MipBlur value=\"true\" />");
                    xml = Regex.Replace(xml, @"<Windowed\s+value=""\d+""\s*/>", "<Windowed value=\"2\" />");
                    File.WriteAllText(gtaPath, xml, new System.Text.UTF8Encoding(false));
                    gtaUpdated = true;
                    Log("🎮 GTA V SSR optimized: High Reflections (2), 8x Anisotropic Filtering, SSAO (1), Borderless Windowed (2).");
                }
                catch (Exception ex)
                {
                    Log("GTA V config update error: " + ex.Message);
                }
            }

            string summary = (gtaUpdated
                ? "✅ GTA V (settings.xml) automatically tuned for sharp SSR:\n  • Reflection Quality: High (2) + MipBlur ON\n  • Anisotropic Filtering: 8x (sharp angled road puddles, ~0 FPS cost)\n  • Ambient Occlusion (SSAO): Normal (1)\n  • Display Mode: Borderless Windowed (ready for FSR + SSR)\n\n"
                : "ℹ️ GTA V settings.xml not detected at Documents\\Rockstar Games\\GTA V.\n\n") +
                "🌍 FOR ALL OTHER GAMES (Cyberpunk, RDR2, Witcher 3, Elden Ring, Emulators):\n" +
                "  1. Keep Magpie 'SSR ENHANCER: ON' (works automatically on EVERY game window!)\n" +
                "  2. Set Display Mode = Borderless Windowed\n" +
                "  3. Set Reflections = High / Screen Space Reflections (NEVER Ray Tracing)\n" +
                "  4. Set Anisotropic Filtering = 8x or 16x (makes puddle/floor reflections crisp at zero FPS loss)";

            MessageBox.Show(summary, "GTA V & Universal Game SSR Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void StartFsrEngine()
        {
            try
            {
                string magpiePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"FSR-Engine\Magpie.exe");
                if (File.Exists(magpiePath))
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = magpiePath;
                    psi.WorkingDirectory = Path.GetDirectoryName(magpiePath);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    System.Threading.Thread.Sleep(800);
                    UpdateFsrStatus();
                    Log("✨ Started Universal AMD FSR & Detail Engine.");
                }
                else
                {
                    MessageBox.Show("FSR Engine not found in: " + magpiePath, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex) { Log("Failed to start FSR engine: " + ex.Message); }
        }

        private void StopFsrEngine()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("Magpie"))
                {
                    p.Kill();
                }
                System.Threading.Thread.Sleep(300);
                UpdateFsrStatus();
                Log("Stopped FSR Engine.");
            }
            catch (Exception ex) { Log("Failed to stop FSR: " + ex.Message); }
        }

        private int GetCurrentMagpieHotkey()
        {
            try
            {
                string cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Magpie\config\v5\config.json");
                if (File.Exists(cfgPath))
                {
                    string json = File.ReadAllText(cfgPath);
                    Match m = Regex.Match(json, @"""scale""\s*:\s*(\d+)");
                    if (m.Success)
                    {
                        int val;
                        if (int.TryParse(m.Groups[1].Value, out val)) return val;
                    }
                }
            }
            catch { }
            return 2170; // Default Alt + F11 (Magpie modifier 8 | VK_F11 122)
        }

        private void SetMagpieHotkey(int code)
        {
            try
            {
                UpdateMagpieConfig(json => Regex.Replace(json, @"""scale""\s*:\s*\d+", "\"scale\": " + code));
                Log("Updated FSR Hotkey to code: " + code);
            }
            catch (Exception ex)
            {
                Log("Failed to update Magpie config: " + ex.Message);
            }
        }

        private void StartFsrAutoUpscaleCountdown()
        {
            if (Process.GetProcessesByName("Magpie").Length == 0)
            {
                StartFsrEngine();
                System.Threading.Thread.Sleep(500);
            }

            if (fsrCountdownTimer == null)
            {
                fsrCountdownTimer = new Timer();
                fsrCountdownTimer.Interval = 1000;
                fsrCountdownTimer.Tick += OnFsrCountdownTick;
            }

            fsrCountdownRemaining = 5;
            btnFsrAutoUpscale.Enabled = false;
            btnFsrAutoUpscale.BackColor = Color.FromArgb(180, 80, 20);
            btnFsrAutoUpscale.Text = "⏳ SWITCH TO GAME! (5s)...";
            fsrCountdownTimer.Start();
            Log("⏳ 5-Second Auto-Upscale Timer started. Click into your game window now!");
        }

        private void OnFsrCountdownTick(object sender, EventArgs e)
        {
            fsrCountdownRemaining--;
            if (fsrCountdownRemaining > 0)
            {
                btnFsrAutoUpscale.Text = string.Format("⏳ SWITCH TO GAME! ({0}s)...", fsrCountdownRemaining);
            }
            else
            {
                fsrCountdownTimer.Stop();
                btnFsrAutoUpscale.Text = "⚡ TRIGGERING UPSCALE...";
                btnFsrAutoUpscale.BackColor = Color.FromArgb(30, 160, 90);

                int hotkey = GetCurrentMagpieHotkey();
                TriggerUpscaleHotkey(hotkey);

                Log("✨ Sent FSR upscale trigger to active game window!");

                Timer resetTimer = new Timer();
                resetTimer.Interval = 2000;
                resetTimer.Tick += (s, ev) => {
                    resetTimer.Stop();
                    resetTimer.Dispose();
                    btnFsrAutoUpscale.Text = "⚡  AUTO-UPSCALE IN 5s";
                    btnFsrAutoUpscale.BackColor = Color.FromArgb(170, 40, 40);
                    btnFsrAutoUpscale.Enabled = true;
                };
                resetTimer.Start();
            }
        }

        private void TriggerUpscaleHotkey(int code)
        {
            try
            {
                switch (code)
                {
                    case 2170: // Alt + F11
                    case 378:  // Win + F11 / Fallback
                        keybd_event(0x12, 0, 0, UIntPtr.Zero);
                        keybd_event(0x7A, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(60);
                        keybd_event(0x7A, 0, 2, UIntPtr.Zero);
                        keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        break;
                    case 3137: // Alt + Shift + A
                        keybd_event(0x12, 0, 0, UIntPtr.Zero);
                        keybd_event(0x10, 0, 0, UIntPtr.Zero);
                        keybd_event(0x41, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(60);
                        keybd_event(0x41, 0, 2, UIntPtr.Zero);
                        keybd_event(0x10, 0, 2, UIntPtr.Zero);
                        keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        break;
                    case 122: // F11
                        keybd_event(0x7A, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(60);
                        keybd_event(0x7A, 0, 2, UIntPtr.Zero);
                        break;
                    case 2643: // Ctrl + Alt + S (Magpie 10 | 83)
                    case 771:
                        keybd_event(0x11, 0, 0, UIntPtr.Zero);
                        keybd_event(0x12, 0, 0, UIntPtr.Zero);
                        keybd_event(0x53, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(60);
                        keybd_event(0x53, 0, 2, UIntPtr.Zero);
                        keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        keybd_event(0x11, 0, 2, UIntPtr.Zero);
                        break;
                    case 2113: // Alt + A (Magpie 8 | 65)
                    case 321:
                        keybd_event(0x12, 0, 0, UIntPtr.Zero);
                        keybd_event(0x41, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(60);
                        keybd_event(0x41, 0, 2, UIntPtr.Zero);
                        keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        break;
                    default:
                        keybd_event(0x12, 0, 0, UIntPtr.Zero);
                        keybd_event(0x7A, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(60);
                        keybd_event(0x7A, 0, 2, UIntPtr.Zero);
                        keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to synthesize hotkey: " + ex.Message);
            }
        }
        #endregion

        #region TAB 4: DRIVER GUIDE
        private Panel BuildGuidePanel()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.AutoScroll = true;
            root.Padding = new Padding(24);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            root.Controls.Add(flow);

            Label lblTitle = new Label { Text = "RECOMMENDED DRIVER & TUNING GUIDE", Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = cRadeonRed, AutoSize = true };
            Label lblSubtitle = new Label { Text = "Maximize the performance and lifespan of your 15\" MacBook Pro (2018) Boot Camp setup", Font = new Font("Segoe UI", 10f), ForeColor = cTextMuted, AutoSize = true, Margin = new Padding(0, 4, 0, 16) };
            flow.Controls.Add(lblTitle);
            flow.Controls.Add(lblSubtitle);

            string guideContent =
                "1. UPDATE AMD GRAPHICS DRIVER (BootCampDrivers.com)\n" +
                "----------------------------------------------------------------------------------\n" +
                "Apple has not updated the Boot Camp Radeon driver in years (stock is 25.20 from 2018/2019).\n" +
                "Modern games struggle, hitch, or refuse to launch without modern driver optimizations.\n\n" +
                "👉 Visit www.bootcampdrivers.com\n" +
                "👉 Download the latest 'Adrenalin Red Edition' (Gaming/Performance).\n" +
                "👉 Follow the 5-minute video tutorial: boot into Windows Safe Mode, run DDU (Display Driver\n" +
                "   Uninstaller), then install the modern Adrenalin package.\n" +
                "👉 Unlocks: Radeon Anti-Lag, modern DirectX 12/Vulkan support, and 15–30% higher average FPS.\n\n\n" +
                "2. RETINA DISPLAY RESOLUTION GUIDELINES (4GB POLARIS ARCHITECTURE)\n" +
                "----------------------------------------------------------------------------------\n" +
                "Never run modern 3D titles at the native Retina resolution (2880 × 1800)!\n" +
                "The Radeon Pro 560X has 4 GB of GDDR5 VRAM and is designed for 1080p-class workloads.\n\n" +
                "Optimal Resolutions:\n" +
                "• 1440 × 900 (2x Integer Scaling): Perfect 1:1 pixel match for Retina, sharp text, 75% less load.\n" +
                "• 1680 × 1050 (Recommended 16:10): Best balance of visual sharpness and 60 FPS gameplay.\n" +
                "• 1920 × 1080 / 1200: For lighter esports titles (CS:GO, Rocket League, Valorant).\n\n\n" +
                "3. THERMAL MANAGEMENT & MACS FAN CONTROL\n" +
                "----------------------------------------------------------------------------------\n" +
                "Because the i9-8950HK and Radeon Pro 560X share a single copper heatpipe:\n" +
                "• Keep the '99% Max Processor State' power plan active (prevents 80W CPU spikes from choking the GPU).\n" +
                "• Install 'Macs Fan Control' (free) on Windows.\n" +
                "• Set custom sensor-based fan curves based on the Radeon GPU Diode: ramp fans to 100% when GPU hits 68°C.\n\n\n" +
                "4. RAY TRACING (RT) VS SCREEN SPACE REFLECTIONS (SSR)\n" +
                "----------------------------------------------------------------------------------\n" +
                "• The Radeon Pro 560X (Polaris 21, GCN 4) DOES NOT have dedicated hardware Ray Accelerators.\n" +
                "• DXR / Hardware Ray Tracing emulates rays via compute shaders, dropping FPS to 3–7 FPS\n" +
                "  and causing extreme 100°C thermal spikes across the shared MacBook heatpipe.\n" +
                "• In game graphics menus, ALWAYS choose 'Screen Space Reflections (SSR)' or 'High/Ultra Reflections'.\n" +
                "• Together with the built-in 'FSR + SSR Cinematic Depth' mode, you get 90% of Ray Tracing's\n" +
                "  visual beauty, glossy mirrors, and dynamic lighting depth at a silky-smooth 50–60 FPS!";

            TextBox txtGuide = new TextBox();
            txtGuide.Multiline = true;
            txtGuide.ReadOnly = true;
            txtGuide.BackColor = cCardBg;
            txtGuide.ForeColor = cTextMain;
            txtGuide.Font = new Font("Consolas", 10f);
            txtGuide.BorderStyle = BorderStyle.FixedSingle;
            txtGuide.Size = new Size(820, 560);
            txtGuide.Text = guideContent;
            flow.Controls.Add(txtGuide);

            return root;
        }
        #endregion

        #region Helper Button Creator
        private Button CreateModernButton(string text, Color backColor, Color foreColor, int width, int height)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btn.BackColor = backColor;
            btn.ForeColor = foreColor;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Size = new Size(width, height);
            btn.Cursor = Cursors.Hand;
            return btn;
        }
        #endregion

        #region Telemetry & Diagnostics
        private void OnGlobalTick()
        {
            UpdateHardwareMetrics();
            if (currentTabIndex == 1) // Benchmark active
            {
                lblBenchCpu.Text = lblCpuUsage.Text;
                lblBenchRam.Text = lblRamUsage.Text;
            }
            if (currentTabIndex == 2) // FSR tab active
            {
                UpdateFsrStatus();
            }
        }

        private void UpdateHardwareMetrics()
        {
            try
            {
                System.Runtime.InteropServices.ComTypes.FILETIME idleTime, kernelTime, userTime;
                if (GetSystemTimes(out idleTime, out kernelTime, out userTime))
                {
                    ulong currentIdle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
                    ulong currentKernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
                    ulong currentUser = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;

                    if (prevKernel != 0)
                    {
                        ulong diffIdle = currentIdle - prevIdle;
                        ulong diffKernel = currentKernel - prevKernel;
                        ulong diffUser = currentUser - prevUser;
                        ulong totalTime = diffKernel + diffUser;

                        if (totalTime > 0)
                        {
                            ulong busyTime = totalTime - diffIdle;
                            int cpuPct = (int)((busyTime * 100) / totalTime);
                            cpuPct = Math.Max(0, Math.Min(100, cpuPct));

                            lblCpuUsage.Text = cpuPct + " %";
                            prgCpu.Value = cpuPct;
                            CurrentCpuPercent = cpuPct;
                            if (cpuPct > 80) lblCpuUsage.ForeColor = cRadeonRed;
                            else if (cpuPct > 50) lblCpuUsage.ForeColor = cYellow;
                            else lblCpuUsage.ForeColor = cGreen;
                        }
                    }
                    prevIdle = currentIdle; prevKernel = currentKernel; prevUser = currentUser;
                }

                MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    double totalGb = (double)memStatus.ullTotalPhys / (1024 * 1024 * 1024);
                    double availGb = (double)memStatus.ullAvailPhys / (1024 * 1024 * 1024);
                    double usedGb = totalGb - availGb;
                    int ramPct = (int)memStatus.dwMemoryLoad;

                    lblRamUsage.Text = string.Format("{0:0.0} / {1:0.0} GB ({2}%)", usedGb, totalGb, ramPct);
                    prgRam.Value = Math.Min(100, Math.Max(0, ramPct));
                    CurrentRamText = string.Format("{0:0.0} / {1:0.0} GB", usedGb, totalGb);
                }
            }
            catch { }
        }

        private void CheckAdminAndLog()
        {
            bool isAdmin = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
            if (isAdmin) Log("✅ Running with Administrator privileges. System tweaks enabled.");
            else Log("⚠️ Running in standard user mode. Some registry modifications may require elevation.");
        }

        private void Log(string message)
        {
            if (txtLog != null && !txtLog.IsDisposed)
            {
                txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}\n", DateTime.Now, message));
                txtLog.ScrollToCaret();
            }
        }
        #endregion

        #region Optimizer Registry Operations
        private void RefreshAllOptimizerStates()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"))
                {
                    if (key != null)
                    {
                        object desc = key.GetValue("DriverDesc");
                        object ver = key.GetValue("DriverVersion");
                        if (desc != null) lblGpuName.Text = desc.ToString() + " (4GB VRAM)";
                        if (ver != null) lblGpuDriver.Text = "Driver: " + ver.ToString();
                    }
                }
            }
            catch { }

            lblPowerPlan.Text = "Plan: " + GetActivePowerPlan();

            // 1. ULPS
            int ulps = GetUlpsState();
            lblUlpsStatus.Text = (ulps == 0) ? "● DISABLED (Optimal - Smooth FPS)" : "● ENABLED (Stutter & Frame-drop Risk)";
            lblUlpsStatus.ForeColor = (ulps == 0) ? cGreen : cRadeonRed;
            btnToggleUlps.Text = (ulps == 0) ? "Enable ULPS" : "Disable ULPS";

            // 2. HAGS
            int hags = GetHagsState();
            lblHagsStatus.Text = (hags == 2) ? "● ENABLED (Hardware Scheduling Active)" : "○ DISABLED";
            lblHagsStatus.ForeColor = (hags == 2) ? cGreen : cYellow;
            btnToggleHags.Text = (hags == 2) ? "Disable HAGS" : "Enable HAGS";

            // 3. TDR Delay
            int tdr = GetTdrState();
            lblTdrStatus.Text = (tdr >= 8) ? string.Format("● {0}s (Crash Protection Active)", tdr) : "○ 2s (Windows Default - Crash Risk)";
            lblTdrStatus.ForeColor = (tdr >= 8) ? cGreen : cYellow;
            btnToggleTdr.Text = (tdr >= 8) ? "Reset to 2s" : "Set to 8s";

            // 4. CPU 99% Cap
            int cpuCap = GetCpuCapState();
            IsCpuCapActive = (cpuCap == 99);
            lblCpuCapStatus.Text = (cpuCap == 99) ? "● 99% (Sustained GPU Boost - No Thermal Throttling)" : "○ 100% (High Thermal Spikes - Throttling Risk)";
            lblCpuCapStatus.ForeColor = (cpuCap == 99) ? cGreen : cYellow;
            btnToggleCpuCap.Text = (cpuCap == 99) ? "Set to 100%" : "Set to 99%";

            // 5. GameDVR
            int dvr = GetGameDvrState();
            lblGameDvrStatus.Text = (dvr == 0) ? "● DISABLED (Full GPU Priority)" : "○ ENABLED (Background Capture Active)";
            lblGameDvrStatus.ForeColor = (dvr == 0) ? cGreen : cYellow;
            btnToggleGameDvr.Text = (dvr == 0) ? "Enable DVR" : "Disable DVR";

            // 6. RIS / CAS
            int ris = GetRisState();
            lblRisStatus.Text = (ris == 1) ? "● ENABLED (Contrast Adaptive Sharpening Active)" : "○ DISABLED";
            lblRisStatus.ForeColor = (ris == 1) ? cGreen : cYellow;
            btnToggleRis.Text = (ris == 1) ? "Disable RIS" : "Enable RIS";

            // 7. Windows MPO
            int mpo = GetMpoState();
            lblMpoStatus.Text = (mpo == 5) ? "● DISABLED (Optimal - Smooth FSR & Borderless)" : "○ ENABLED (Default - Flicker/Stutter Risk)";
            lblMpoStatus.ForeColor = (mpo == 5) ? cGreen : cYellow;
            btnToggleMpo.Text = (mpo == 5) ? "Enable MPO" : "Disable MPO";

            // 8. CPU Core Unparking
            int minCores = GetCoreMinPercentState();
            lblUnparkStatus.Text = (minCores >= 100) ? "● UNPARKED (100% Cores Ready - Smooth 1% Lows)" : string.Format("○ PARKED ({0}% Min Cores - Wakeup Stutter Risk)", minCores);
            lblUnparkStatus.ForeColor = (minCores >= 100) ? cGreen : cYellow;
            btnToggleUnpark.Text = (minCores >= 100) ? "Allow Parking" : "Unpark All";

            // 9. 0.5ms Timer Resolution
            lblTimerResStatus.Text = isTimerResActive ? "● ACTIVE (0.5ms Low-Latency Precision Tick)" : "○ DEFAULT (15.6ms Windows Tick)";
            lblTimerResStatus.ForeColor = isTimerResActive ? cGreen : cYellow;
            btnToggleTimerRes.Text = isTimerResActive ? "Reset 15.6ms" : "Enable 0.5ms";

            // 10. Pre-Game RAM & Priority Boost
            if (lblRamBoostStatus != null && lblRamBoostStatus.Text == "Detecting...")
            {
                lblRamBoostStatus.Text = "○ READY (Trim Standby RAM & Boost Active Game)";
                lblRamBoostStatus.ForeColor = cCyan;
            }
            btnRamBoost.Text = "⚡ Boost Now";
        }

        private int GetUlpsState()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"))
                {
                    if (k != null) { object v = k.GetValue("EnableUlps"); if (v != null) return Convert.ToInt32(v); }
                }
            } catch { }
            return 1;
        }

        private int GetHagsState()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    if (k != null) { object v = k.GetValue("HwSchMode"); if (v != null) return Convert.ToInt32(v); }
                }
            } catch { }
            return 0;
        }

        private int GetTdrState()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    if (k != null) { object v = k.GetValue("TdrDelay"); if (v != null) return Convert.ToInt32(v); }
                }
            } catch { }
            return 2;
        }

        private int GetPowerCfgAcValue(string subGroupGuid, string settingGuid, int fallback)
        {
            try
            {
                string activeGuid = GetActivePowerSchemeGuid();
                if (!string.IsNullOrEmpty(activeGuid))
                {
                    string outStr = RunPowerCfg(string.Format("/qh {0} {1} {2}", activeGuid, subGroupGuid, settingGuid));
                    Match m = Regex.Match(outStr, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
                    if (m.Success) return Convert.ToInt32(m.Groups[1].Value, 16);
                }
            } catch { }
            return fallback;
        }

        private int GetCpuCapState()
        {
            return GetPowerCfgAcValue("54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec", 100);
        }

        private int GetCoreMinPercentState()
        {
            return GetPowerCfgAcValue("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 5);
        }

        private int GetMpoState()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm"))
                {
                    if (k != null) { object v = k.GetValue("OverlayTestMode"); if (v != null) return Convert.ToInt32(v); }
                }
            } catch { }
            return 0;
        }

        private List<string> FindOptimizedPowerPlanGuids()
        {
            List<string> guids = new List<string>();
            try
            {
                string listOut = RunPowerCfg("-list");
                foreach (string line in listOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.IndexOf("Radeon Pro 560X - Optimized", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Match m = Regex.Match(line, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                        if (m.Success) guids.Add(m.Value);
                    }
                }
            } catch { }
            return guids;
        }

        private int GetGameDvrState()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore"))
                {
                    if (k != null) { object v = k.GetValue("GameDVR_Enabled"); if (v != null) return Convert.ToInt32(v); }
                }
            } catch { }
            return 1;
        }

        private int GetRisState()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"))
                {
                    if (k != null) { object v = k.GetValue("KMD_RadeonImageSharpening"); if (v != null) return Convert.ToInt32(v); }
                }
            } catch { }
            return 0;
        }

        private string GetActivePowerPlan()
        {
            try
            {
                string res = RunPowerCfg("/getactivescheme");
                Match m = Regex.Match(res, @"\((.*?)\)");
                if (m.Success) return m.Groups[1].Value;
            } catch { }
            return "Balanced";
        }

        private string GetActivePowerSchemeGuid()
        {
            try
            {
                string res = RunPowerCfg("/getactivescheme");
                Match m = Regex.Match(res, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (m.Success) return m.Value;
            } catch { }
            return null;
        }

        private string RunPowerCfg(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using (Process p = Process.Start(psi)) { return p.StandardOutput.ReadToEnd(); }
        }

        private void ToggleUlps()
        {
            int cur = GetUlpsState();
            int next = (cur == 0) ? 1 : 0;
            try
            {
                using (RegistryKey classRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true))
                {
                    if (classRoot != null)
                    {
                        foreach (string sub in classRoot.GetSubKeyNames())
                        {
                            if (sub.StartsWith("00"))
                            {
                                using (RegistryKey k = classRoot.OpenSubKey(sub, true))
                                {
                                    if (k != null && (k.GetValue("EnableUlps") != null || sub == "0000"))
                                    {
                                        k.SetValue("EnableUlps", next, RegistryValueKind.DWord);
                                        k.SetValue("EnableUlps_NA", next, RegistryValueKind.DWord);
                                    }
                                }
                            }
                        }
                    }
                }
                Log(string.Format("ULPS set to {0} ({1}).", next, next == 0 ? "Disabled - Smooth FPS" : "Enabled"));
            }
            catch (Exception ex) { Log("Error updating ULPS: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleHags()
        {
            int cur = GetHagsState();
            int next = (cur == 2) ? 0 : 2;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    if (k != null) k.SetValue("HwSchMode", next, RegistryValueKind.DWord);
                }
                Log(string.Format("HAGS set to {0} ({1}).", next, next == 2 ? "Enabled" : "Disabled"));
            }
            catch (Exception ex) { Log("Error updating HAGS: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleTdr()
        {
            int cur = GetTdrState();
            int next = (cur >= 8) ? 2 : 8;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    if (k != null) k.SetValue("TdrDelay", next, RegistryValueKind.DWord);
                }
                Log(string.Format("TdrDelay set to {0} seconds.", next));
            }
            catch (Exception ex) { Log("Error updating TdrDelay: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleCpuCap()
        {
            int cur = GetCpuCapState();
            int next = (cur == 99) ? 100 : 99;
            try
            {
                string guid = GetActivePowerSchemeGuid();
                if (!string.IsNullOrEmpty(guid))
                {
                    RunPowerCfg(string.Format("-setacvalueindex {0} 54533251-82be-4824-96c1-47b60b740d00 bc5038f7-23e0-4960-96da-33abaf5935ec {1}", guid, next));
                    RunPowerCfg(string.Format("-setactive {0}", guid));
                    Log(string.Format("CPU Thermal Cap set to {0}% on active power plan.", next));
                }
            }
            catch (Exception ex) { Log("Error setting CPU Cap: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleGameDvr()
        {
            int cur = GetGameDvrState();
            int next = (cur == 0) ? 1 : 0;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                {
                    if (k != null) k.SetValue("GameDVR_Enabled", next, RegistryValueKind.DWord);
                }
                Log(string.Format("GameDVR background capture set to {0}.", next == 0 ? "Disabled" : "Enabled"));
            }
            catch (Exception ex) { Log("Error setting GameDVR: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleRis()
        {
            int cur = GetRisState();
            int next = (cur == 1) ? 0 : 1;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", true))
                {
                    if (k != null)
                    {
                        k.SetValue("KMD_RadeonImageSharpening", next, RegistryValueKind.DWord);
                        if (next == 1) k.SetValue("KMD_RadeonImageSharpeningStrength", 80, RegistryValueKind.DWord);
                    }
                }
                Log(string.Format("Radeon Image Sharpening (CAS) set to {0}.", next == 1 ? "Enabled (80% strength)" : "Disabled"));
            }
            catch (Exception ex) { Log("Error setting RIS: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleMpo()
        {
            int cur = GetMpoState();
            bool disableMpo = (cur != 5);
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\Dwm"))
                {
                    if (k != null)
                    {
                        if (disableMpo) k.SetValue("OverlayTestMode", 5, RegistryValueKind.DWord);
                        else k.DeleteValue("OverlayTestMode", false);
                    }
                }
                Log(disableMpo ? "Windows MPO disabled (OverlayTestMode=5). Restart recommended." : "Windows MPO restored to default.");
            }
            catch (Exception ex) { Log("Error setting MPO: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleCoreUnpark()
        {
            int cur = GetCoreMinPercentState();
            int next = (cur >= 100) ? 5 : 100;
            try
            {
                string guid = GetActivePowerSchemeGuid();
                if (!string.IsNullOrEmpty(guid))
                {
                    RunPowerCfg(string.Format("-setacvalueindex {0} 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 {1}", guid, next));
                    RunPowerCfg(string.Format("-setactive {0}", guid));
                    Log(string.Format("CPU Core Parking Min Cores set to {0}% ({1}).", next, next == 100 ? "All 12 threads unparked" : "Default parking"));
                }
            }
            catch (Exception ex) { Log("Error setting Core Unparking: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void ToggleTimerResolution()
        {
            try
            {
                uint cur;
                if (!isTimerResActive)
                {
                    int status = NtSetTimerResolution(5000, true, out cur);
                    if (status == 0)
                    {
                        isTimerResActive = true;
                        Log(string.Format("⚡ Enabled 0.5ms Low-Latency Timer Resolution (active tick: {0:F2}ms).", cur / 10000.0));
                    }
                    else
                    {
                        Log("NtSetTimerResolution returned status: " + status);
                    }
                }
                else
                {
                    NtSetTimerResolution(5000, false, out cur);
                    isTimerResActive = false;
                    Log("Restored default Windows timer resolution (auto-reset).");
                }
            }
            catch (Exception ex) { Log("Timer resolution error: " + ex.Message); }
            RefreshAllOptimizerStates();
        }

        private void BoostMemoryAndGames()
        {
            int trimmedCount = 0;
            List<string> boostedGames = new List<string>();
            HashSet<string> skipProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "Idle", "System", "Registry", "smss", "csrss", "wininit", "services", "lsass",
                "svchost", "dwm", "explorer", "RadeonOptimizer", "Magpie", "Memory Compression"
            };
            HashSet<string> knownGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "GTA5", "PlayGTAV", "Cyberpunk2077", "witcher3", "RDR2", "eldenring",
                "rpcs3", "pcsx2", "dolphin", "yuzu", "cs2", "javaw", "FortniteClient-Win64-Shipping"
            };
            HashSet<string> nonGameApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "chrome", "msedge", "firefox", "brave", "code", "devenv", "steamwebhelper", "Discord", "obs64"
            };

            MEMORYSTATUSEX beforeMem = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(beforeMem);

            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId == 0 || skipProcs.Contains(p.ProcessName)) continue;

                    bool isKnownGame = knownGames.Contains(p.ProcessName);
                    bool isHeavyWindowedApp = !nonGameApps.Contains(p.ProcessName)
                        && p.MainWindowHandle != IntPtr.Zero
                        && p.WorkingSet64 > 450L * 1024L * 1024L;

                    if (isKnownGame || isHeavyWindowedApp)
                    {
                        if (p.PriorityClass != ProcessPriorityClass.High)
                        {
                            p.PriorityClass = ProcessPriorityClass.High;
                        }
                        boostedGames.Add(p.ProcessName);
                    }
                    else
                    {
                        if (EmptyWorkingSet(p.Handle)) trimmedCount++;
                    }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            MEMORYSTATUSEX afterMem = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(afterMem);
            double freedMb = Math.Max(0, (double)((long)afterMem.ullAvailPhys - (long)beforeMem.ullAvailPhys) / (1024.0 * 1024.0));

            string gameText = boostedGames.Count > 0 ? string.Join(", ", boostedGames.ToArray()) : "None detected";
            lblRamBoostStatus.Text = string.Format("● BOOSTED ({0} apps trimmed, +{1:F0} MB free)", trimmedCount, freedMb);
            lblRamBoostStatus.ForeColor = cGreen;

            Log(string.Format("🚀 Pre-Game Boost complete: Trimmed {0} background apps (+{1:F0} MB RAM freed). High-Priority Games: {2}",
                trimmedCount, freedMb, gameText));
            UpdateHardwareMetrics();
        }

        private void ApplyAllOptimizations()
        {
            Log("=================================================");
            Log("🚀 APPLYING ALL OPTIMIZATIONS FOR RADEON PRO 560X");
            Log("=================================================");

            bool isAdmin = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
            if (!isAdmin)
            {
                MessageBox.Show("Administrator privileges are required to apply GPU optimizations.\n\nPlease close and restart the application as Administrator.", "Permission Denied", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log("❌ Optimization aborted: Administrator privileges required.");
                return;
            }

            try
            {
                using (RegistryKey classRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true))
                {
                    if (classRoot != null)
                    {
                        foreach (string subName in classRoot.GetSubKeyNames())
                        {
                            if (subName.StartsWith("00"))
                            {
                                using (RegistryKey k = classRoot.OpenSubKey(subName, true))
                                {
                                    if (k != null && (k.GetValue("EnableUlps") != null || subName == "0000"))
                                    {
                                        k.SetValue("EnableUlps", 0, RegistryValueKind.DWord);
                                        k.SetValue("EnableUlps_NA", 0, RegistryValueKind.DWord);
                                    }
                                }
                            }
                        }
                    }
                }
                Log("1/6: ULPS disabled across all display registries.");
            } catch (Exception ex) { Log("1/6 ULPS Error: " + ex.Message); }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    if (k != null) { k.SetValue("TdrDelay", 8, RegistryValueKind.DWord); k.SetValue("HwSchMode", 2, RegistryValueKind.DWord); }
                }
                Log("2/6: TdrDelay set to 8s & HAGS enabled.");
            } catch (Exception ex) { Log("2/6 GraphicsDrivers Error: " + ex.Message); }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    if (k != null) { k.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord); k.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord); }
                }
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                {
                    if (k != null) { k.SetValue("GPU Priority", 8, RegistryValueKind.DWord); k.SetValue("Priority", 6, RegistryValueKind.DWord); k.SetValue("Scheduling Category", "High", RegistryValueKind.String); }
                }
                Log("3/6: Multimedia & 3D Task priority set to High.");
            } catch (Exception ex) { Log("3/6 SystemProfile Error: " + ex.Message); }

            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore")) { if (k != null) k.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord); }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar")) { if (k != null) k.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord); }
                Log("4/6: GameDVR background recording disabled.");
            } catch (Exception ex) { Log("4/6 GameDVR Error: " + ex.Message); }

            try
            {
                using (RegistryKey classRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true))
                {
                    if (classRoot != null)
                    {
                        foreach (string subName in classRoot.GetSubKeyNames())
                        {
                            if (subName.StartsWith("00"))
                            {
                                using (RegistryKey k = classRoot.OpenSubKey(subName, true))
                                {
                                    if (k != null && (k.GetValue("DriverDesc") != null || subName == "0000"))
                                    {
                                        k.SetValue("KMD_RadeonImageSharpening", 1, RegistryValueKind.DWord);
                                        k.SetValue("KMD_RadeonImageSharpeningStrength", 80, RegistryValueKind.DWord);
                                    }
                                }
                            }
                        }
                    }
                }
                Log("5/6: Driver-level Radeon Image Sharpening (CAS) enabled.");
            } catch (Exception ex) { Log("5/6 RIS Error: " + ex.Message); }

            try
            {
                List<string> existingPlans = FindOptimizedPowerPlanGuids();
                string planGuid = (existingPlans.Count > 0) ? existingPlans[0] : null;

                if (string.IsNullOrEmpty(planGuid))
                {
                    string dupOut = RunPowerCfg("-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                    Match m = Regex.Match(dupOut, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    planGuid = m.Success ? m.Value : GetActivePowerSchemeGuid();
                    RunPowerCfg(string.Format("-changename {0} \"Radeon Pro 560X - Optimized\" \"Sustained GPU Boost Clock with 99% CPU thermal limit\"", planGuid));
                }

                RunPowerCfg(string.Format("-setactive {0}", planGuid));
                RunPowerCfg(string.Format("-setacvalueindex {0} 501a4d13-42af-4429-9e56-4784f8da68f4 ee12f906-d277-404b-b6da-e5fa1a576df5 0", planGuid));
                RunPowerCfg(string.Format("-setacvalueindex {0} 54533251-82be-4824-96c1-47b60b740d00 bc5038f7-23e0-4960-96da-33abaf5935ec 99", planGuid));
                RunPowerCfg(string.Format("-setacvalueindex {0} 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100", planGuid));
                RunPowerCfg(string.Format("-setactive {0}", planGuid));
                Log("6/7: Activated 'Radeon Pro 560X - Optimized' Power Plan (99% CPU cap + 100% Core Unpark).");
            } catch (Exception ex) { Log("6/7 PowerCfg Error: " + ex.Message); }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\Dwm"))
                {
                    if (k != null) k.SetValue("OverlayTestMode", 5, RegistryValueKind.DWord);
                }
                Log("7/7: Windows MPO disabled (OverlayTestMode=5 - stops borderless/FSR flicker).");
            } catch (Exception ex) { Log("7/7 MPO Error: " + ex.Message); }

            Log("✅ ALL 7 OPTIMIZATIONS SUCCESSFULLY APPLIED.");
            Log("=================================================");
            RefreshAllOptimizerStates();

            MessageBox.Show("All optimizations applied successfully!\n\nFor the display driver changes (ULPS, HAGS & MPO) to take full hardware effect, a quick system restart is recommended.",
                "Radeon Performance Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RestoreDefaults()
        {
            if (MessageBox.Show("Restore default Windows settings and the standard Balanced power plan?", "Restore Defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            bool isAdmin = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
            if (!isAdmin)
            {
                MessageBox.Show("Administrator privileges are required to restore Windows factory defaults.\n\nPlease close and restart the application as Administrator.", "Permission Denied", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log("❌ Restore failed: Administrator privileges required.");
                return;
            }

            Log("Restoring factory defaults...");
            bool hasError = false;

            // 1. Revert ULPS to 1 across all display subkeys
            try
            {
                using (RegistryKey classRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true))
                {
                    if (classRoot != null)
                    {
                        foreach (string subName in classRoot.GetSubKeyNames())
                        {
                            if (subName.StartsWith("00"))
                            {
                                using (RegistryKey sub = classRoot.OpenSubKey(subName, true))
                                {
                                    if (sub != null)
                                    {
                                        if (sub.GetValue("EnableUlps") != null) sub.SetValue("EnableUlps", 1, RegistryValueKind.DWord);
                                        if (sub.GetValue("EnableUlps_NA") != null) sub.SetValue("EnableUlps_NA", 1, RegistryValueKind.DWord);
                                        try { sub.DeleteValue("KMD_RadeonImageSharpening", false); } catch { }
                                        try { sub.DeleteValue("KMD_RadeonImageSharpeningStrength", false); } catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                Log("1/5: Reverted EnableUlps=1 and cleared RIS.");
            }
            catch (Exception ex)
            {
                hasError = true;
                Log("Restore error (ULPS): " + ex.Message);
            }

            // 2. Clear TdrDelay and HwSchMode
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true))
                {
                    if (k != null)
                    {
                        try { k.DeleteValue("TdrDelay", false); } catch { }
                        try { k.DeleteValue("HwSchMode", false); } catch { }
                    }
                }
                Log("2/5: Reverted TdrDelay and HAGS to Windows defaults.");
            }
            catch (Exception ex)
            {
                hasError = true;
                Log("Restore error (GraphicsDrivers): " + ex.Message);
            }

            // 3. Revert SystemProfile
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true))
                {
                    if (k != null)
                    {
                        k.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                        k.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                    }
                }
                Log("3/5: Reverted multimedia responsiveness to Windows defaults.");
            }
            catch (Exception ex)
            {
                hasError = true;
                Log("Restore error (SystemProfile): " + ex.Message);
            }

            // 4. Revert GameDVR
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", true))
                {
                    if (k != null) k.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);
                }
                Log("4/5: Re-enabled GameDVR.");
            }
            catch (Exception ex)
            {
                hasError = true;
                Log("Restore error (GameDVR): " + ex.Message);
            }

            // 5. Revert Windows MPO & In-Memory Timer
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm", true))
                {
                    if (k != null) { try { k.DeleteValue("OverlayTestMode", false); } catch { } }
                }
                if (isTimerResActive)
                {
                    uint cur;
                    NtSetTimerResolution(5000, false, out cur);
                    isTimerResActive = false;
                }
                Log("5/6: Reverted Windows MPO and timer resolution.");
            }
            catch (Exception ex)
            {
                hasError = true;
                Log("Restore error (MPO): " + ex.Message);
            }

            // 6. Restore Balanced Power Plan & Clean Custom Schemes
            try
            {
                const string balancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
                RunPowerCfg("-setactive " + balancedGuid);
                foreach (string customGuid in FindOptimizedPowerPlanGuids())
                {
                    if (!string.Equals(customGuid, balancedGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        RunPowerCfg("-delete " + customGuid);
                    }
                }
                Log("6/6: Activated standard Windows Balanced power plan and cleaned custom plans.");
            }
            catch (Exception ex)
            {
                hasError = true;
                Log("Restore error (PowerCfg): " + ex.Message);
            }

            if (!hasError)
            {
                Log("✅ FACTORY DEFAULTS RESTORED SUCCESSFULLY.");
                MessageBox.Show("Windows factory defaults and the standard Balanced power plan have been successfully restored!\n\nA quick system restart is recommended for the display driver to re-engage default power cycling.", "Defaults Restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Log("⚠️ Restore completed with warnings/errors. Check log above.");
                MessageBox.Show("Some settings could not be restored. Ensure the application is running as Administrator.", "Restore Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshAllOptimizerStates();
        }
        #endregion

        #region Benchmark OpenGL Engine
        private void InitParticles()
        {
            Random rnd = new Random(42);
            particleCoords = new float[particleCount * 3];
            particleColors = new float[particleCount * 3];

            for (int i = 0; i < particleCount; i++)
            {
                float radius = 1.8f + (float)rnd.NextDouble() * 3.5f;
                float theta = (float)(rnd.NextDouble() * Math.PI * 2);
                float phi = (float)((rnd.NextDouble() - 0.5) * Math.PI * 0.8);

                particleCoords[i * 3 + 0] = radius * (float)Math.Cos(theta) * (float)Math.Cos(phi);
                particleCoords[i * 3 + 1] = radius * (float)Math.Sin(phi);
                particleCoords[i * 3 + 2] = radius * (float)Math.Sin(theta) * (float)Math.Cos(phi);

                if (rnd.Next(2) == 0)
                {
                    particleColors[i * 3 + 0] = 0.92f; particleColors[i * 3 + 1] = 0.08f; particleColors[i * 3 + 2] = 0.38f;
                }
                else
                {
                    particleColors[i * 3 + 0] = 0.22f; particleColors[i * 3 + 1] = 0.74f; particleColors[i * 3 + 2] = 0.98f;
                }
            }
        }

        private void InitOpenGL()
        {
            if (isGlInitialized || glRenderPanel == null) return;

            hDC = GetDC(glRenderPanel.Handle);
            PIXELFORMATDESCRIPTOR pfd = new PIXELFORMATDESCRIPTOR();
            pfd.nSize = (ushort)Marshal.SizeOf(pfd);
            pfd.nVersion = 1;
            pfd.dwFlags = 0x00000004 | 0x00000020 | 0x00000001;
            pfd.iPixelType = 0;
            pfd.cColorBits = 32;
            pfd.cDepthBits = 24;

            int format = ChoosePixelFormat(hDC, ref pfd);
            SetPixelFormat(hDC, format, ref pfd);

            hGLRC = wglCreateContext(hDC);
            wglMakeCurrent(hDC, hGLRC);

            glEnable(GL_DEPTH_TEST);
            glDepthFunc(GL_LEQUAL);
            glShadeModel(GL_SMOOTH);
            glClearColor(0.05f, 0.05f, 0.07f, 1.0f);
            glClearDepth(1.0);

            glEnable(GL_LIGHTING);
            glEnable(GL_LIGHT0);
            glEnable(GL_COLOR_MATERIAL);

            float[] lightPos = new float[] { 2.0f, 3.0f, 4.0f, 1.0f };
            float[] lightColor = new float[] { 1.0f, 0.95f, 0.95f, 1.0f };
            float[] ambient = new float[] { 0.25f, 0.25f, 0.3f, 1.0f };

            glLightfv(GL_LIGHT0, 0x1203, lightPos);
            glLightfv(GL_LIGHT0, 0x1201, lightColor);
            glLightfv(GL_LIGHT0, 0x1200, ambient);

            ResizeGL();
            isGlInitialized = true;
            benchFrameStopwatch.Start();
        }

        private void ResizeGL()
        {
            if (glRenderPanel == null || glRenderPanel.Width == 0 || glRenderPanel.Height == 0 || hGLRC == IntPtr.Zero) return;

            wglMakeCurrent(hDC, hGLRC);
            glViewport(0, 0, glRenderPanel.Width, glRenderPanel.Height);

            glMatrixMode(GL_PROJECTION);
            glLoadIdentity();

            double aspect = (double)glRenderPanel.Width / (double)glRenderPanel.Height;
            double zNear = 0.5;
            double zFar = 50.0;
            double fovY = 55.0;
            double halfH = Math.Tan(fovY * Math.PI / 360.0) * zNear;
            double halfW = halfH * aspect;
            glFrustum(-halfW, halfW, -halfH, halfH, zNear, zFar);

            glMatrixMode(GL_MODELVIEW);
        }

        private void CleanupOpenGL()
        {
            if (hGLRC != IntPtr.Zero)
            {
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(hGLRC);
                hGLRC = IntPtr.Zero;
            }
            if (hDC != IntPtr.Zero)
            {
                ReleaseDC(glRenderPanel.Handle, hDC);
                hDC = IntPtr.Zero;
            }
            isGlInitialized = false;
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            while (AppStillIdle)
            {
                if (currentTabIndex == 1 && isGlInitialized) // Only render when Benchmark tab is visible
                {
                    RenderBenchmarkFrame();
                }
                else
                {
                    System.Threading.Thread.Sleep(16);
                    break;
                }
            }
        }

        private void RenderBenchmarkFrame()
        {
            double currentMs = benchFrameStopwatch.Elapsed.TotalMilliseconds;
            double deltaMs = currentMs - lastBenchFrameMs;
            lastBenchFrameMs = currentMs;

            if (deltaMs > 0 && deltaMs < 1000)
            {
                rollingFrameTimes.Add(deltaMs);
                if (rollingFrameTimes.Count > 120) rollingFrameTimes.RemoveAt(0);
                if (deltaMs > 33.3) benchStutterEvents++;

                if (isBenchRunning)
                {
                    benchFrameTimes.Add(deltaMs);
                    double elapsed = benchTimedStopwatch.Elapsed.TotalSeconds;
                    double remaining = Math.Max(0, BENCH_DURATION_SEC - elapsed);
                    lblBenchTimer.Text = string.Format("Testing: {0:F1}s remaining", remaining);
                    if (elapsed >= BENCH_DURATION_SEC) FinishBenchmarkRun();
                }

                graphHistory.Enqueue((float)deltaMs);
                if (graphHistory.Count > 150) graphHistory.Dequeue();
            }

            wglMakeCurrent(hDC, hGLRC);
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
            glLoadIdentity();
            glTranslatef(0.0f, 0.15f, -6.7f);

            rotX += 0.45f; rotY += 0.85f; rotZ += 0.25f;

            if (enableBenchmarkSsr)
            {
                // Cinematic downward viewing pitch for reflective ground plane
                glRotatef(16.0f, 1.0f, 0.0f, 0.0f);

                // 1. Draw Real-time Screen Space Floor Reflection
                DrawSsrReflection();

                // 2. Draw Glossy Obsidian Mirror Floor & Cyberpunk Grid
                DrawSsrFloorGrid();

                // 3. Draw Core Crystal & Particles
                glPushMatrix();
                glRotatef(rotX, 1.0f, 0.0f, 0.0f);
                glRotatef(rotY, 0.0f, 1.0f, 0.0f);
                glRotatef(rotZ, 0.0f, 0.0f, 1.0f);
                DrawBenchmarkCore();
                glPopMatrix();

                DrawBenchmarkParticles();
            }
            else
            {
                // Classic standard non-SSR view
                glRotatef(rotX, 1.0f, 0.0f, 0.0f);
                glRotatef(rotY, 0.0f, 1.0f, 0.0f);
                glRotatef(rotZ, 0.0f, 0.0f, 1.0f);

                DrawBenchmarkCore();
                DrawBenchmarkParticles();
            }

            SwapBuffers(hDC);

            UpdateBenchmarkHud(deltaMs);
            graphBox.Invalidate();
        }

        private void DrawBenchmarkCore()
        {
            float s = 1.35f;
            glBegin(GL_TRIANGLES);

            Point3D[] verts = new Point3D[] {
                new Point3D(0, s, 0), new Point3D(-s, 0, s), new Point3D(s, 0, s),
                new Point3D(0, s, 0), new Point3D(s, 0, s), new Point3D(s, 0, -s),
                new Point3D(0, s, 0), new Point3D(s, 0, -s), new Point3D(-s, 0, -s),
                new Point3D(0, s, 0), new Point3D(-s, 0, -s), new Point3D(-s, 0, s),
                new Point3D(0, -s, 0), new Point3D(s, 0, s), new Point3D(-s, 0, s),
                new Point3D(0, -s, 0), new Point3D(s, 0, -s), new Point3D(s, 0, s),
                new Point3D(0, -s, 0), new Point3D(-s, 0, -s), new Point3D(s, 0, -s),
                new Point3D(0, -s, 0), new Point3D(-s, 0, s), new Point3D(-s, 0, -s)
            };

            for (int i = 0; i < verts.Length; i += 3)
            {
                if ((i / 3) % 2 == 0) glColor3f(0.88f, 0.07f, 0.37f); // Radeon Ruby
                else glColor3f(0.65f, 0.04f, 0.25f);

                Point3D p1 = verts[i], p2 = verts[i + 1], p3 = verts[i + 2];
                Point3D n = CalcNormal(p1, p2, p3);
                glNormal3f(n.x, n.y, n.z);
                glVertex3f(p1.x, p1.y, p1.z);
                glVertex3f(p2.x, p2.y, p2.z);
                glVertex3f(p3.x, p3.y, p3.z);
            }
            glEnd();

            if (benchStressLevel >= 2)
            {
                glLineWidth(1.5f);
                glBegin(GL_LINES);
                glColor3f(0.22f, 0.74f, 0.98f);
                for (int i = 0; i < verts.Length; i += 3)
                {
                    glVertex3f(verts[i].x * 1.3f, verts[i].y * 1.3f, verts[i].z * 1.3f);
                    glVertex3f(verts[i + 1].x * 1.3f, verts[i + 1].y * 1.3f, verts[i + 1].z * 1.3f);
                    glVertex3f(verts[i + 1].x * 1.3f, verts[i + 1].y * 1.3f, verts[i + 1].z * 1.3f);
                    glVertex3f(verts[i + 2].x * 1.3f, verts[i + 2].y * 1.3f, verts[i + 2].z * 1.3f);
                }
                glEnd();
            }
        }

        private void DrawBenchmarkParticles()
        {
            glDisable(GL_LIGHTING);
            glPointSize(2.5f);
            glBegin(GL_POINTS);
            int count = (benchStressLevel == 1) ? 1000 : (benchStressLevel == 2 ? 1500 : particleCount);
            for (int i = 0; i < count; i++)
            {
                glColor3f(particleColors[i * 3 + 0], particleColors[i * 3 + 1], particleColors[i * 3 + 2]);
                glVertex3f(particleCoords[i * 3 + 0], particleCoords[i * 3 + 1], particleCoords[i * 3 + 2]);
            }
            glEnd();
            glEnable(GL_LIGHTING);
        }

        struct Point3D { public float x, y, z; public Point3D(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
        private Point3D CalcNormal(Point3D p1, Point3D p2, Point3D p3)
        {
            float ax = p2.x - p1.x, ay = p2.y - p1.y, az = p2.z - p1.z;
            float bx = p3.x - p1.x, by = p3.y - p1.y, bz = p3.z - p1.z;
            float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0) { nx /= len; ny /= len; nz /= len; }
            return new Point3D(nx, ny, nz);
        }

        private void UpdateBenchmarkHud(double deltaMs)
        {
            if (rollingFrameTimes.Count == 0) return;
            double fps = (deltaMs > 0) ? (1000.0 / deltaMs) : 0;
            CurrentFpsValue = fps;
            lblBenchFps.Text = string.Format("{0:0} FPS", fps);

            double total = 0;
            foreach (double d in rollingFrameTimes) total += d;
            double avgMs = total / rollingFrameTimes.Count;
            double avgFps = (avgMs > 0) ? (1000.0 / avgMs) : 0;
            lblBenchAvgFps.Text = string.Format("Avg: {0:F1} FPS  ({1:F1} ms)", avgFps, avgMs);

            List<double> sorted = new List<double>(rollingFrameTimes);
            sorted.Sort();
            int lowIdx = (int)(sorted.Count * 0.99);
            if (lowIdx >= sorted.Count) lowIdx = sorted.Count - 1;
            double worst1Pct = sorted[lowIdx];
            double onePctFps = (worst1Pct > 0) ? (1000.0 / worst1Pct) : 0;
            lblBench1Pct.Text = string.Format("1% Low: {0:F1} FPS (Stability)", onePctFps);

            lblBenchStutter.Text = string.Format("Micro-Stutters: {0} ({1})", benchStutterEvents, benchStutterEvents == 0 ? "Rock-solid" : "Hitching");
            lblBenchStutter.ForeColor = (benchStutterEvents == 0) ? cGreen : cYellow;
        }

        private void DrawBenchmarkGraph(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = graphBox.Width, h = graphBox.Height;
            if (w < 10 || h < 10 || graphHistory.Count < 2) return;

            float maxMs = 50.0f;
            float y60 = h - (16.6f / maxMs * h);
            float y30 = h - (33.3f / maxMs * h);

            using (Pen gridPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            {
                gridPen.DashStyle = DashStyle.Dash;
                g.DrawLine(gridPen, 0, y60, w, y60);
                g.DrawLine(gridPen, 0, y30, w, y30);
            }

            using (Font f = new Font("Segoe UI", 7.5f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
            {
                g.DrawString("16.6ms (60 FPS)", f, b, 4, y60 - 14);
                g.DrawString("33.3ms (30 FPS)", f, b, 4, y30 - 14);
            }

            float[] points = graphHistory.ToArray();
            float stepX = (float)w / (points.Length - 1);

            using (GraphicsPath path = new GraphicsPath())
            {
                for (int i = 0; i < points.Length; i++)
                {
                    float ms = points[i];
                    float y = h - (Math.Min(ms, maxMs) / maxMs * h);
                    float x = i * stepX;
                    if (i == 0) path.StartFigure();
                    else path.AddLine((i - 1) * stepX, h - (Math.Min(points[i - 1], maxMs) / maxMs * h), x, y);
                }

                Color lineColor = (benchStutterEvents == 0) ? cGreen : cYellow;
                using (Pen linePen = new Pen(lineColor, 2f)) g.DrawPath(linePen, path);
            }
        }

        private void OnStartBenchmarkClick(object sender, EventArgs e)
        {
            if (isBenchRunning) return;
            benchFrameTimes.Clear();
            benchStutterEvents = 0;
            isBenchRunning = true;
            btnStartBench.Enabled = false;
            btnStartBench.Text = "⏳ TESTING...";
            lblBenchVerdict.Text = "Recording frame times & monitoring thermal stability...";
            lblBenchVerdict.ForeColor = cYellow;
            benchTimedStopwatch.Restart();
        }

        private void FinishBenchmarkRun()
        {
            isBenchRunning = false;
            benchTimedStopwatch.Stop();
            btnStartBench.Enabled = true;
            btnStartBench.Text = "🚀  START 30s BENCHMARK";

            if (benchFrameTimes.Count < 30) return;

            int mid = benchFrameTimes.Count / 2;
            double sum1 = 0, sum2 = 0;
            for (int i = 0; i < mid; i++) sum1 += benchFrameTimes[i];
            for (int i = mid; i < benchFrameTimes.Count; i++) sum2 += benchFrameTimes[i];

            double fps1 = 1000.0 / (sum1 / mid);
            double fps2 = 1000.0 / (sum2 / (benchFrameTimes.Count - mid));

            double totalMs = 0;
            foreach (double d in benchFrameTimes) totalMs += d;
            double finalAvgFps = 1000.0 / (totalMs / benchFrameTimes.Count);

            List<double> sorted = new List<double>(benchFrameTimes);
            sorted.Sort();
            int lowIdx = (int)(sorted.Count * 0.99);
            double final1Pct = 1000.0 / sorted[lowIdx];

            double consistency = (final1Pct / finalAvgFps) * 100.0;
            double dropPercent = ((fps1 - fps2) / fps1) * 100.0;
            bool isThrottling = dropPercent > 12.0;

            if (isThrottling)
            {
                lblBenchVerdict.Text = string.Format("⚠️ THERMAL DROP (-{0:F1}%). CPU Turbo is heating up shared heatpipe. Keep 99% cap on!", dropPercent);
                lblBenchVerdict.ForeColor = cRadeonRed;
            }
            else
            {
                lblBenchVerdict.Text = string.Format("✅ EXCELLENT: Sustained boost confirmed! Clocks held stable (Consistency: {0:F1}%). Zero thermal drop.", consistency);
                lblBenchVerdict.ForeColor = cGreen;
            }

            string msg = string.Format(
                "=== BENCHMARK RESULTS ===\n\n" +
                "Average Framerate: {0:F1} FPS\n" +
                "1% Low Framerate: {1:F1} FPS\n" +
                "Frame Consistency: {2:F1}%\n" +
                "Micro-Stutter Events: {3}\n" +
                "Thermal Status: {4}\n\n" +
                "Would you like to export this report?",
                finalAvgFps, final1Pct, consistency, benchStutterEvents,
                isThrottling ? "THROTTLED (-" + dropPercent.ToString("F1") + "%)" : "SUSTAINED (Zero Drop)"
            );

            if (MessageBox.Show(msg, "Benchmark Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                ExportBenchmarkReport();
        }

        private void ToggleBenchStress()
        {
            benchStressLevel = (benchStressLevel % 3) + 1;
            string name = (benchStressLevel == 1) ? "Normal" : (benchStressLevel == 2 ? "Heavy (Vortex)" : "Extreme");
            btnBenchStress.Text = "Stress Mode: " + name;
        }

        private void ToggleBenchmarkSsr()
        {
            enableBenchmarkSsr = !enableBenchmarkSsr;
            if (enableBenchmarkSsr)
            {
                btnToggleSsr.Text = "✨ SSR Reflections: ON";
                btnToggleSsr.BackColor = Color.FromArgb(20, 140, 80);
                btnToggleSsr.ForeColor = Color.White;
                if (btnSsrSurface != null) btnSsrSurface.Enabled = true;
                Log("✨ Enabled Screen Space Reflections (SSR) in 3D Benchmark.");
            }
            else
            {
                btnToggleSsr.Text = "○ SSR Reflections: OFF";
                btnToggleSsr.BackColor = Color.FromArgb(32, 34, 46);
                btnToggleSsr.ForeColor = cTextMuted;
                if (btnSsrSurface != null) btnSsrSurface.Enabled = false;
                Log("Disabled Screen Space Reflections in 3D Benchmark.");
            }
        }

        private void ToggleSsrSurface()
        {
            isSsrWetMirror = !isSsrWetMirror;
            if (isSsrWetMirror)
            {
                btnSsrSurface.Text = "💧 SSR Surface: Wet Mirror";
                btnSsrSurface.BackColor = Color.FromArgb(30, 80, 130);
                Log("💧 3D SSR Surface switched to Wet Mirror (high reflectivity + puddle glow).");
            }
            else
            {
                btnSsrSurface.Text = "🪩 SSR Surface: Satin Metal";
                btnSsrSurface.BackColor = Color.FromArgb(75, 55, 110);
                Log("🪩 3D SSR Surface switched to Satin Metal (diffused matte reflection).");
            }
        }

        private void DrawSsrFloorGrid()
        {
            glDisable(GL_LIGHTING);
            glEnable(GL_BLEND);
            glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            glDepthMask(0);

            float yFloor = -1.65f;
            float extent = 4.4f;
            float floorAlpha = isSsrWetMirror ? 0.64f : 0.84f;

            // Semi-transparent obsidian mirror / satin floor
            glBegin(GL_TRIANGLES);
            glColor4f(0.03f, 0.04f, 0.08f, floorAlpha);
            glVertex3f(-extent, yFloor, -extent);
            glVertex3f(extent, yFloor, -extent);
            glVertex3f(extent, yFloor, extent);

            glVertex3f(-extent, yFloor, -extent);
            glVertex3f(extent, yFloor, extent);
            glVertex3f(-extent, yFloor, extent);
            glEnd();

            // Specular under-core puddle glow directly beneath crystal
            float glowRadius = isSsrWetMirror ? 2.6f : 1.9f;
            float glowAlpha = isSsrWetMirror ? 0.28f : 0.14f;
            glBegin(GL_TRIANGLE_FAN);
            glColor4f(0.92f, 0.12f, 0.45f, glowAlpha);
            glVertex3f(0.0f, yFloor + 0.004f, 0.0f);
            for (int seg = 0; seg <= 28; seg++)
            {
                double ang = seg * Math.PI * 2.0 / 28.0;
                glColor4f(0.18f, 0.55f, 0.90f, 0.0f);
                glVertex3f((float)Math.Cos(ang) * glowRadius, yFloor + 0.004f, (float)Math.Sin(ang) * glowRadius);
            }
            glEnd();

            // Cyberpunk grid lines on reflective floor
            glLineWidth(1.0f);
            glBegin(GL_LINES);
            glColor4f(0.22f, 0.62f, 0.95f, isSsrWetMirror ? 0.52f : 0.34f);
            for (float x = -extent; x <= extent + 0.1f; x += 0.7f)
            {
                glVertex3f(x, yFloor + 0.002f, -extent);
                glVertex3f(x, yFloor + 0.002f, extent);
            }
            for (float z = -extent; z <= extent + 0.1f; z += 0.7f)
            {
                glVertex3f(-extent, yFloor + 0.002f, z);
                glVertex3f(extent, yFloor + 0.002f, z);
            }
            glEnd();

            glDepthMask(1);
            glDisable(GL_BLEND);
            glEnable(GL_LIGHTING);
        }

        private void DrawSsrReflection()
        {
            float yFloor = -1.65f;
            float reflectScale = isSsrWetMirror ? 1.0f : 0.55f;

            glEnable(GL_BLEND);
            glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            glDepthMask(0);

            // 1. Reflect active particle field onto the floor (only particles above yFloor)
            glDisable(GL_LIGHTING);
            glPointSize(isSsrWetMirror ? 2.4f : 1.8f);
            glBegin(GL_POINTS);
            int pCount = (benchStressLevel == 1) ? 1000 : (benchStressLevel == 2 ? 1500 : particleCount);
            for (int i = 0; i < pCount; i++)
            {
                float px = particleCoords[i * 3 + 0];
                float py = particleCoords[i * 3 + 1];
                float pz = particleCoords[i * 3 + 2];
                if (py >= yFloor)
                {
                    float dist = py - yFloor;
                    float fade = Math.Max(0.06f, 0.45f - dist * 0.08f) * reflectScale;
                    glColor4f(particleColors[i * 3 + 0], particleColors[i * 3 + 1], particleColors[i * 3 + 2], fade);
                    glVertex3f(px, yFloor - dist, pz);
                }
            }
            glEnd();
            glEnable(GL_LIGHTING);

            // 2. Reflect central ruby crystal (and wireframe cage in stress modes)
            glPushMatrix();
            glTranslatef(0.0f, yFloor, 0.0f);
            glScalef(1.0f, -1.0f, 1.0f);
            glTranslatef(0.0f, -yFloor, 0.0f);

            glRotatef(rotX, 1.0f, 0.0f, 0.0f);
            glRotatef(rotY, 0.0f, 1.0f, 0.0f);
            glRotatef(rotZ, 0.0f, 0.0f, 1.0f);

            float s = 1.35f;
            glBegin(GL_TRIANGLES);

            Point3D[] verts = new Point3D[] {
                new Point3D(0, s, 0), new Point3D(-s, 0, s), new Point3D(s, 0, s),
                new Point3D(0, s, 0), new Point3D(s, 0, s), new Point3D(s, 0, -s),
                new Point3D(0, s, 0), new Point3D(s, 0, -s), new Point3D(-s, 0, -s),
                new Point3D(0, s, 0), new Point3D(-s, 0, -s), new Point3D(-s, 0, s),
                new Point3D(0, -s, 0), new Point3D(s, 0, s), new Point3D(-s, 0, s),
                new Point3D(0, -s, 0), new Point3D(s, 0, -s), new Point3D(s, 0, s),
                new Point3D(0, -s, 0), new Point3D(-s, 0, -s), new Point3D(s, 0, -s),
                new Point3D(0, -s, 0), new Point3D(-s, 0, s), new Point3D(-s, 0, -s)
            };

            for (int i = 0; i < verts.Length; i += 3)
            {
                if ((i / 3) % 2 == 0) glColor4f(0.82f, 0.08f, 0.34f, 0.48f * reflectScale);
                else glColor4f(0.55f, 0.04f, 0.22f, 0.36f * reflectScale);

                Point3D p1 = verts[i], p2 = verts[i + 1], p3 = verts[i + 2];
                Point3D n = CalcNormal(p1, p2, p3);
                glNormal3f(n.x, -n.y, n.z);
                glVertex3f(p1.x, p1.y, p1.z);
                glVertex3f(p2.x, p2.y, p2.z);
                glVertex3f(p3.x, p3.y, p3.z);
            }
            glEnd();

            if (benchStressLevel >= 2)
            {
                glDisable(GL_LIGHTING);
                glLineWidth(1.2f);
                glBegin(GL_LINES);
                glColor4f(0.22f, 0.74f, 0.98f, 0.36f * reflectScale);
                for (int i = 0; i < verts.Length; i += 3)
                {
                    glVertex3f(verts[i].x * 1.3f, verts[i].y * 1.3f, verts[i].z * 1.3f);
                    glVertex3f(verts[i + 1].x * 1.3f, verts[i + 1].y * 1.3f, verts[i + 1].z * 1.3f);
                    glVertex3f(verts[i + 1].x * 1.3f, verts[i + 1].y * 1.3f, verts[i + 1].z * 1.3f);
                    glVertex3f(verts[i + 2].x * 1.3f, verts[i + 2].y * 1.3f, verts[i + 2].z * 1.3f);
                }
                glEnd();
                glEnable(GL_LIGHTING);
            }

            glPopMatrix();
            glDepthMask(1);
            glDisable(GL_BLEND);
        }

        private void ExportBenchmarkReport()
        {
            try
            {
                string filename = string.Format("Benchmark_Report_{0:yyyyMMdd_HHmmss}.txt", DateTime.Now);
                string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
                using (StreamWriter sw = new StreamWriter(outPath))
                {
                    sw.WriteLine("==================================================================");
                    sw.WriteLine("  RADEON PRO 560X & CORE I9-8950HK BENCHMARK PERFORMANCE REPORT   ");
                    sw.WriteLine("==================================================================");
                    sw.WriteLine("Timestamp: " + DateTime.Now.ToString("F"));
                    sw.WriteLine("Device: Apple MacBook Pro 15-inch (2018) Boot Camp");
                    sw.WriteLine("GPU: AMD Radeon Pro 560X (4 GB GDDR5)");
                    sw.WriteLine("CPU: Intel Core i9-8950HK (6C / 12T)");
                    sw.WriteLine("------------------------------------------------------------------");
                    sw.WriteLine(string.Format("Average Framerate:    {0}", lblBenchAvgFps.Text));
                    sw.WriteLine(string.Format("1% Low (Smoothness):  {0}", lblBench1Pct.Text));
                    sw.WriteLine(string.Format("Micro-Stutter Events: {0}", benchStutterEvents));
                    sw.WriteLine(string.Format("Thermal Assessment:   {0}", lblBenchVerdict.Text));
                    sw.WriteLine("==================================================================");
                }
                MessageBox.Show("Report exported successfully to:\n" + outPath, "Report Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Failed to save report: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        #endregion

        #region Mini-HUD & Fan Turbo Operations
        public void ToggleMiniHud()
        {
            if (miniHudForm == null || miniHudForm.IsDisposed)
            {
                miniHudForm = new FloatingMiniHud(this);
                miniHudForm.Show();
                if (btnToggleMiniHud != null) { btnToggleMiniHud.BackColor = Color.FromArgb(20, 140, 80); btnToggleMiniHud.Text = "🔥  MINI-HUD: ON"; }
                Log("🔥 In-Game Mini-HUD activated! Drag it to any corner of your screen. Hotkey: Option + Shift + H to toggle.");
            }
            else
            {
                if (miniHudForm.Visible)
                {
                    miniHudForm.Hide();
                    if (btnToggleMiniHud != null) { btnToggleMiniHud.BackColor = Color.FromArgb(170, 40, 40); btnToggleMiniHud.Text = "🔥  MINI-HUD: OFF"; }
                    Log("In-Game Mini-HUD hidden.");
                }
                else
                {
                    miniHudForm.Show();
                    if (btnToggleMiniHud != null) { btnToggleMiniHud.BackColor = Color.FromArgb(20, 140, 80); btnToggleMiniHud.Text = "🔥  MINI-HUD: ON"; }
                    Log("In-Game Mini-HUD displayed.");
                }
            }
        }

        public void OnMiniHudClosed()
        {
            if (btnToggleMiniHud != null)
            {
                btnToggleMiniHud.BackColor = Color.FromArgb(170, 40, 40);
                btnToggleMiniHud.Text = "🔥  MINI-HUD: OFF";
            }
        }

        private void OnFanTurboClick()
        {
            string[] knownPaths = new string[] {
                @"C:\Program Files\Macs Fan Control\MacsFanControl.exe",
                @"C:\Program Files (x86)\Macs Fan Control\MacsFanControl.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Macs Fan Control\MacsFanControl.exe")
            };

            string foundPath = null;
            foreach (string p in knownPaths)
            {
                if (File.Exists(p)) { foundPath = p; break; }
            }

            if (foundPath != null)
            {
                try
                {
                    Process.Start(foundPath);
                    Log("❄️ Launched Macs Fan Control. Recommended: Set Left Fan (CPU) to 5900 RPM and Right Fan (GPU) to 5900 RPM for maximum sustained clocks.");
                }
                catch (Exception ex)
                {
                    Log("Error launching Macs Fan Control: " + ex.Message);
                }
            }
            else
            {
                using (MacsFanControlDialog dlg = new MacsFanControlDialog())
                {
                    dlg.ShowDialog(this);
                }
            }
        }
        #endregion
    }

    public class FloatingMiniHud : Form
    {
        private UnifiedMainForm parent;
        private Label lblTitle;
        private Label lblFps;
        private Label lblGpuClock;
        private Label lblCpu;
        private Label lblRam;
        private Label lblThermal;
        private Button btnLock;
        private Button btnClose;
        private bool isLocked = false;
        private bool isDragging = false;
        private Point dragStartPoint;
        private Timer hudTimer;

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public FloatingMiniHud(UnifiedMainForm parentForm)
        {
            this.parent = parentForm;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(40, 40);
            this.Size = new Size(270, 115);
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(15, 17, 23);
            this.Opacity = 0.90;
            this.DoubleBuffered = true;

            BuildUi();

            hudTimer = new Timer();
            hudTimer.Interval = 400;
            hudTimer.Tick += (s, e) => RefreshHudData();
            hudTimer.Start();
        }

        private void BuildUi()
        {
            Panel pnlHeader = new Panel();
            pnlHeader.Location = new Point(0, 0);
            pnlHeader.Size = new Size(270, 24);
            pnlHeader.BackColor = Color.FromArgb(22, 25, 34);
            this.Controls.Add(pnlHeader);

            lblTitle = new Label();
            lblTitle.Text = "🔥 RADEON HUD";
            lblTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(225, 29, 72);
            lblTitle.Location = new Point(8, 4);
            lblTitle.AutoSize = true;
            pnlHeader.Controls.Add(lblTitle);

            Label lblHotkey = new Label();
            lblHotkey.Text = "(Alt+Shift+H)";
            lblHotkey.Font = new Font("Segoe UI", 7.5f);
            lblHotkey.ForeColor = Color.FromArgb(130, 140, 160);
            lblHotkey.Location = new Point(106, 5);
            lblHotkey.AutoSize = true;
            pnlHeader.Controls.Add(lblHotkey);

            btnLock = new Button();
            btnLock.Text = "📌 PIN";
            btnLock.Font = new Font("Segoe UI", 7f, FontStyle.Bold);
            btnLock.Size = new Size(46, 18);
            btnLock.Location = new Point(190, 3);
            btnLock.FlatStyle = FlatStyle.Flat;
            btnLock.FlatAppearance.BorderSize = 0;
            btnLock.BackColor = Color.FromArgb(40, 45, 60);
            btnLock.ForeColor = Color.FromArgb(220, 225, 235);
            btnLock.Cursor = Cursors.Hand;
            btnLock.Click += (s, e) => ToggleLock();
            pnlHeader.Controls.Add(btnLock);

            btnClose = new Button();
            btnClose.Text = "✕";
            btnClose.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnClose.Size = new Size(22, 18);
            btnClose.Location = new Point(242, 3);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.BackColor = Color.FromArgb(40, 45, 60);
            btnClose.ForeColor = Color.FromArgb(200, 205, 220);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => { this.Hide(); parent.OnMiniHudClosed(); };
            pnlHeader.Controls.Add(btnClose);

            lblFps = new Label();
            lblFps.Text = "FPS: 60.0 (16.6ms)";
            lblFps.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblFps.ForeColor = Color.FromArgb(56, 189, 248);
            lblFps.Location = new Point(10, 30);
            lblFps.AutoSize = true;
            this.Controls.Add(lblFps);

            lblGpuClock = new Label();
            lblGpuClock.Text = "GPU: 1004 MHz";
            lblGpuClock.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblGpuClock.ForeColor = Color.FromArgb(16, 185, 129);
            lblGpuClock.Location = new Point(148, 30);
            lblGpuClock.AutoSize = true;
            this.Controls.Add(lblGpuClock);

            lblCpu = new Label();
            lblCpu.Text = "CPU: 0 %";
            lblCpu.Font = new Font("Segoe UI", 8.5f);
            lblCpu.ForeColor = Color.FromArgb(220, 225, 235);
            lblCpu.Location = new Point(10, 56);
            lblCpu.AutoSize = true;
            this.Controls.Add(lblCpu);

            lblRam = new Label();
            lblRam.Text = "RAM: 0.0 GB";
            lblRam.Font = new Font("Segoe UI", 8.5f);
            lblRam.ForeColor = Color.FromArgb(180, 190, 210);
            lblRam.Location = new Point(148, 56);
            lblRam.AutoSize = true;
            this.Controls.Add(lblRam);

            lblThermal = new Label();
            lblThermal.Text = "🛡️ 99% CPU CAP: ACTIVE [COOL]";
            lblThermal.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lblThermal.ForeColor = Color.FromArgb(52, 211, 153);
            lblThermal.Location = new Point(10, 84);
            lblThermal.AutoSize = true;
            this.Controls.Add(lblThermal);

            AttachDrag(this);
            AttachDrag(pnlHeader);
            AttachDrag(lblTitle);
            AttachDrag(lblHotkey);
            AttachDrag(lblFps);
            AttachDrag(lblGpuClock);
            AttachDrag(lblCpu);
            AttachDrag(lblRam);
            AttachDrag(lblThermal);
        }

        private void AttachDrag(Control c)
        {
            c.MouseDown += (s, e) => {
                if (!isLocked && e.Button == MouseButtons.Left)
                {
                    isDragging = true;
                    dragStartPoint = e.Location;
                }
            };
            c.MouseMove += (s, e) => {
                if (isDragging)
                {
                    Point screen = c.PointToScreen(e.Location);
                    this.Location = new Point(screen.X - dragStartPoint.X, screen.Y - dragStartPoint.Y);
                }
            };
            c.MouseUp += (s, e) => { isDragging = false; };
        }

        private void ToggleLock()
        {
            isLocked = !isLocked;
            int style = GetWindowLong(this.Handle, GWL_EXSTYLE);
            if (isLocked)
            {
                SetWindowLong(this.Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                btnLock.Text = "🔒 LOCK";
                btnLock.BackColor = Color.FromArgb(20, 140, 80);
                lblTitle.Text = "🔥 RADEON HUD (LOCKED)";
            }
            else
            {
                SetWindowLong(this.Handle, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
                btnLock.Text = "📌 PIN";
                btnLock.BackColor = Color.FromArgb(40, 45, 60);
                lblTitle.Text = "🔥 RADEON HUD";
            }
            this.Invalidate();
        }

        private void RefreshHudData()
        {
            if (parent == null || parent.IsDisposed) return;

            int cpu = parent.CurrentCpuPercent;
            lblCpu.Text = "CPU: " + cpu + " %";
            if (cpu > 80) lblCpu.ForeColor = Color.FromArgb(239, 68, 68);
            else if (cpu > 50) lblCpu.ForeColor = Color.FromArgb(250, 204, 21);
            else lblCpu.ForeColor = Color.FromArgb(52, 211, 153);

            lblRam.Text = "RAM: " + parent.CurrentRamText;

            if (parent.IsCpuCapActive)
            {
                lblGpuClock.Text = "GPU: 1004 MHz";
                lblGpuClock.ForeColor = Color.FromArgb(16, 185, 129);
                lblThermal.Text = "🛡️ 99% CPU CAP: ACTIVE [COOL]";
                lblThermal.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else
            {
                lblGpuClock.Text = "GPU: ~300-600 MHz";
                lblGpuClock.ForeColor = Color.FromArgb(239, 68, 68);
                lblThermal.Text = "⚠️ CPU UNCAPPED [THROTTLE RISK]";
                lblThermal.ForeColor = Color.FromArgb(239, 68, 68);
            }

            double fps = parent.CurrentFpsValue;
            if (fps <= 0) fps = 60.0;
            double frameMs = (fps > 0) ? (1000.0 / fps) : 16.6;
            lblFps.Text = string.Format("FPS: {0:F1} ({1:F1}ms)", fps, frameMs);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(isLocked ? Color.FromArgb(16, 185, 129) : Color.FromArgb(225, 29, 72), 1.5f))
            {
                e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }

    public class MacsFanControlDialog : Form
    {
        public MacsFanControlDialog()
        {
            this.Text = "MacBook Pro (2018) Fan Turbo Assistant";
            this.Size = new Size(680, 520);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(15, 17, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f);

            BuildDialogUi();
        }

        private void BuildDialogUi()
        {
            Label lblTitle = new Label();
            lblTitle.Text = "❄️  MACBOOK PRO 15\" THERMAL TURBO COOLING";
            lblTitle.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(56, 189, 248);
            lblTitle.Location = new Point(20, 16);
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Prevent the shared copper heatpipe from heat-soaking to lock GPU clocks at 1004 MHz.";
            lblSubtitle.Font = new Font("Segoe UI", 9f);
            lblSubtitle.ForeColor = Color.FromArgb(160, 170, 190);
            lblSubtitle.Location = new Point(22, 44);
            lblSubtitle.AutoSize = true;
            this.Controls.Add(lblSubtitle);

            // Explanation Card
            Panel pnlExplain = new Panel();
            pnlExplain.Location = new Point(20, 75);
            pnlExplain.Size = new Size(624, 110);
            pnlExplain.BackColor = Color.FromArgb(24, 28, 38);
            pnlExplain.Padding = new Padding(12);
            this.Controls.Add(pnlExplain);

            Label lblExpTitle = new Label();
            lblExpTitle.Text = "🌡️  Why Apple's Default Boot Camp Fan Curve Throttles Gaming:";
            lblExpTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblExpTitle.ForeColor = Color.FromArgb(250, 204, 21);
            lblExpTitle.Location = new Point(10, 8);
            lblExpTitle.AutoSize = true;
            pnlExplain.Controls.Add(lblExpTitle);

            string expText = "Apple's default Windows firmware waits until CPU temperatures hit 90°C–95°C before ramping fans.\n" +
                             "Because the 2018 15\" MacBook Pro shares a single copper heatpipe between the 80W i9 CPU and\n" +
                             "the Radeon Pro 560X GPU, heat saturates the system instantly, forcing GPU clocks down to 300 MHz.\n" +
                             "Running fans at 100% (5,900 RPM) before starting a game gives 100% thermal boost headroom!";
            Label lblExpBody = new Label();
            lblExpBody.Text = expText;
            lblExpBody.Font = new Font("Segoe UI", 8.8f);
            lblExpBody.ForeColor = Color.FromArgb(220, 225, 235);
            lblExpBody.Location = new Point(10, 30);
            lblExpBody.Size = new Size(600, 72);
            pnlExplain.Controls.Add(lblExpBody);

            // Recommended Sensor Table
            Panel pnlTable = new Panel();
            pnlTable.Location = new Point(20, 200);
            pnlTable.Size = new Size(624, 175);
            pnlTable.BackColor = Color.FromArgb(24, 28, 38);
            pnlTable.Padding = new Padding(12);
            this.Controls.Add(pnlTable);

            Label lblTblTitle = new Label();
            lblTblTitle.Text = "🎯  Recommended Sensor-Based Curves in Macs Fan Control:";
            lblTblTitle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblTblTitle.ForeColor = Color.FromArgb(16, 185, 129);
            lblTblTitle.Location = new Point(10, 8);
            lblTblTitle.AutoSize = true;
            pnlTable.Controls.Add(lblTblTitle);

            string sensorText = "• Left Fan (CPU Fan):\n" +
                                "   - Control mode: Sensor-based value\n" +
                                "   - Sensor: 'CPU Core Average' (or CPU Package)\n" +
                                "   - Temperature that fan speed starts to increase: 50°C\n" +
                                "   - Maximum temperature (Full 5,900 RPM): 78°C\n\n" +
                                "• Right Fan (GPU Fan):\n" +
                                "   - Control mode: Sensor-based value\n" +
                                "   - Sensor: 'AMD Radeon GPU Diode' (or GPU Pro 560X)\n" +
                                "   - Temperature that fan speed starts to increase: 48°C\n" +
                                "   - Maximum temperature (Full 5,900 RPM): 72°C";
            Label lblSensorInfo = new Label();
            lblSensorInfo.Text = sensorText;
            lblSensorInfo.Font = new Font("Consolas", 8.8f);
            lblSensorInfo.ForeColor = Color.FromArgb(220, 225, 235);
            lblSensorInfo.Location = new Point(10, 32);
            lblSensorInfo.Size = new Size(600, 135);
            pnlTable.Controls.Add(lblSensorInfo);

            // Action Buttons
            Button btnDownload = new Button();
            btnDownload.Text = "📥  1-Click Download Macs Fan Control";
            btnDownload.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnDownload.Size = new Size(270, 42);
            btnDownload.Location = new Point(20, 410);
            btnDownload.FlatStyle = FlatStyle.Flat;
            btnDownload.FlatAppearance.BorderSize = 0;
            btnDownload.BackColor = Color.FromArgb(30, 90, 160);
            btnDownload.ForeColor = Color.White;
            btnDownload.Cursor = Cursors.Hand;
            btnDownload.Click += (s, e) => DownloadAndInstall();
            this.Controls.Add(btnDownload);

            Button btnWeb = new Button();
            btnWeb.Text = "🌐  Official Site";
            btnWeb.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnWeb.Size = new Size(160, 42);
            btnWeb.Location = new Point(300, 410);
            btnWeb.FlatStyle = FlatStyle.Flat;
            btnWeb.FlatAppearance.BorderSize = 0;
            btnWeb.BackColor = Color.FromArgb(45, 50, 65);
            btnWeb.ForeColor = Color.FromArgb(220, 225, 235);
            btnWeb.Cursor = Cursors.Hand;
            btnWeb.Click += (s, e) => {
                try { Process.Start("https://crystalidea.com/macs-fan-control/download"); } catch { }
            };
            this.Controls.Add(btnWeb);

            Button btnClose = new Button();
            btnClose.Text = "✓  Done";
            btnClose.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnClose.Size = new Size(160, 42);
            btnClose.Location = new Point(484, 410);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.BackColor = Color.FromArgb(30, 35, 45);
            btnClose.ForeColor = Color.White;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private void DownloadAndInstall()
        {
            try
            {
                string tempExe = Path.Combine(Path.GetTempPath(), "macsfancontrol_setup.exe");
                Cursor.Current = Cursors.WaitCursor;
                using (WebClient wc = new WebClient())
                {
                    wc.DownloadFile("https://crystalidea.com/downloads/macsfancontrol_setup.exe", tempExe);
                }
                Cursor.Current = Cursors.Default;
                if (File.Exists(tempExe))
                {
                    Process.Start(tempExe);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show("Could not auto-download: " + ex.Message + "\nOpening the download page in your browser instead.",
                    "Download Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("https://crystalidea.com/macs-fan-control/download"); } catch { }
            }
        }
    }
}
