// Áudio do Windows: lista quem está tocando som e captura o som de um app (ou de todos menos um).
// Usa a "captura por processo" do Windows 10 2004+ (a mesma que o Discord e o OBS usam).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TelaAoVivoAudio
{
    // ------------------------------------------------------------------ interfaces COM do Windows
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
        [PreserveSig] int GetIconPath(out IntPtr p);
        [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr a);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr a);
        [PreserveSig] int GetSessionIdentifier(out IntPtr s);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr s);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long def, out long min);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op);
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    class Ativador : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEvent Pronto = new ManualResetEvent(false);
        public int Hr = -1;
        public object Cliente;
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op)
        {
            int hr; object o;
            op.GetActivateResult(out hr, out o);
            Hr = hr; Cliente = o;
            Pronto.Set();
            return 0;
        }
    }

    static class Nativo
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true)]
        public static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        public static string CaminhoDoProcesso(uint pid)
        {
            IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    // ------------------------------------------------------------------ fila circular de bytes (PCM)
    class Anel
    {
        readonly byte[] buf; int ini, qtd; readonly object trava = new object();
        public bool Pronto; // já juntou o mínimo pra começar a tocar sem picotar
        public Anel(int capacidade) { buf = new byte[capacidade]; }
        public int Disponivel { get { lock (trava) return qtd; } }
        public void Escrever(byte[] src, int n)
        {
            lock (trava)
            {
                if (n > buf.Length) { Array.Copy(src, n - buf.Length, src, 0, buf.Length); n = buf.Length; }
                int sobra = buf.Length - qtd;
                if (n > sobra) { int fora = n - sobra; ini = (ini + fora) % buf.Length; qtd -= fora; }
                int fim = (ini + qtd) % buf.Length;
                int a = Math.Min(n, buf.Length - fim);
                Array.Copy(src, 0, buf, fim, a);
                if (n > a) Array.Copy(src, a, buf, 0, n - a);
                qtd += n;
            }
        }
        public int Ler(byte[] dst, int n)
        {
            lock (trava)
            {
                n = Math.Min(n, qtd);
                int a = Math.Min(n, buf.Length - ini);
                Array.Copy(buf, ini, dst, 0, a);
                if (n > a) Array.Copy(buf, 0, dst, a, n - a);
                ini = (ini + n) % buf.Length; qtd -= n;
                return n;
            }
        }
        public void Descartar(int n)
        {
            lock (trava) { n = Math.Min(n, qtd); n -= n % 4; ini = (ini + n) % buf.Length; qtd -= n; }
        }
    }

    // ------------------------------------------------------------------ captura de um processo (ou de todos menos ele)
    class Captura
    {
        public const int Taxa = 48000, BytesPorQuadro = 4; // 48 kHz, estéreo, 16 bits
        public readonly uint Pid;
        public readonly bool Excluir;
        public readonly Anel Anel = new Anel(Taxa * BytesPorQuadro); // 1 s
        public volatile string Erro;
        public volatile bool Rodando;
        volatile bool parar;
        Thread fio;

        public Captura(uint pid, bool excluir) { Pid = pid; Excluir = excluir; }
        public string Chave { get { return (Excluir ? "x" : "i") + Pid; } }

        public void Iniciar()
        {
            fio = new Thread(Rodar) { IsBackground = true, Name = "captura " + Chave, Priority = ThreadPriority.AboveNormal };
            fio.SetApartmentState(ApartmentState.MTA);
            fio.Start();
        }
        public void Parar() { parar = true; }

        void Rodar()
        {
            IntPtr pv = Marshal.AllocHGlobal(24), prm = Marshal.AllocHGlobal(12), fmt = Marshal.AllocHGlobal(18);
            IAudioClient cli = null; IAudioCaptureClient cap = null;
            try
            {
                // AUDIOCLIENT_ACTIVATION_PARAMS { tipo = PROCESS_LOOPBACK, { pid, modo } }
                Marshal.WriteInt32(prm, 0, 1);
                Marshal.WriteInt32(prm, 4, (int)Pid);
                Marshal.WriteInt32(prm, 8, Excluir ? 1 : 0);
                // PROPVARIANT do tipo VT_BLOB apontando pros parâmetros
                for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(pv, i, 0);
                Marshal.WriteInt16(pv, 0, 65);
                Marshal.WriteInt32(pv, 8, 12);
                Marshal.WriteIntPtr(pv, 16, prm);
                // WAVEFORMATEX: PCM 48 kHz estéreo 16 bits
                Marshal.WriteInt16(fmt, 0, 1); Marshal.WriteInt16(fmt, 2, 2);
                Marshal.WriteInt32(fmt, 4, Taxa); Marshal.WriteInt32(fmt, 8, Taxa * BytesPorQuadro);
                Marshal.WriteInt16(fmt, 12, BytesPorQuadro); Marshal.WriteInt16(fmt, 14, 16); Marshal.WriteInt16(fmt, 16, 0);

                var at = new Ativador();
                IActivateAudioInterfaceAsyncOperation op;
                int hr = Nativo.ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, pv, at, out op);
                if (hr != 0) throw new Exception("ativar 0x" + hr.ToString("X8"));
                if (!at.Pronto.WaitOne(5000)) throw new Exception("o Windows não respondeu");
                if (at.Hr != 0) throw new Exception("ativar 0x" + at.Hr.ToString("X8"));
                cli = (IAudioClient)at.Cliente;

                const uint LOOPBACK = 0x00020000, EVENTO = 0x00040000, CONVERTE = 0x80000000, QUALIDADE = 0x08000000;
                hr = cli.Initialize(0, LOOPBACK | EVENTO | CONVERTE | QUALIDADE, 200000, 0, fmt, IntPtr.Zero);
                if (hr < 0) hr = cli.Initialize(0, LOOPBACK | EVENTO, 200000, 0, fmt, IntPtr.Zero);
                if (hr < 0) throw new Exception("iniciar 0x" + hr.ToString("X8"));

                var ev = new AutoResetEvent(false);
                cli.SetEventHandle(ev.SafeWaitHandle.DangerousGetHandle());
                Guid iid = typeof(IAudioCaptureClient).GUID; object so;
                hr = cli.GetService(ref iid, out so);
                if (hr < 0) throw new Exception("serviço 0x" + hr.ToString("X8"));
                cap = (IAudioCaptureClient)so;
                hr = cli.Start();
                if (hr < 0) throw new Exception("começar 0x" + hr.ToString("X8"));
                Rodando = true;

                var tmp = new byte[Taxa * BytesPorQuadro];
                while (!parar)
                {
                    ev.WaitOne(50);
                    uint n;
                    while (!parar && cap.GetNextPacketSize(out n) == 0 && n > 0)
                    {
                        IntPtr dados; uint quadros, flags; ulong p1, p2;
                        if (cap.GetBuffer(out dados, out quadros, out flags, out p1, out p2) != 0) break;
                        int bytes = (int)Math.Min(quadros * BytesPorQuadro, (uint)tmp.Length);
                        if ((flags & 2) != 0) Array.Clear(tmp, 0, bytes); // pacote marcado como silêncio
                        else Marshal.Copy(dados, tmp, 0, bytes);
                        cap.ReleaseBuffer(quadros);
                        Anel.Escrever(tmp, bytes);
                    }
                }
                cli.Stop();
            }
            catch (Exception e) { Erro = e.Message; }
            finally
            {
                Rodando = false;
                if (cap != null) Marshal.ReleaseComObject(cap);
                if (cli != null) Marshal.ReleaseComObject(cli);
                Marshal.FreeHGlobal(pv); Marshal.FreeHGlobal(prm); Marshal.FreeHGlobal(fmt);
            }
        }
    }

    // ------------------------------------------------------------------ quem está tocando som agora
    class AppComSom
    {
        public string Exe, Nome, Caminho;
        public float Pico;
        public bool Tocando;
    }

    static class Sessoes
    {
        static readonly Dictionary<string, string> nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> icones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public struct Proc { public uint Pai; public string Exe; }

        public static Dictionary<uint, Proc> Processos()
        {
            var r = new Dictionary<uint, Proc>();
            IntPtr snap = Nativo.CreateToolhelp32Snapshot(2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return r;
            try
            {
                var e = new Nativo.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(Nativo.PROCESSENTRY32W)) };
                if (Nativo.Process32FirstW(snap, ref e))
                    do { r[e.th32ProcessID] = new Proc { Pai = e.th32ParentProcessID, Exe = (e.szExeFile ?? "").ToLowerInvariant() }; }
                    while (Nativo.Process32NextW(snap, ref e));
            }
            finally { Nativo.CloseHandle(snap); }
            return r;
        }

        // o processo "raiz" do app: sobe enquanto o pai for o mesmo programa (Chrome, Discord têm vários processos)
        public static uint Raiz(uint pid, Dictionary<uint, Proc> procs)
        {
            Proc p;
            if (!procs.TryGetValue(pid, out p)) return pid;
            var vistos = new HashSet<uint> { pid };
            while (true)
            {
                Proc pai;
                if (!procs.TryGetValue(p.Pai, out pai) || pai.Exe != p.Exe || !vistos.Add(p.Pai)) return pid;
                pid = p.Pai; p = pai;
            }
        }

        public static List<uint> Raizes(string exe, Dictionary<uint, Proc> procs)
        {
            var r = new List<uint>();
            foreach (var kv in procs)
            {
                if (kv.Value.Exe != exe) continue;
                Proc pai;
                if (procs.TryGetValue(kv.Value.Pai, out pai) && pai.Exe == exe) continue;
                r.Add(kv.Key);
            }
            return r;
        }

        // lista os apps com sessão de som em qualquer saída ativa (fone, caixa, cabo virtual...)
        public static List<AppComSom> Listar(Dictionary<uint, Proc> procs)
        {
            var porExe = new Dictionary<string, AppComSom>(StringComparer.OrdinalIgnoreCase);
            uint eu = (uint)Process.GetCurrentProcess().Id;
            object oe = null; IMMDeviceCollection col = null;
            try
            {
                oe = new MMDeviceEnumeratorCom();
                var en = (IMMDeviceEnumerator)oe;
                if (en.EnumAudioEndpoints(0 /*render*/, 1 /*ativos*/, out col) != 0) return new List<AppComSom>();
                uint nd; col.GetCount(out nd);
                for (uint d = 0; d < nd; d++)
                {
                    IMMDevice dev = null; object om = null; IAudioSessionEnumerator se = null;
                    try
                    {
                        if (col.Item(d, out dev) != 0) continue;
                        Guid iid = typeof(IAudioSessionManager2).GUID;
                        if (dev.Activate(ref iid, 23, IntPtr.Zero, out om) != 0) continue;
                        if (((IAudioSessionManager2)om).GetSessionEnumerator(out se) != 0) continue;
                        int ns; se.GetCount(out ns);
                        for (int i = 0; i < ns; i++)
                        {
                            IAudioSessionControl2 c = null;
                            try
                            {
                                if (se.GetSession(i, out c) != 0 || c == null) continue;
                                if (c.IsSystemSoundsSession() == 0) continue;
                                uint pid; c.GetProcessId(out pid);
                                if (pid == 0 || pid == eu) continue;
                                int estado; c.GetState(out estado);
                                if (estado == 2) continue; // sessão encerrada
                                float pico = 0;
                                var m = c as IAudioMeterInformation;
                                if (m != null) m.GetPeakValue(out pico);

                                uint raiz = Raiz(pid, procs);
                                string caminho = Nativo.CaminhoDoProcesso(raiz) ?? Nativo.CaminhoDoProcesso(pid);
                                Proc pr; string exe = procs.TryGetValue(pid, out pr) ? pr.Exe : (caminho != null ? Path.GetFileName(caminho).ToLowerInvariant() : null);
                                if (string.IsNullOrEmpty(exe)) continue;
                                AppComSom a;
                                if (!porExe.TryGetValue(exe, out a)) porExe[exe] = a = new AppComSom { Exe = exe, Caminho = caminho, Nome = NomeAmigavel(exe, caminho) };
                                a.Pico = Math.Max(a.Pico, pico);
                                a.Tocando |= estado == 1;
                            }
                            finally { if (c != null) Marshal.ReleaseComObject(c); }
                        }
                    }
                    finally
                    {
                        if (se != null) Marshal.ReleaseComObject(se);
                        if (om != null) Marshal.ReleaseComObject(om);
                        if (dev != null) Marshal.ReleaseComObject(dev);
                    }
                }
            }
            catch { }
            finally
            {
                if (col != null) Marshal.ReleaseComObject(col);
                if (oe != null) Marshal.ReleaseComObject(oe);
            }
            var lista = new List<AppComSom>(porExe.Values);
            lista.Sort((x, y) => string.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase));
            return lista;
        }

        static string NomeAmigavel(string exe, string caminho)
        {
            string n;
            if (caminho != null && nomes.TryGetValue(caminho, out n)) return n;
            n = null;
            try
            {
                if (caminho != null)
                {
                    var v = FileVersionInfo.GetVersionInfo(caminho);
                    n = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription.Trim() : (v.ProductName ?? "").Trim();
                }
            }
            catch { }
            if (string.IsNullOrEmpty(n)) n = Path.GetFileNameWithoutExtension(exe);
            if (caminho != null) nomes[caminho] = n;
            return n;
        }

        public static string Icone(string caminho)
        {
            if (caminho == null) return null;
            string r;
            if (icones.TryGetValue(caminho, out r)) return r;
            r = null;
            try
            {
                using (var ic = Icon.ExtractAssociatedIcon(caminho))
                using (var bmp = new Bitmap(32, 32))
                using (var g = Graphics.FromImage(bmp))
                using (var ms = new MemoryStream())
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawIcon(ic, new Rectangle(0, 0, 32, 32));
                    bmp.Save(ms, ImageFormat.Png);
                    r = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { }
            icones[caminho] = r;
            return r;
        }
    }
}
