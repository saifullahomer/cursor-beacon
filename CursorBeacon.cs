// Cursor Beacon - find your mouse pointer on multi-monitor setups.
// Shake the mouse or double-tap Ctrl and a glowing beacon (plus an optional
// screen-dimming spotlight) appears around the cursor.
//
// Built with the C# compiler that ships with Windows (.NET Framework 4.x),
// so no runtime needs to be installed. See build.ps1.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Cursor Beacon")]
[assembly: System.Reflection.AssemblyProduct("Cursor Beacon")]
[assembly: System.Reflection.AssemblyDescription("Shake the mouse or double-tap Ctrl to find your cursor")]

namespace CursorBeacon
{
    // ------------------------------------------------------------------ Win32
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204,
                         WM_MBUTTONDOWN = 0x207, WM_MOUSEWHEEL = 0x20A, WM_XBUTTONDOWN = 0x20B, WM_QUIT = 0x12;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                         WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        public const uint LWA_ALPHA = 0x2, ULW_ALPHA = 0x2;
        public const int RGN_DIFF = 4;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, uint crKey, ref BLENDFUNCTION blend, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateEllipticRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dx, out uint dy);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint p);

        public static float ScaleAt(POINT p)
        {
            try
            {
                uint dx, dy;
                IntPtr mon = MonitorFromPoint(p, 2 /* NEAREST */);
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            return 1f;
        }

        public static void TrimMemory()
        {
            try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
        }
    }

    // --------------------------------------------------------------- Settings
    internal class Settings
    {
        const string KeyPath = @"Software\CursorBeacon";
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "CursorBeacon";

        public static readonly string[] ColorNames = { "Aqua", "Violet", "Amber", "Lime", "Coral" };
        public static readonly Color[] Colors = {
            Color.FromArgb(0x22, 0xD3, 0xEE), Color.FromArgb(0xA7, 0x8B, 0xFA), Color.FromArgb(0xFB, 0xBF, 0x24),
            Color.FromArgb(0xA3, 0xE6, 0x35), Color.FromArgb(0xFB, 0x71, 0x85) };
        public static readonly string[] SensitivityNames = { "Low", "Normal", "High" };

        public volatile bool Shake = true, DoubleCtrl = true;
        public bool Dim = true, AutoUpdate = true;
        public int ColorIndex = 0;
        public volatile int Sensitivity = 1;

        public Color Accent { get { return Colors[Math.Max(0, Math.Min(Colors.Length - 1, ColorIndex))]; } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k != null)
                    {
                        s.Shake = Get(k, "Shake", 1) != 0;
                        s.DoubleCtrl = Get(k, "DoubleCtrl", 1) != 0;
                        s.Dim = Get(k, "Dim", 1) != 0;
                        s.AutoUpdate = Get(k, "AutoUpdate", 1) != 0;
                        s.ColorIndex = Math.Max(0, Math.Min(Colors.Length - 1, Get(k, "Color", 0)));
                        s.Sensitivity = Math.Max(0, Math.Min(2, Get(k, "Sensitivity", 1)));
                    }
                }
            }
            catch { }
            return s;
        }

        static int Get(RegistryKey k, string name, int def)
        {
            try { return Convert.ToInt32(k.GetValue(name, def)); } catch { return def; }
        }

        public void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("Shake", Shake ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("DoubleCtrl", DoubleCtrl ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("Dim", Dim ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("AutoUpdate", AutoUpdate ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("Color", ColorIndex, RegistryValueKind.DWord);
                    k.SetValue("Sensitivity", Sensitivity, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public static void DeleteAll()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false); } catch { }
            SetStartup(false);
        }

        public static bool GetStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunPath))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --startup");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Drawing
    internal static class Art
    {
        public const float LayerHalf = 272f;   // half-size of the beacon window (at 100% scale)
        public const float StartR = 225f;      // ring radius when the beacon appears
        public const float EndR = 62f;         // ring radius once it has locked on
        public const int DimAlpha = 125;       // darkness of the spotlight overlay (0-255)

        public const double Intro = 0.55, Hold = 1.1, FadeIn = 0.14, FadeOut = 0.4;

        public static float Radius(double t, float s, double level, bool fadingOut)
        {
            double p = Math.Max(0, Math.Min(1, t / Intro));
            double x = p - 1, c1 = 1.25, c3 = c1 + 1;
            double e = 1 + c3 * x * x * x + c1 * x * x;    // ease-out-back: collapses with a small snap
            double r = StartR + (EndR - StartR) * e;
            if (p >= 1) r *= 1 + 0.035 * Math.Sin((t - Intro) * 5.5);   // gentle breathing
            if (fadingOut) r += (1 - level) * 22;                        // drift outward while fading
            return (float)(r * s);
        }

        static Color A(Color c, float alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)alpha)), c); }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static void Circle(Graphics g, Pen p, float cx, float cy, float r) { g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2); }

        public static void DrawBeacon(Graphics g, float cx, float cy, float r, float a, double t, float s, Color accent)
        {
            if (a <= 0.004f) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color light = Mix(accent, Color.White, 0.45f);

            // Lens: faint tint towards the rim, clear in the middle so the pointer stays crisp.
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (PathGradientBrush br = new PathGradientBrush(path))
                {
                    br.CenterPoint = new PointF(cx, cy);
                    br.CenterColor = Color.FromArgb(0, accent);
                    br.SurroundColors = new Color[] { A(accent, 70 * a) };
                    br.FocusScales = new PointF(0.7f, 0.7f);
                    g.FillPath(br, path);
                }
            }

            // Ripples radiating outward once locked on.
            for (int k = 0; k < 2; k++)
            {
                double local = t - 0.35 - k * 0.6;
                if (local < 0) continue;
                float ph = (float)((local % 1.2) / 1.2);
                float rr = r + ph * 115f * s;
                float ra = (1 - ph) * (1 - ph) * 0.75f * a;
                using (Pen p = new Pen(A(accent, 255 * ra), (2.2f + 2f * (1 - ph)) * s)) Circle(g, p, cx, cy, rr);
            }

            // Soft glow: stacked translucent strokes.
            for (int i = 6; i >= 1; i--)
                using (Pen p = new Pen(A(accent, a * 70f / (i * 1.5f)), s * (4f + i * 4.5f))) Circle(g, p, cx, cy, r);

            // Dark hairlines so the ring still pops on white backgrounds.
            using (Pen p = new Pen(A(Color.Black, 75 * a), 1.2f * s))
            {
                Circle(g, p, cx, cy, r + 3.6f * s);
                Circle(g, p, cx, cy, r - 3.6f * s);
            }

            // Core ring.
            using (Pen p = new Pen(A(light, 255 * a), 4f * s)) Circle(g, p, cx, cy, r);
            using (Pen p = new Pen(A(Color.White, 235 * a), 1.6f * s)) Circle(g, p, cx, cy, r);

            // Orbiting arcs.
            float ro = r + 13f * s;
            float ang = (float)((t * 150) % 360);
            using (Pen p = new Pen(A(accent, 225 * a), 3f * s))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                RectangleF rc = new RectangleF(cx - ro, cy - ro, ro * 2, ro * 2);
                g.DrawArc(p, rc, ang, 46); g.DrawArc(p, rc, ang + 180, 46);
            }
            float ri = r - 12f * s;
            float ang2 = (float)((-t * 95) % 360);
            using (Pen p = new Pen(A(light, 150 * a), 1.6f * s))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                RectangleF rc = new RectangleF(cx - ri, cy - ri, ri * 2, ri * 2);
                for (int k = 0; k < 3; k++) g.DrawArc(p, rc, ang2 + k * 120, 28);
            }

            // Targeting notches pointing in at the cursor.
            using (Pen p = new Pen(A(light, 210 * a), 2.6f * s))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                float n1 = r + 24f * s, n2 = r + 36f * s;
                g.DrawLine(p, cx, cy - n1, cx, cy - n2);
                g.DrawLine(p, cx, cy + n1, cx, cy + n2);
                g.DrawLine(p, cx - n1, cy, cx - n2, cy);
                g.DrawLine(p, cx + n1, cy, cx + n2, cy);
            }
        }

        // App / tray icon: glowing ring with a pointer in the middle.
        public static Bitmap RenderIcon(int size, bool background, Color accent)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float s = size / 256f;
                float c = 128f * s;
                float ringR, ringW, k;
                if (background)
                {
                    using (GraphicsPath bg = RoundRect(8 * s, 8 * s, 240 * s, 240 * s, 56 * s))
                    using (LinearGradientBrush br = new LinearGradientBrush(new PointF(0, 0), new PointF(0, size),
                        Color.FromArgb(0x1E, 0x29, 0x3B), Color.FromArgb(0x0B, 0x12, 0x20)))
                        g.FillPath(br, bg);
                    ringR = 80 * s; ringW = Math.Max(16 * s, 1.5f); k = 1.55f * s;
                }
                else { ringR = 104 * s; ringW = Math.Max(24 * s, 1.5f); k = 2.0f * s; }

                if (size >= 48)
                    for (int i = 4; i >= 1; i--)
                        using (Pen p = new Pen(Color.FromArgb(70 / i, accent), ringW + i * 10 * s)) Circle(g, p, c, c, ringR);
                using (Pen p = new Pen(accent, ringW)) Circle(g, p, c, c, ringR);
                if (size >= 48)
                    using (Pen p = new Pen(Color.FromArgb(220, Color.White), ringW * 0.35f)) Circle(g, p, c, c, ringR);

                // Classic pointer arrow, centred in the ring.
                PointF[] arrow = { new PointF(0, 0), new PointF(0, 40), new PointF(10, 31), new PointF(17, 46),
                                   new PointF(24, 43), new PointF(17, 28), new PointF(29, 28) };
                float ox = c - 12 * k, oy = c - 24 * k;
                for (int i = 0; i < arrow.Length; i++) arrow[i] = new PointF(ox + arrow[i].X * k, oy + arrow[i].Y * k);
                using (Pen outline = new Pen(Color.FromArgb(0x0B, 0x12, 0x20), Math.Max(3.5f * s, 1f)))
                {
                    outline.LineJoin = LineJoin.Round;
                    g.FillPolygon(Brushes.White, arrow);
                    g.DrawPolygon(outline, arrow);
                }
            }
            return bmp;
        }

        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Writes a multi-resolution .ico (PNG-compressed entries).
        public static void WriteIco(string path)
        {
            int[] sizes = { 256, 64, 48, 32, 24, 16 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int sz in sizes)
                using (Bitmap b = RenderIcon(sz, true, Settings.Colors[0]))
                using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (FileStream fs = File.Create(path))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] png in pngs) w.Write(png);
            }
        }

        // Renders a static preview sheet (used to check the look without touching the screen).
        public static void WritePreview(string path)
        {
            const int P = 540;
            double[] times = { 0.12, 0.5, 1.45 };
            using (Bitmap sheet = new Bitmap(P * times.Length, P * 2, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < times.Length; col++)
                    {
                        double t = times[col];
                        double level = Math.Min(1, t / FadeIn);
                        int ox = col * P, oy = row * P;
                        float cx = ox + P / 2f, cy = oy + P / 2f;
                        float r = Radius(t, 1f, level, false);
                        g.SetClip(new Rectangle(ox, oy, P, P));
                        // fake desktop
                        Color bgA = row == 0 ? Color.FromArgb(31, 41, 55) : Color.FromArgb(243, 244, 246);
                        Color bgB = row == 0 ? Color.FromArgb(17, 24, 39) : Color.FromArgb(229, 231, 235);
                        using (LinearGradientBrush br = new LinearGradientBrush(new Point(ox, oy), new Point(ox + P, oy + P), bgA, bgB))
                            g.FillRectangle(br, ox, oy, P, P);
                        using (SolidBrush win = new SolidBrush(row == 0 ? Color.FromArgb(55, 65, 81) : Color.White))
                        {
                            g.FillRectangle(win, ox + 30, oy + 40, 300, 200);
                            g.FillRectangle(win, ox + 220, oy + 300, 290, 200);
                        }
                        // spotlight dim
                        using (Region dim = new Region(new Rectangle(ox, oy, P, P)))
                        using (GraphicsPath hole = new GraphicsPath())
                        {
                            hole.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                            dim.Exclude(hole);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(DimAlpha * level), Color.Black)))
                                g.FillRegion(b, dim);
                        }
                        DrawBeacon(g, cx, cy, r, (float)level, t, 1f, Settings.Colors[col % Settings.Colors.Length]);
                        // pointer
                        PointF[] pts = { new PointF(0, 0), new PointF(0, 17), new PointF(4, 13), new PointF(7, 20),
                                         new PointF(10, 19), new PointF(7, 12), new PointF(12, 12) };
                        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(cx + pts[i].X * 1.4f, cy + pts[i].Y * 1.4f);
                        g.FillPolygon(Brushes.White, pts);
                        g.DrawPolygon(Pens.Black, pts);
                        g.ResetClip();
                    }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }

    // --------------------------------------------------------- Overlay windows
    internal class OverlayForm : Form
    {
        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
    }

    // A small per-pixel-alpha window that follows the cursor and shows the ring.
    internal class RingLayer : IDisposable
    {
        readonly OverlayForm form = new OverlayForm();
        readonly int size;
        IntPtr memDC, hbmp, oldBmp, bits;
        Bitmap bmp;
        Graphics g;

        public RingLayer(int size)
        {
            this.size = size;
            IntPtr h = form.Handle;   // create window
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER();
            bi.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bi.biWidth = size; bi.biHeight = -size; bi.biPlanes = 1; bi.biBitCount = 32;
            hbmp = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(memDC, hbmp);
            bmp = new Bitmap(size, size, size * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(bmp);
        }

        public void Render(Point c, float r, float alpha, double t, float s, Color accent)
        {
            g.Clear(Color.Transparent);
            Art.DrawBeacon(g, size / 2f, size / 2f, r, alpha, t, s, accent);
            g.Flush(FlushIntention.Sync);
            Native.GdiFlush();
            Native.POINT dst = new Native.POINT(c.X - size / 2, c.Y - size / 2);
            Native.SIZE sz = new Native.SIZE(size, size);
            Native.POINT src = new Native.POINT(0, 0);
            Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
            bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            Native.UpdateLayeredWindow(form.Handle, screen, ref dst, ref sz, memDC, ref src, 0, ref bf, Native.ULW_ALPHA);
            Native.ReleaseDC(IntPtr.Zero, screen);
            if (!form.Visible)
            {
                form.Show();
                Native.SetWindowPos(form.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
        }

        public void Dispose()
        {
            if (g != null) { g.Dispose(); g = null; }
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (memDC != IntPtr.Zero)
            {
                Native.SelectObject(memDC, oldBmp);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            form.Close();
            form.Dispose();
        }
    }

    // A black, translucent, click-through window per monitor with a hole cut at the cursor.
    internal class DimLayer : IDisposable
    {
        readonly OverlayForm form = new OverlayForm();
        readonly Rectangle b;
        bool hasHole, shown;
        Point lastC = new Point(int.MinValue, int.MinValue);
        int lastR = -1;
        int lastAlpha = -1;

        public DimLayer(Rectangle bounds)
        {
            b = bounds;
            form.BackColor = Color.Black;
            form.Bounds = bounds;
            IntPtr h = form.Handle;
            Native.SetLayeredWindowAttributes(h, 0, 0, Native.LWA_ALPHA);
        }

        public void Update(int alpha, Point c, int r)
        {
            if (!shown)
            {
                shown = true;
                form.Show();
                Native.SetWindowPos(form.Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            bool hit = c.X + r > b.Left && c.X - r < b.Right && c.Y + r > b.Top && c.Y - r < b.Bottom;
            if (hit && (c != lastC || r != lastR))
            {
                int lx = c.X - b.X, ly = c.Y - b.Y;
                IntPtr full = Native.CreateRectRgn(0, 0, b.Width, b.Height);
                IntPtr hole = Native.CreateEllipticRgn(lx - r, ly - r, lx + r + 1, ly + r + 1);
                Native.CombineRgn(full, full, hole, Native.RGN_DIFF);
                Native.DeleteObject(hole);
                Native.SetWindowRgn(form.Handle, full, true);   // system now owns 'full'
                hasHole = true;
            }
            else if (!hit && hasHole)
            {
                Native.SetWindowRgn(form.Handle, IntPtr.Zero, true);
                hasHole = false;
            }
            lastC = c; lastR = r;
            alpha = Math.Max(0, Math.Min(255, alpha));
            if (alpha != lastAlpha)
            {
                Native.SetLayeredWindowAttributes(form.Handle, 0, (byte)alpha, Native.LWA_ALPHA);
                lastAlpha = alpha;
            }
        }

        public void Dispose() { form.Close(); form.Dispose(); }
    }

    // ------------------------------------------------------------- Animation
    internal class Beacon
    {
        readonly Settings settings;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch sw = new Stopwatch();
        readonly List<DimLayer> dims = new List<DimLayer>();
        RingLayer ring;
        bool active;
        double lastTrigger, lastTick, level;
        float scale;

        public Beacon(Settings s)
        {
            settings = s;
            timer.Interval = 10;
            timer.Tick += delegate { Tick(); };
        }

        public bool Active { get { return active; } }

        public void Trigger()
        {
            if (!active) Start();
            lastTrigger = sw.Elapsed.TotalSeconds;
        }

        void Start()
        {
            active = true; level = 0; lastTick = 0; lastTrigger = 0;
            Native.POINT p; Native.GetCursorPos(out p);
            scale = Native.ScaleAt(p);
            ring = new RingLayer((int)Math.Ceiling(Art.LayerHalf * scale) * 2);
            if (settings.Dim)
                foreach (Screen sc in Screen.AllScreens) dims.Add(new DimLayer(sc.Bounds));
            Native.timeBeginPeriod(1);
            sw.Restart();
            timer.Start();
            Tick();
        }

        void Stop()
        {
            timer.Stop();
            Native.timeEndPeriod(1);
            foreach (DimLayer d in dims) d.Dispose();
            dims.Clear();
            if (ring != null) { ring.Dispose(); ring = null; }
            active = false;
            sw.Stop();
            Native.TrimMemory();
        }

        void Tick()
        {
            double t = sw.Elapsed.TotalSeconds;
            double dt = Math.Max(0, t - lastTick);
            lastTick = t;
            bool holding = t - lastTrigger < Art.Hold;
            level = holding ? Math.Min(1, level + dt / Art.FadeIn) : Math.Max(0, level - dt / Art.FadeOut);
            if (!holding && level <= 0) { Stop(); return; }

            float r = Art.Radius(t, scale, level, !holding);
            Native.POINT p; Native.GetCursorPos(out p);
            Point c = new Point(p.X, p.Y);
            double eased = level * level * (3 - 2 * level);   // smoothstep
            int dimA = (int)(Art.DimAlpha * eased);
            int ir = (int)Math.Round(r);
            foreach (DimLayer d in dims) d.Update(dimA, c, ir);
            ring.Render(c, r, (float)eased, t, scale, settings.Accent);
        }
    }

    // --------------------------------------------------------- Shake detection
    internal class ShakeDetector
    {
        class Axis { public bool init; public int dir, extreme; }
        readonly Axis ax = new Axis(), ay = new Axis();
        readonly List<long> reversals = new List<long>();

        // Sensitivity presets: minimum travel per stroke (px @100%), reversals needed, time window (ms)
        static readonly int[] MinSeg = { 90, 60, 40 };
        static readonly int[] Needed = { 5, 4, 3 };
        static readonly int[] Window = { 900, 800, 700 };

        public int Sensitivity = 1;
        public float Scale = 1f;

        public bool OnMove(int x, int y, long ms)
        {
            int seg = (int)(MinSeg[Sensitivity] * Scale);
            bool rx = Step(ax, x, seg), ry = Step(ay, y, seg);
            if (rx || ry) reversals.Add(ms);
            while (reversals.Count > 0 && ms - reversals[0] > Window[Sensitivity]) reversals.RemoveAt(0);
            if (reversals.Count >= Needed[Sensitivity]) { reversals.Clear(); return true; }
            return false;
        }

        static bool Step(Axis a, int v, int seg)
        {
            if (!a.init) { a.init = true; a.extreme = v; a.dir = 0; return false; }
            if (a.dir == 0)
            {
                if (Math.Abs(v - a.extreme) >= seg) { a.dir = Math.Sign(v - a.extreme); a.extreme = v; }
            }
            else if (a.dir > 0)
            {
                if (v > a.extreme) a.extreme = v;
                else if (a.extreme - v >= seg) { a.dir = -1; a.extreme = v; return true; }
            }
            else
            {
                if (v < a.extreme) a.extreme = v;
                else if (v - a.extreme >= seg) { a.dir = 1; a.extreme = v; return true; }
            }
            return false;
        }
    }

    // ---------------------------------------------------------- Input hooks
    // Runs on its own thread so drawing never delays system-wide mouse input.
    internal class InputWatcher
    {
        readonly Settings settings;
        readonly Action onTrigger;
        readonly ShakeDetector shake = new ShakeDetector();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        Native.HookProc mouseProc, keyProc;    // keep delegates alive
        IntPtr mouseHook, keyHook;
        uint threadId;
        Thread thread;

        bool ctrlDown, ctrlDirty;
        long ctrlDownAt, lastTap;

        public InputWatcher(Settings s, Action onTrigger)
        {
            settings = s;
            this.onTrigger = onTrigger;
            Native.POINT origin = new Native.POINT(0, 0);
            shake.Scale = Native.ScaleAt(origin);
        }

        public bool Hooked { get { return mouseHook != IntPtr.Zero && keyHook != IntPtr.Zero; } }

        public void Start()
        {
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Name = "InputHooks";
            thread.Start();
            ready.WaitOne(3000);
        }

        public void Stop()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (thread != null) thread.Join(1000);
        }

        void Run()
        {
            threadId = Native.GetCurrentThreadId();
            mouseProc = MouseHook;
            keyProc = KeyHook;
            IntPtr mod = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, mod, 0);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, mod, 0);
            ready.Set();
            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            mouseHook = keyHook = IntPtr.Zero;
        }

        IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_MOUSEMOVE)
                {
                    if (settings.Shake)
                    {
                        Native.MSLLHOOKSTRUCT d = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                        shake.Sensitivity = settings.Sensitivity;
                        if (shake.OnMove(d.pt.X, d.pt.Y, clock.ElapsedMilliseconds)) Fire();
                    }
                }
                else if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN ||
                         msg == Native.WM_XBUTTONDOWN || msg == Native.WM_MOUSEWHEEL)
                {
                    ctrlDirty = true; lastTap = 0;   // Ctrl+click / Ctrl+wheel is not a tap
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                uint vk = (uint)Marshal.ReadInt32(lParam);
                bool isCtrl = vk == 0x11 || vk == 0xA2 || vk == 0xA3;
                long now = clock.ElapsedMilliseconds;
                if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                {
                    if (isCtrl)
                    {
                        if (!ctrlDown) { ctrlDown = true; ctrlDirty = false; ctrlDownAt = now; }
                    }
                    else { ctrlDirty = true; lastTap = 0; }   // a shortcut like Ctrl+C, or normal typing
                }
                else if ((msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP) && isCtrl)
                {
                    if (ctrlDown && !ctrlDirty && now - ctrlDownAt < 350)
                    {
                        if (lastTap != 0 && now - lastTap < 450)
                        {
                            lastTap = 0;
                            if (settings.DoubleCtrl) Fire();
                        }
                        else lastTap = now;
                    }
                    else lastTap = 0;
                    ctrlDown = false;
                }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        void Fire()
        {
            try { onTrigger(); } catch { }
        }
    }

    // --------------------------------------------------------------- Updater
    // Checks the GitHub repo's latest release and swaps in a newer exe.
    internal static class Updater
    {
        public enum Result { UpToDate, Updated, Failed, Disabled }

        public static bool Enabled { get { return !string.IsNullOrEmpty(BuildInfo.Repo); } }

        public static void CleanupOld()
        {
            try
            {
                string old = Application.ExecutablePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }

        public static Result TryUpdate(out string info)
        {
            info = "";
            if (!Enabled) { info = "Updates are off in local builds."; return Result.Disabled; }
            string exe = Application.ExecutablePath, tmp = exe + ".new", old = exe + ".old";
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                string json;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + BuildInfo.Repo + "/releases/latest");
                req.UserAgent = "CursorBeacon-Updater";
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();

                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(?:\\.[0-9]+){1,3})\"");
                if (!tag.Success) { info = "No release found."; return Result.Failed; }
                Version latest = new Version(tag.Groups[1].Value);
                Version current = new Version(BuildInfo.Version);
                if (latest <= current) { info = "You have the latest version (" + BuildInfo.Version + ")."; return Result.UpToDate; }

                Match asset = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(https://[^\"]+/CursorBeacon\\.exe)\"");
                if (!asset.Success) { info = "Release has no CursorBeacon.exe."; return Result.Failed; }

                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "CursorBeacon-Updater");
                    wc.DownloadFile(asset.Groups[1].Value, tmp);
                }
                using (FileStream fs = File.OpenRead(tmp))
                {
                    if (fs.Length < 8192 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                        throw new InvalidDataException("Downloaded file is not a valid program.");
                }
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);      // a running exe can be renamed, just not overwritten
                File.Move(tmp, exe);
                info = latest.ToString();
                return Result.Updated;
            }
            catch (Exception ex)
            {
                try
                {
                    if (!File.Exists(exe) && File.Exists(old)) File.Move(old, exe);
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch { }
                info = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ------------------------------------------------------------ Tray app
    internal class App : ApplicationContext
    {
        readonly Settings settings = Settings.Load();
        readonly Beacon beacon;
        readonly InputWatcher input;
        readonly Control invoker = new Control();
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        readonly string selftestLog;
        volatile bool updating;
        public int ExitCode;

        public App(string[] args)
        {
            IntPtr h = invoker.Handle;   // create a handle so other threads can post to us
            beacon = new Beacon(settings);
            input = new InputWatcher(settings, delegate
            {
                try { invoker.BeginInvoke(new MethodInvoker(beacon.Trigger)); } catch { }
            });
            input.Start();

            bool selftest = Has(args, "--selftest");
            if (selftest)
            {
                selftestLog = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "CursorBeacon-selftest.log");
                RunSelfTest();
                return;
            }

            tray = new NotifyIcon();
            tray.Text = "Cursor Beacon " + BuildInfo.Version;
            tray.Icon = MakeTrayIcon();
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) beacon.Trigger(); };
            tray.Visible = true;

            if (Has(args, "--updated"))
                tray.ShowBalloonTip(4000, "Cursor Beacon updated", "Now running version " + BuildInfo.Version + ".", ToolTipIcon.None);
            else if (!Has(args, "--startup"))
                tray.ShowBalloonTip(5000, "Cursor Beacon is running",
                    "Shake your mouse or double-tap Ctrl to find your cursor. Settings live in this tray icon.", ToolTipIcon.None);

            // Auto-update: first check shortly after start, then every 3 hours.
            updateTimer.Interval = 30 * 1000;
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 3 * 60 * 60 * 1000;
                Updater.CleanupOld();
                if (settings.AutoUpdate) CheckForUpdates(false);
            };
            updateTimer.Start();

            Native.TrimMemory();
        }

        static bool Has(string[] args, string flag)
        {
            foreach (string a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        Icon MakeTrayIcon()
        {
            int sz = SystemInformation.SmallIconSize.Width;
            using (Bitmap b = Art.RenderIcon(sz, false, settings.Accent)) return Icon.FromHandle(b.GetHicon());
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem find = new ToolStripMenuItem("Find my cursor", null, delegate { beacon.Trigger(); });
            find.Font = new Font(find.Font, FontStyle.Bold);
            menu.Items.Add(find);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(Toggle("Shake mouse to find", settings.Shake, delegate (bool v) { settings.Shake = v; }));
            menu.Items.Add(Toggle("Double-tap Ctrl to find", settings.DoubleCtrl, delegate (bool v) { settings.DoubleCtrl = v; }));
            menu.Items.Add(Toggle("Dim screens around cursor", settings.Dim, delegate (bool v) { settings.Dim = v; }));

            ToolStripMenuItem colors = new ToolStripMenuItem("Color");
            for (int i = 0; i < Settings.ColorNames.Length; i++)
            {
                int idx = i;
                ToolStripMenuItem it = new ToolStripMenuItem(Settings.ColorNames[i], Swatch(Settings.Colors[i]));
                it.Checked = settings.ColorIndex == i;
                it.Click += delegate
                {
                    settings.ColorIndex = idx; settings.Save();
                    foreach (ToolStripMenuItem o in colors.DropDownItems) o.Checked = o == it;
                    Icon old = tray.Icon; tray.Icon = MakeTrayIcon(); if (old != null) old.Dispose();
                    beacon.Trigger();
                };
                colors.DropDownItems.Add(it);
            }
            menu.Items.Add(colors);

            ToolStripMenuItem sens = new ToolStripMenuItem("Shake sensitivity");
            for (int i = 0; i < Settings.SensitivityNames.Length; i++)
            {
                int idx = i;
                ToolStripMenuItem it = new ToolStripMenuItem(Settings.SensitivityNames[i]);
                it.Checked = settings.Sensitivity == i;
                it.Click += delegate
                {
                    settings.Sensitivity = idx; settings.Save();
                    foreach (ToolStripMenuItem o in sens.DropDownItems) o.Checked = o == it;
                };
                sens.DropDownItems.Add(it);
            }
            menu.Items.Add(sens);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows");
            startup.Checked = Settings.GetStartup();
            startup.CheckOnClick = true;
            startup.CheckedChanged += delegate { Settings.SetStartup(startup.Checked); };
            menu.Items.Add(startup);

            menu.Items.Add(Toggle("Install updates automatically", settings.AutoUpdate, delegate (bool v) { settings.AutoUpdate = v; }));
            ToolStripMenuItem check = new ToolStripMenuItem("Check for updates now", null, delegate { CheckForUpdates(true); });
            check.Enabled = Updater.Enabled;
            menu.Items.Add(check);

            if (Program.IsInstalled)
                menu.Items.Add(new ToolStripMenuItem("Uninstall", null, delegate { Uninstall(); }));

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem about = new ToolStripMenuItem("Cursor Beacon " + BuildInfo.Version);
            about.Enabled = false;
            menu.Items.Add(about);
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Quit(); }));
            return menu;
        }

        ToolStripMenuItem Toggle(string text, bool initial, Action<bool> set)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = initial;
            it.CheckOnClick = true;
            it.CheckedChanged += delegate { set(it.Checked); settings.Save(); };
            return it;
        }

        static Bitmap Swatch(Color c)
        {
            Bitmap b = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(c)) g.FillEllipse(br, 2, 2, 12, 12);
            }
            return b;
        }

        void CheckForUpdates(bool manual)
        {
            if (updating || !Updater.Enabled) return;
            updating = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string info;
                Updater.Result res = Updater.TryUpdate(out info);
                try
                {
                    invoker.BeginInvoke(new MethodInvoker(delegate
                    {
                        updating = false;
                        if (res == Updater.Result.Updated)
                        {
                            try { Process.Start(Application.ExecutablePath, "--updated"); } catch { }
                            Quit();
                        }
                        else if (manual)
                        {
                            tray.ShowBalloonTip(4000, "Cursor Beacon",
                                res == Updater.Result.UpToDate ? info : "Update check failed: " + info, ToolTipIcon.None);
                        }
                    }));
                }
                catch { updating = false; }
            });
        }

        void Uninstall()
        {
            if (MessageBox.Show("Remove Cursor Beacon from this PC?", "Cursor Beacon",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Settings.DeleteAll();
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
            Quit();
        }

        void RunSelfTest()
        {
            System.Windows.Forms.Timer t1 = new System.Windows.Forms.Timer();
            t1.Interval = 300;
            int step = 0;
            bool sawActive = false;
            t1.Tick += delegate
            {
                step++;
                if (step == 1) beacon.Trigger();
                if (beacon.Active) sawActive = true;
                if (step >= 10)
                {
                    t1.Stop();
                    bool ok = input.Hooked && sawActive;
                    try
                    {
                        File.WriteAllText(selftestLog, (ok ? "OK" : "FAIL") + " hooks=" + input.Hooked +
                            " beaconShown=" + sawActive + " screens=" + Screen.AllScreens.Length +
                            " version=" + BuildInfo.Version + " repo=" + BuildInfo.Repo);
                    }
                    catch { }
                    ExitCode = ok ? 0 : 1;
                    Quit();
                }
            };
            t1.Start();
        }

        public void Fail(Exception ex)
        {
            if (selftestLog != null)
            {
                try { File.WriteAllText(selftestLog, "FAIL " + ex); } catch { }
                ExitCode = 2;
                Quit();
            }
        }

        void Quit()
        {
            updateTimer.Stop();
            input.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            ExitThread();
        }
    }

    // ------------------------------------------------------------- Startup
    internal static class Program
    {
        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CursorBeacon"); }
        }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "CursorBeacon.exe"); } }

        public static bool IsInstalled
        {
            get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); }
        }

        [STAThread]
        static int Main(string[] args)
        {
            try { if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) Native.SetProcessDPIAware(); }
            catch { try { Native.SetProcessDPIAware(); } catch { } }

            if (args.Length >= 2 && args[0] == "--make-icon") { Art.WriteIco(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--preview") { Art.WritePreview(args[1]); return 0; }

            bool selftest = args.Length > 0 && args[0] == "--selftest";
            bool portable = Array.IndexOf(args, "--portable") >= 0;
            bool updated = Array.IndexOf(args, "--updated") >= 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (Mutex mutex = new Mutex(false, @"Local\CursorBeacon.SingleInstance"))
            {
                bool owned = false;
                if (!selftest)
                {
                    // After a self-update the old copy may still be shutting down: wait for it.
                    try { owned = mutex.WaitOne(updated ? 15000 : 0); }
                    catch (AbandonedMutexException) { owned = true; }
                    if (!owned)
                    {
                        MessageBox.Show("Cursor Beacon is already running. Look for its icon in the system tray.",
                            "Cursor Beacon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }

                    // First run from a download: install to %LOCALAPPDATA%\Programs and start from there.
                    if (!portable && !IsInstalled)
                    {
                        try
                        {
                            Directory.CreateDirectory(InstallDir);
                            File.Copy(Application.ExecutablePath, InstalledExe, true);
                            mutex.ReleaseMutex();
                            owned = false;
                            ProcessStartInfo psi = new ProcessStartInfo(InstalledExe, "--installed --set-startup");
                            psi.UseShellExecute = false;
                            Process.Start(psi);
                            return 0;
                        }
                        catch { /* could not install: just run from where we are */ }
                    }
                    if (Array.IndexOf(args, "--set-startup") >= 0) Settings.SetStartup(true);
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                App app = null;
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { if (app != null) app.Fail(e.Exception); };
                try
                {
                    app = new App(args);
                    Application.Run(app);
                }
                catch (Exception ex)
                {
                    if (selftest)
                    {
                        try { File.WriteAllText(args.Length > 1 ? args[1] : "selftest.log", "FAIL " + ex); } catch { }
                        return 3;
                    }
                    throw;
                }
                finally
                {
                    if (owned) { try { mutex.ReleaseMutex(); } catch { } }
                }
                return app.ExitCode;
            }
        }
    }
}
