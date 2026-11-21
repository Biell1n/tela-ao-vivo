// Camera: a câmera "por cima da tela" na captura pelo programa (igual ao "estilo OBS" do site).
// Lê a câmera pelo Media Foundation (Source Reader, já convertendo pra BGRA) numa thread própria e guarda o último quadro;
// o Video desenha ele por cima de cada quadro da tela (só no pedaço onde a câmera fica, como o cursor).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace TelaAoVivoAudio
{
    static class Camera
    {
        [DllImport("mfplat.dll")] static extern int MFStartup(uint ver, uint flags);
        [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IntPtr a, uint n);
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr t);
        [DllImport("mf.dll")] static extern int MFEnumDeviceSources(IntPtr attrs, out IntPtr acts, out uint n);
        [DllImport("mfreadwrite.dll")] static extern int MFCreateSourceReaderFromMediaSource(IntPtr src, IntPtr attrs, out IntPtr reader);
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr r, uint mode);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReadSampleD(IntPtr s, uint stream, uint flags, out uint idx, out uint sflags, out long ts, out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NativeTypeD(IntPtr s, uint stream, uint i, out IntPtr t);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetTypeD(IntPtr s, uint stream, IntPtr reserved, IntPtr t);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CurTypeD(IntPtr s, uint stream, out IntPtr t);

        static readonly Guid SourceType = new Guid("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        static readonly Guid VidCap = new Guid("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        static readonly Guid FriendlyName = new Guid("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
        static readonly Guid VideoProcessing = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
        static readonly Guid IMFMediaSource = new Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66");
        static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        static readonly Guid RGB32 = new Guid(0x16, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
        const uint PRIMEIRO_VIDEO = 0xFFFFFFFC;

        // o que o site pediu (posição/tamanho relativos à imagem: x, y = canto de cima à esquerda; w = largura)
        public static volatile bool Ligada, Visivel = true, Circulo, Espelho = true;
        public static float X = 0.73f, Y = 0.66f, W = 0.25f;
        // o último quadro da câmera (BGRA, de cima pra baixo)
        public static byte[] Quadro; public static int QW, QH;
        public static readonly object Trava = new object();
        public static volatile string Erro = "", Nome = "";

        static Thread laco; static volatile bool parar; static int indiceAtual = -1;
        static int mf;

        static IntPtr Dispositivos(out uint n)
        {
            if (Interlocked.Exchange(ref mf, 1) == 0) MFStartup(0x00020070, 0);
            IntPtr attrs; Com.Ok(MFCreateAttributes(out attrs, 1), "MFCreateAttributes");
            try
            {
                Guid k = SourceType, v = VidCap; Com.F<D.SetGuid>(attrs, 24)(attrs, ref k, ref v);
                IntPtr acts; Com.Ok(MFEnumDeviceSources(attrs, out acts, out n), "MFEnumDeviceSources");
                return acts;
            }
            finally { Com.Rel(ref attrs); }
        }

        static string NomeDe(IntPtr act)
        {
            IntPtr s; uint len; Guid k = FriendlyName;
            if (Com.F<D.GetAllocStr>(act, 13)(act, ref k, out s, out len) < 0) return "Câmera";
            var r = Marshal.PtrToStringUni(s); CoTaskMemFree(s); return r;
        }

        public static List<Dictionary<string, object>> Listar()
        {
            var r = new List<Dictionary<string, object>>();
            try
            {
                CoInitializeEx(IntPtr.Zero, 0);
                uint n; IntPtr acts = Dispositivos(out n);
                for (int i = 0; i < n; i++)
                {
                    IntPtr a = Marshal.ReadIntPtr(acts, i * IntPtr.Size);
                    r.Add(new Dictionary<string, object> { { "id", i }, { "nome", NomeDe(a) } });
                    Com.Rel(ref a);
                }
                CoTaskMemFree(acts);
            }
            catch { }
            return r;
        }

        public static void Configurar(bool ligar, int indice)
        {
            if (!ligar) { Parar(); return; }
            if (laco != null && laco.IsAlive && indice == indiceAtual) return;
            Parar();
            indiceAtual = indice; parar = false; Erro = "";
            laco = new Thread(() => Ler(indice)) { IsBackground = true, Name = "camera" };
            laco.SetApartmentState(ApartmentState.MTA);
            laco.Start();
            Ligada = true;
        }

        public static void Parar()
        {
            Ligada = false; parar = true;
            var t = laco; laco = null;
            if (t != null && t.IsAlive) t.Join(2500);
            lock (Trava) Quadro = null;
            indiceAtual = -1;
        }

        static void Ler(int indice)
        {
            IntPtr act = IntPtr.Zero, fonte = IntPtr.Zero, leitor = IntPtr.Zero;
            try
            {
                CoInitializeEx(IntPtr.Zero, 0);
                uint n; IntPtr acts = Dispositivos(out n);
                try
                {
                    if (indice < 0 || indice >= n) throw new Exception("câmera não encontrada");
                    for (int i = 0; i < n; i++) { IntPtr a = Marshal.ReadIntPtr(acts, i * IntPtr.Size); if (i == indice) act = a; else Com.Rel(ref a); }
                }
                finally { CoTaskMemFree(acts); }
                Nome = NomeDe(act);
                Guid iid = IMFMediaSource;
                Com.Ok(Com.F<D.Activate>(act, 33)(act, ref iid, out fonte), "abrir a câmera");
                IntPtr ra; Com.Ok(MFCreateAttributes(out ra, 1), "MFCreateAttributes");
                try { Guid k = VideoProcessing; Com.F<D.SetU32>(ra, 21)(ra, ref k, 1); Com.Ok(MFCreateSourceReaderFromMediaSource(fonte, ra, out leitor), "SourceReader"); }
                finally { Com.Rel(ref ra); }
                EscolherFormato(leitor);
                // saída em BGRA (o próprio Media Foundation converte)
                IntPtr tipo; Com.Ok(MFCreateMediaType(out tipo), "MFCreateMediaType");
                try
                {
                    Guid k = G.MajorType, v = G.Video; Com.F<D.SetGuid>(tipo, 24)(tipo, ref k, ref v);
                    k = G.SubType; Guid sub = RGB32; Com.F<D.SetGuid>(tipo, 24)(tipo, ref k, ref sub);
                    Com.Ok(Marshal.GetDelegateForFunctionPointer<SetTypeD>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(leitor), 7 * IntPtr.Size))(leitor, PRIMEIRO_VIDEO, IntPtr.Zero, tipo), "formato BGRA");
                }
                finally { Com.Rel(ref tipo); }
                IntPtr cur; int w = 0, h = 0, passo = 0;
                if (Marshal.GetDelegateForFunctionPointer<CurTypeD>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(leitor), 6 * IntPtr.Size))(leitor, PRIMEIRO_VIDEO, out cur) >= 0)
                {
                    Guid k = G.FrameSize; ulong tam; Com.F<D.GetU64>(cur, 8)(cur, ref k, out tam); w = (int)(tam >> 32); h = (int)(tam & 0xffffffff);
                    k = DefaultStride; uint st; if (Com.F<D.GetU32>(cur, 7)(cur, ref k, out st) >= 0) passo = (int)st; else passo = -w * 4;
                    Com.Rel(ref cur);
                }
                if (w <= 0 || h <= 0) throw new Exception("a câmera não informou o tamanho");
                var ler = Marshal.GetDelegateForFunctionPointer<ReadSampleD>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(leitor), 9 * IntPtr.Size));
                while (!parar)
                {
                    uint idx, sflags; long ts; IntPtr amostra;
                    int hr = ler(leitor, PRIMEIRO_VIDEO, 0, out idx, out sflags, out ts, out amostra);
                    if (hr < 0) throw new Exception("leitura 0x" + hr.ToString("x8"));
                    if ((sflags & 0x2 /*FIM*/) != 0) break;
                    if (amostra == IntPtr.Zero) continue;
                    try
                    {
                        IntPtr buf; if (Com.F<D.Ptr>(amostra, 41)(amostra, out buf) < 0) continue;
                        try
                        {
                            IntPtr p; uint max, tamAtual;
                            if (Com.F<D.Lock>(buf, 3)(buf, out p, out max, out tamAtual) < 0) continue;
                            try
                            {
                                var q = new byte[w * h * 4];
                                int linha = w * 4, ap = Math.Abs(passo) < linha ? linha : Math.Abs(passo);
                                for (int y = 0; y < h; y++)
                                {   // passo negativo = a imagem vem de baixo pra cima
                                    int origem = passo < 0 ? (h - 1 - y) * ap : y * ap;
                                    if (origem + linha > tamAtual) break;
                                    Marshal.Copy(p + origem, q, y * linha, linha);
                                }
                                lock (Trava) { Quadro = q; QW = w; QH = h; }
                            }
                            finally { Com.F<D.Int0>(buf, 4)(buf); }
                        }
                        finally { Com.Rel(ref buf); }
                    }
                    finally { Com.Rel(ref amostra); }
                }
            }
            catch (Exception e) { Erro = e.Message; }
            finally
            {
                Com.Rel(ref leitor);
                if (act != IntPtr.Zero) { try { Com.F<D.Int0>(act, 34)(act); } catch { } } // ShutdownObject: solta a câmera
                Com.Rel(ref fonte); Com.Rel(ref act);
                lock (Trava) Quadro = null;
            }
        }

        // formato nativo: o maior até 1280x720 com 25 fps ou mais (a câmera fica pequena na tela: não precisa de mais)
        static void EscolherFormato(IntPtr leitor)
        {
            var nativo = Marshal.GetDelegateForFunctionPointer<NativeTypeD>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(leitor), 5 * IntPtr.Size));
            var setar = Marshal.GetDelegateForFunctionPointer<SetTypeD>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(leitor), 7 * IntPtr.Size));
            IntPtr melhor = IntPtr.Zero; long pontos = -1;
            for (uint i = 0; i < 200; i++)
            {
                IntPtr t; if (nativo(leitor, PRIMEIRO_VIDEO, i, out t) < 0) break;
                Guid k = G.FrameSize; ulong tam; Com.F<D.GetU64>(t, 8)(t, ref k, out tam);
                k = G.FrameRate; ulong fr; Com.F<D.GetU64>(t, 8)(t, ref k, out fr);
                long w = (long)(tam >> 32), h = (long)(tam & 0xffffffff);
                double fps = (fr & 0xffffffff) == 0 ? 0 : (double)(fr >> 32) / (fr & 0xffffffff);
                long p = (w <= 1280 && h <= 720 ? w * h : 0) * (fps >= 24 ? 2 : 1);
                if (p > pontos) { Com.Rel(ref melhor); melhor = t; pontos = p; } else Com.Rel(ref t);
            }
            if (melhor != IntPtr.Zero) { setar(leitor, PRIMEIRO_VIDEO, IntPtr.Zero, melhor); Com.Rel(ref melhor); }
        }
    }
}
