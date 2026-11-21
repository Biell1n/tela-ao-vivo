// Janelas: a lista do "o que compartilhar" (igual ao Discord): as telas (monitores) e as janelas abertas,
// com uma miniatura de cada uma e o programa (.exe) dono da janela (pra marcar o som só dele).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TelaAoVivoAudio
{
    static class Janelas
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int n);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int n);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        // retângulo da janela na tela, sem a sombra invisível do Windows 10/11
        public static bool Retangulo(IntPtr h, out RECT r)
        {
            if (DwmGetWindowAttribute(h, 9 /*EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(RECT))) == 0) return true;
            return GetWindowRect(h, out r);
        }
        public static bool Minimizada(IntPtr h) { return IsIconic(h); }

        // a própria janela se desenha (mostra ela mesma se estiver atrás de outra); se vier preta (alguns jogos), usa a tela
        static string MiniaturaJanela(IntPtr h, Rectangle area)
        {
            try
            {
                RECT wr; if (!GetWindowRect(h, out wr)) return Miniatura(area);
                int ww = wr.R - wr.L, wh = wr.B - wr.T;
                if (ww < 8 || wh < 8 || ww > 8000 || wh > 8000) return Miniatura(area);
                using (var bmp = new Bitmap(ww, wh))
                {
                    using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); try { PrintWindow(h, hdc, 2 /*PW_RENDERFULLCONTENT*/); } finally { g.ReleaseHdc(hdc); } }
                    // sem a sombra invisível: recorta pro retângulo visível (área relativa à janela)
                    var rel = new Rectangle(area.X - wr.L, area.Y - wr.T, area.Width, area.Height);
                    rel.Intersect(new Rectangle(0, 0, ww, wh));
                    if (rel.Width < 8 || rel.Height < 8) return Miniatura(area);
                    int claros = 0;
                    for (int k = 1; k <= 24; k++) { var c = bmp.GetPixel(rel.X + rel.Width * (k % 5 + 1) / 7, rel.Y + rel.Height * (k % 6 + 1) / 8); if (c.R + c.G + c.B > 24) claros++; }
                    if (claros < 3) return Miniatura(area); // veio preta
                    using (var recorte = bmp.Clone(rel, bmp.PixelFormat)) return Reduzir(recorte);
                }
            }
            catch { return Miniatura(area); }
        }

        static string Reduzir(Bitmap grande)
        {
            int w = 320, h = Math.Max(1, (int)Math.Round(320.0 * grande.Height / grande.Width));
            if (h > 240) { h = 240; w = Math.Max(1, (int)Math.Round(240.0 * grande.Width / grande.Height)); }
            using (var mini = new Bitmap(w, h))
            using (var g2 = Graphics.FromImage(mini))
            using (var ms = new MemoryStream())
            {
                g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g2.DrawImage(grande, 0, 0, w, h);
                var enc = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.MimeType == "image/jpeg");
                var pars = new EncoderParameters(1); pars.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
                mini.Save(ms, enc, pars);
                return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        static string Miniatura(Rectangle area)
        {
            try
            {
                if (area.Width < 8 || area.Height < 8) return "";
                int w = 320, h = Math.Max(1, (int)Math.Round(320.0 * area.Height / area.Width));
                if (h > 240) { h = 240; w = Math.Max(1, (int)Math.Round(240.0 * area.Width / area.Height)); }
                using (var grande = new Bitmap(area.Width, area.Height))
                using (var g = Graphics.FromImage(grande))
                {
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    using (var mini = new Bitmap(w, h))
                    using (var g2 = Graphics.FromImage(mini))
                    using (var ms = new MemoryStream())
                    {
                        g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g2.DrawImage(grande, 0, 0, w, h);
                        var enc = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.MimeType == "image/jpeg");
                        var pars = new EncoderParameters(1); pars.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
                        mini.Save(ms, enc, pars);
                        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return ""; }
        }

        public static List<Dictionary<string, object>> Telas()
        {
            var r = new List<Dictionary<string, object>>();
            var telas = Screen.AllScreens;
            for (int i = 0; i < telas.Length; i++)
            {
                var b = telas[i].Bounds;
                r.Add(new Dictionary<string, object> { { "id", "t" + i }, { "nome", telas[i].Primary ? "Tela principal" : "Tela " + (i + 1) },
                    { "w", b.Width }, { "h", b.Height }, { "principal", telas[i].Primary }, { "img", Miniatura(b) } });
            }
            return r;
        }

        public static List<Dictionary<string, object>> Lista()
        {
            var r = new List<Dictionary<string, object>>();
            uint eu = (uint)Process.GetCurrentProcess().Id;
            var virt = SystemInformation.VirtualScreen;
            EnumWindows((h, p) =>
            {
                try
                {
                    if (!IsWindowVisible(h) || IsIconic(h)) return true;
                    if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;              // janelinha dona de outra
                    int ex = GetWindowLong(h, -20 /*GWL_EXSTYLE*/);
                    if ((ex & 0x80 /*TOOLWINDOW*/) != 0) return true;
                    int escondida; if (DwmGetWindowAttribute(h, 14 /*CLOAKED*/, out escondida, 4) == 0 && escondida != 0) return true;
                    int n = GetWindowTextLength(h); if (n <= 0) return true;
                    var sb = new StringBuilder(n + 1); GetWindowText(h, sb, sb.Capacity);
                    string titulo = sb.ToString().Trim(); if (titulo.Length == 0 || titulo == "Program Manager") return true;
                    RECT rc; if (!Retangulo(h, out rc)) return true;
                    var area = Rectangle.Intersect(new Rectangle(rc.L, rc.T, rc.R - rc.L, rc.B - rc.T), virt);
                    if (area.Width < 120 || area.Height < 80) return true;
                    uint pid; GetWindowThreadProcessId(h, out pid);
                    if (pid == eu) return true;
                    string exe = "";
                    try { exe = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant() + ".exe"; } catch { }
                    r.Add(new Dictionary<string, object> { { "id", h.ToInt64().ToString() }, { "titulo", titulo }, { "exe", exe },
                        { "w", rc.R - rc.L }, { "h", rc.B - rc.T }, { "img", MiniaturaJanela(h, area) } });
                }
                catch { }
                return r.Count < 40;
            }, IntPtr.Zero);
            return r;
        }
    }
}
