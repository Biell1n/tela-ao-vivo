// Video: captura a tela e comprime NA PLACA DE VÍDEO, como o Discord/OBS. A imagem não sai da placa em nenhum
// momento (o navegador traz cada quadro pro processador e devolve, e com o jogo pesando perde ~40% dos quadros).
//   1) DXGI Desktop Duplication: pega o quadro que o Windows montou (textura na placa)
//   2) Video Processor MFT: converte BGRA -> NV12 (e redimensiona) na placa
//   3) Encoder H.264 de hardware do Media Foundation (NVENC / AMF / QuickSync), assíncrono
// Sai H.264 "Annex B" (com SPS/PPS nos quadros-chave), sem quadros B, baixa latência: pronto pro WebRTC.
// Roda com prioridade alta na placa enquanto transmite: a transmissão pega a parte dela e o jogo cede um pouco.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TelaAoVivoAudio
{
    // chamadas COM pela tabela de funções (sem bibliotecas: o programa continua um .exe só)
    static class Com
    {
        static class Cache<T> where T : class { public static readonly Dictionary<IntPtr, T> D = new Dictionary<IntPtr, T>(); }
        public static T F<T>(IntPtr obj, int slot) where T : class
        {
            if (obj == IntPtr.Zero) throw new Exception("objeto nulo (" + typeof(T).Name + ")");
            IntPtr fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);
            lock (Cache<T>.D)
            {
                T d;
                if (!Cache<T>.D.TryGetValue(fn, out d)) { d = Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T; Cache<T>.D[fn] = d; }
                return d;
            }
        }
        public static void Rel(ref IntPtr p) { if (p != IntPtr.Zero) { Marshal.Release(p); p = IntPtr.Zero; } }
        public static void Ok(int hr, string onde) { if (hr < 0) throw new Exception(onde + " 0x" + hr.ToString("x8")); }
        public static IntPtr QI(IntPtr obj, Guid iid) { IntPtr o; return F<D.QI>(obj, 0)(obj, ref iid, out o) >= 0 ? o : IntPtr.Zero; }
    }

    static class D
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int QI(IntPtr s, ref Guid g, out IntPtr o);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Ptr(IntPtr s, out IntPtr o);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int IdxPtr(IntPtr s, uint i, out IntPtr o);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DupOut(IntPtr s, IntPtr dev, out IntPtr dup);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Acquire(IntPtr s, uint ms, IntPtr info, out IntPtr res);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Int0(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void VoidPtr(IntPtr s, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateTex(IntPtr s, IntPtr desc, IntPtr init, out IntPtr tex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void Copy(IntPtr s, IntPtr dst, IntPtr src);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetU32(IntPtr s, ref Guid k, uint v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetU64(IntPtr s, ref Guid k, ulong v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetGuid(IntPtr s, ref Guid k, ref Guid v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetU32(IntPtr s, ref Guid k, out uint v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetGuid(IntPtr s, ref Guid k, out Guid v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetU64(IntPtr s, ref Guid k, out ulong v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetBlobSize(IntPtr s, ref Guid k, out uint n);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetBlob(IntPtr s, ref Guid k, [Out] byte[] b, uint n, out uint got);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetAllocStr(IntPtr s, ref Guid k, out IntPtr str, out uint n);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetType(IntPtr s, uint stream, IntPtr type, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int AvailType(IntPtr s, uint stream, uint idx, out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CurType(IntPtr s, uint stream, out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int StreamInfo(IntPtr s, uint stream, IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ProcMsg(IntPtr s, uint msg, IntPtr param);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ProcIn(IntPtr s, uint stream, IntPtr sample, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ProcOut(IntPtr s, uint flags, uint n, IntPtr bufs, out uint status);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetEvent(IntPtr s, uint flags, out IntPtr ev);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetU32Out(IntPtr s, out uint v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetI32Out(IntPtr s, out int v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetI64(IntPtr s, long v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetI64(IntPtr s, out long v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Lock(IntPtr s, out IntPtr p, out uint max, out uint cur);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Activate(IntPtr s, ref Guid iid, out IntPtr o);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Reset(IntPtr s, IntPtr dev, uint token);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CodecSet(IntPtr s, ref Guid api, IntPtr v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetMT(IntPtr s, int on);
    }

    static class G // GUIDs do Media Foundation / Direct3D
    {
        static Guid Fmt(uint fourcc) { return new Guid(fourcc, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71); }
        public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid H264 = Fmt(0x34363248), NV12 = Fmt(0x3231564E), ARGB32 = Fmt(0x15);
        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid AvgBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid Interlace = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid PixelAspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid Profile = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid SeqHeader = new Guid("3c036de7-3ad0-4c9e-9216-ee6d6ac21cb3");
        public static readonly Guid YuvMatrix = new Guid("3e23d450-2c75-4d25-a00e-b91670d12327");
        public static readonly Guid NominalRange = new Guid("c21b8ee5-b956-4071-8daf-325edf5cab11");
        public static readonly Guid Primaries = new Guid("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
        public static readonly Guid Transfer = new Guid("5fb0fce9-be5c-4935-a811-ec838f8eed93");
        public static readonly Guid AsyncUnlock = new Guid("e5666d6b-3422-4eb6-a421-da7db1f8e207");
        public static readonly Guid D3D11Aware = new Guid("206b4fc8-fcf9-4c51-afe3-9764369e33a0");
        public static readonly Guid FriendlyName = new Guid("314ffbae-5b41-4c95-9c19-4e7d586face3");
        public static readonly Guid CleanPoint = new Guid("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");
        public static readonly Guid DisableFrc = new Guid("2c0afa19-7a97-4d5a-9ee8-16d4fc518d8c"); // MF_XVP_DISABLE_FRC
        public static readonly Guid BindFlags = new Guid("eacf97ad-065c-4408-bee3-fdcbfd128be2"); // MF_SA_D3D11_BINDFLAGS
        public static readonly Guid MinSampleCount = new Guid("851745d5-c3d6-476d-9527-498ef2d10d18"); // MF_SA_MINIMUM_OUTPUT_SAMPLE_COUNT
        public static readonly Guid CatEncoder = new Guid("f79eac7d-e545-4387-bdee-d647d7bde42a");
        public static readonly Guid ClsidVideoProcessor = new Guid("88753b26-5b24-49bd-b2e7-0c445c78c982");
        public static readonly Guid IMFTransform = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
        public static readonly Guid IMFMediaEventGenerator = new Guid("2cd0bd52-bcd5-4b89-b62c-eadc0c031e7d");
        public static readonly Guid IMFShutdown = new Guid("97ec2ea4-0e42-4937-97ac-9d6d328824e1");
        public static readonly Guid ICodecAPI = new Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
        public static readonly Guid ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        public static readonly Guid ID3D10Multithread = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");
        public static readonly Guid IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        public static readonly Guid LowLatency = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");
        public static readonly Guid RateControl = new Guid("1c0608e9-370c-4710-8a58-cb6181c42423");
        public static readonly Guid MeanBitRate = new Guid("f7222374-2144-4815-b550-a37f8e12ee52");
        public static readonly Guid GopSize = new Guid("95f31b26-95a4-41aa-9303-246a7fc6eef1");
        public static readonly Guid BFrames = new Guid("8d390aac-dc5c-4200-b57f-814d04babab2");
        public static readonly Guid ForceKey = new Guid("398c1b98-8353-475a-9ef2-8f265d260345");
    }

    class Video
    {
        [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
            IntPtr featureLevels, uint nLevels, uint sdk, out IntPtr device, out int level, out IntPtr ctx);
        [DllImport("mfplat.dll")] static extern int MFStartup(uint ver, uint flags);
        [DllImport("mfplat.dll")] static extern int MFCreateDXGIDeviceManager(out uint token, out IntPtr mgr);
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr t);
        [DllImport("mfplat.dll")] static extern int MFCreateSample(out IntPtr s);
        [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(uint max, out IntPtr b);
        [DllImport("mfplat.dll")] static extern int MFCreateDXGISurfaceBuffer(ref Guid riid, IntPtr surface, uint sub, int bottomUp, out IntPtr buf);
        [DllImport("mfplat.dll")] static extern int MFTEnumEx(Guid cat, uint flags, IntPtr inInfo, IntPtr outInfo, out IntPtr acts, out uint n);
        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr o);
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr r, uint mode);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);
        [DllImport("gdi32.dll")] static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr proc, int cls);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

        const int DXGI_TIMEOUT = unchecked((int)0x887A0027), DXGI_ACCESS_LOST = unchecked((int)0x887A0026);
        const int MF_NEED_INPUT = unchecked((int)0xC00D6D72), MF_STREAM_CHANGE = unchecked((int)0xC00D6D61);
        const uint EV_NEED_INPUT = 601, EV_HAVE_OUTPUT = 602;
        static int mfIniciado;

        // o que o site pediu
        public volatile int Fps = 60, Kbps = 12000, Altura = 1080;
        // "fluida" = a transmissão pega a parte dela na placa (prioridade alta; o jogo cede um pouco)
        // "jogo"   = prioridade normal: se a placa apertar, quem perde fps é a transmissão
        public volatile bool Fluida = true;
        // fluida: tenta a máxima (tempo real, como o OBS como administrador); sem permissão, fica na alta
        public void AplicarPrioridade()
        {
            if (!rodando) return;
            if (!Fluida) { D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(), 2); ClasseAtual = 2; return; }
            ClasseAtual = D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(), 5) == 0 ? 5 : 4;
            if (ClasseAtual == 4) D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(), 4);
        }
        public volatile int ClasseAtual;
        public Action<byte[], bool, long> AoQuadro;  // (H.264 Annex B, é quadro-chave, tempo em microssegundos)
        public Action<string> AoAviso;               // texto pro site/janela
        public volatile string Encoder = "", Erro = "";
        void Falha(string e) { if (string.IsNullOrEmpty(Erro)) Erro = e; }
        public volatile int Largura, AlturaSaida;
        public volatile int Capturados, Enviados;   // por segundo (pra estatística)
        public volatile int VpFlags;
        public int nPede, nSaida, nEntrega, nRecusa, nEventoOutro, nVpOk, nVpFalha, nVpRecusa, nVpIncompleto; public volatile string UltimoHr = "", UltimoVp = "";
        public string Dbg { get { return "pede " + nPede + " saída " + nSaida + " entregas " + nEntrega + " recusas " + nRecusa + " outros " + nEventoOutro + " hr " + UltimoHr + " | vp ok " + nVpOk + " falha " + nVpFalha + " recusa " + nVpRecusa + " incompleto " + nVpIncompleto + " " + UltimoVp + " | " + Tamanhos + " | caminho " + Caminho + " | aceita " + EntradasAceitas + " | " + Tempos + " | prioridade " + ClasseAtual; } }

        Thread laco; volatile bool rodando; volatile bool pedirChave; volatile int novaTaxa;
        IntPtr dev, ctx, mgr; uint token;
        IntPtr dup; int telaW, telaH;
        IntPtr copia;                       // nossa cópia do quadro (BGRA, na placa)
        IntPtr vp; bool vpDaSamples; IntPtr[] poolNv12 = new IntPtr[0]; int poolIdx;
        IntPtr enc, encGen, codec; bool encDaSamples; uint encBufTam;
        byte[] cabecalho = new byte[0];     // SPS/PPS
        readonly object trava = new object(); int precisaEntrada; IntPtr pendente; // amostra NV12 esperando o encoder pedir
        readonly Stopwatch relogio = Stopwatch.StartNew();
        int contCap, contEnv; long ultSeg;

        public void PedirChave() { pedirChave = true; }
        public void MudarTaxa(int kbps) { if (kbps > 200) { Kbps = kbps; novaTaxa = kbps; } }

        public void Iniciar()
        {
            if (rodando) return;
            rodando = true; Erro = "";
            laco = new Thread(Rodar) { IsBackground = true, Name = "video", Priority = ThreadPriority.Highest };
            laco.SetApartmentState(ApartmentState.MTA);
            laco.Start();
        }

        public void Parar()
        {
            rodando = false;
            var t = laco; if (t != null && t != Thread.CurrentThread) t.Join(3000);
        }

        void Rodar()
        {
            try
            {
                CoInitializeEx(IntPtr.Zero, 0);
                if (Interlocked.Exchange(ref mfIniciado, 1) == 0) Com.Ok(MFStartup(0x00020070, 0), "MFStartup");
                CriarPlaca();
                AplicarPrioridade();
                CriarCaptura();
                MontarCompressao();
                Capturar();
            }
            catch (Exception e) { Erro = e.Message; if (AoAviso != null) AoAviso("erro: " + e.Message); }
            finally { Desmontar(); D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(), 2); rodando = false; }
        }

        // ------------------------------------------------------------------ placa de vídeo
        void CriarPlaca()
        {
            int lvl;
            // BGRA + VIDEO_SUPPORT (o conversor e o encoder usam o mesmo dispositivo)
            Com.Ok(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20 | 0x800, IntPtr.Zero, 0, 7, out dev, out lvl, out ctx), "D3D11CreateDevice");
            var mt = Com.QI(dev, G.ID3D10Multithread);
            if (mt != IntPtr.Zero) { Com.F<D.SetMT>(mt, 5)(mt, 1); Com.Rel(ref mt); } // várias threads no mesmo dispositivo
            Com.Ok(MFCreateDXGIDeviceManager(out token, out mgr), "MFCreateDXGIDeviceManager");
            Com.Ok(Com.F<D.Reset>(mgr, 7)(mgr, dev, token), "ResetDevice");
        }

        void CriarCaptura()
        {
            Com.Rel(ref dup);
            var dxdev = Com.QI(dev, G.IDXGIDevice);
            IntPtr adapter, output;
            try
            {
                Com.Ok(Com.F<D.Ptr>(dxdev, 7)(dxdev, out adapter), "GetAdapter");
                try
                {
                    Com.Ok(Com.F<D.IdxPtr>(adapter, 7)(adapter, 0, out output), "EnumOutputs");
                    try { Com.Ok(Com.F<D.DupOut>(output, 22)(output, dev, out dup), "DuplicateOutput"); } // IDXGIOutput1::DuplicateOutput
                    finally { Com.Rel(ref output); }
                }
                finally { Com.Rel(ref adapter); }
            }
            finally { Com.Rel(ref dxdev); }
            var desc = Marshal.AllocHGlobal(64);
            try { Com.F<D.VoidPtr>(dup, 7)(dup, desc); telaW = Marshal.ReadInt32(desc, 0); telaH = Marshal.ReadInt32(desc, 4); }
            finally { Marshal.FreeHGlobal(desc); }
        }

        IntPtr CriarTextura(int w, int h, int formato, int bind)
        {
            var d = Marshal.AllocHGlobal(44);
            try
            {
                int[] v = { w, h, 1, 1, formato, 1, 0, 0 /*DEFAULT*/, bind, 0, 0 };
                Marshal.Copy(v, 0, d, 11);
                IntPtr t; Com.Ok(Com.F<D.CreateTex>(dev, 5)(dev, d, IntPtr.Zero, out t), "CreateTexture2D");
                return t;
            }
            finally { Marshal.FreeHGlobal(d); }
        }

        // ------------------------------------------------------------------ conversor (BGRA -> NV12) e encoder
        IntPtr TipoVideo(Guid sub, int w, int h, int fps)
        {
            IntPtr t; Com.Ok(MFCreateMediaType(out t), "MFCreateMediaType");
            Guid k = G.MajorType, v = G.Video; Com.F<D.SetGuid>(t, 24)(t, ref k, ref v);
            k = G.SubType; Com.F<D.SetGuid>(t, 24)(t, ref k, ref sub);
            k = G.FrameSize; Com.F<D.SetU64>(t, 22)(t, ref k, ((ulong)(uint)w << 32) | (uint)h);
            k = G.FrameRate; Com.F<D.SetU64>(t, 22)(t, ref k, ((ulong)(uint)fps << 32) | 1u);
            k = G.PixelAspect; Com.F<D.SetU64>(t, 22)(t, ref k, (1ul << 32) | 1u);
            k = G.Interlace; Com.F<D.SetU32>(t, 21)(t, ref k, 2); // progressivo
            return t;
        }
        static void Cor709(IntPtr t, bool limitado)
        {
            Guid k = G.YuvMatrix; Com.F<D.SetU32>(t, 21)(t, ref k, 1);        // BT.709
            k = G.Primaries; Com.F<D.SetU32>(t, 21)(t, ref k, 2);             // BT.709
            k = G.Transfer; Com.F<D.SetU32>(t, 21)(t, ref k, 5);              // BT.709
            k = G.NominalRange; Com.F<D.SetU32>(t, 21)(t, ref k, limitado ? 2u : 1u); // 16-235 (vídeo) / 0-255
        }

        void MontarCompressao()
        {
            int fps = Math.Max(10, Math.Min(240, Fps));
            int alt = Altura > 0 ? Math.Min(Altura, telaH) : telaH;
            int larg = (int)Math.Round(telaW * (double)alt / telaH);
            larg &= ~1; alt &= ~1;
            Largura = larg; AlturaSaida = alt;

            if (larg == (telaW & ~1) && alt == (telaH & ~1) && Environment.GetEnvironmentVariable("TVA_DIRETO") == "1")
            {
                try
                {
                    MontarEncoder(larg, alt, fps, true);
                    encDireto = true;
                    poolBgra = new IntPtr[8];
                    for (int i = 0; i < poolBgra.Length; i++) poolBgra[i] = CriarTextura(telaW, telaH, 87 /*B8G8R8A8*/, 0x20 /*RENDER_TARGET*/);
                    return;
                }
                catch (Exception e) { Caminho = "conversor (direto falhou: " + e.Message + ")"; Com.Rel(ref codec); Com.Rel(ref encGen); Com.Rel(ref enc); }
            }
            encDireto = false;

            // conversor de cor/tamanho do Windows, na placa
            Guid clsid = G.ClsidVideoProcessor, iid = G.IMFTransform;
            Com.Ok(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out vp), "VideoProcessorMFT");
            Com.Ok(Com.F<D.ProcMsg>(vp, 23)(vp, 2 /*SET_D3D_MANAGER*/, mgr), "VP d3d");
            IntPtr vattr; // sem "conversão de taxa": um quadro entra, um quadro sai (senão ele inventa cópias)
            if (Com.F<D.Ptr>(vp, 8)(vp, out vattr) >= 0 && vattr != IntPtr.Zero) { Guid k = G.DisableFrc; Com.F<D.SetU32>(vattr, 21)(vattr, ref k, 1); Com.Rel(ref vattr); }
            var ti = TipoVideo(G.ARGB32, telaW, telaH, fps); Cor709(ti, false);
            var to = TipoVideo(G.NV12, larg, alt, fps); Cor709(to, true);
            try
            {
                Com.Ok(Com.F<D.SetType>(vp, 15)(vp, 0, ti, 0), "VP entrada");
                Com.Ok(Com.F<D.SetType>(vp, 16)(vp, 0, to, 0), "VP saída");
            }
            finally { Com.Rel(ref ti); Com.Rel(ref to); }
            var info = Marshal.AllocHGlobal(16);
            try { Com.Ok(Com.F<D.StreamInfo>(vp, 7)(vp, 0, info), "VP info"); VpFlags = Marshal.ReadInt32(info, 0); vpDaSamples = (VpFlags & 0x100) != 0; } // se ele PODE dar, a gente dá as nossas (controle do pool)
            finally { Marshal.FreeHGlobal(info); }
            if (!vpDaSamples) { poolNv12 = new IntPtr[6]; for (int i = 0; i < poolNv12.Length; i++) poolNv12[i] = CriarTextura(larg, alt, 103 /*NV12*/, 0x20 /*RENDER_TARGET*/); }
            // o conversor entrega imagens de um estoque dele; o encoder segura algumas enquanto comprime: estoque maior
            IntPtr oattr;
            if (Com.F<D.IdxPtr>(vp, 10)(vp, 0, out oattr) >= 0 && oattr != IntPtr.Zero)
            {
                Guid k = G.MinSampleCount; Com.F<D.SetU32>(oattr, 21)(oattr, ref k, 12);
                // imagens já "prontas pro compressor" (RENDER_TARGET | VIDEO_ENCODER): a placa comprime sem copiar antes
                if (Environment.GetEnvironmentVariable("TVA_SEM_BIND") != "1") { k = G.BindFlags; Com.F<D.SetU32>(oattr, 21)(oattr, ref k, 0x20 | 0x400); }
                Com.Rel(ref oattr);
            }
            Com.F<D.ProcMsg>(vp, 23)(vp, 0x10000000, IntPtr.Zero); // BEGIN_STREAMING

            copia = CriarTextura(telaW, telaH, 87 /*B8G8R8A8*/, 0x28 /*SHADER_RESOURCE|RENDER_TARGET*/);
            MontarEncoder(larg, alt, fps, false);
            if (string.IsNullOrEmpty(Caminho)) Caminho = "conversor";
        }
        double tEntregue, tPediu, tProcIn, tComprimir, tConverter;
        public string Tempos { get { return "converter " + tConverter.ToString("0.0") + " · ProcessInput " + tProcIn.ToString("0.0") + " · entrega→saída " + tComprimir.ToString("0.0") + " ms"; } }
        bool encDireto; IntPtr[] poolBgra = new IntPtr[0]; int poolBgraIdx;
        public volatile string Caminho = "", EntradasAceitas = "";

        void MontarEncoder(int larg, int alt, int fps, bool argb)
        {
            // procura os encoders H.264 de HARDWARE (NVIDIA/AMD/Intel), o melhor primeiro
            var tin = Marshal.AllocHGlobal(32); var tout = Marshal.AllocHGlobal(32);
            IntPtr acts; uint n;
            try
            {
                Marshal.StructureToPtr(G.Video, tin, false); Marshal.StructureToPtr(G.NV12, tin + 16, false);
                Marshal.StructureToPtr(G.Video, tout, false); Marshal.StructureToPtr(G.H264, tout + 16, false);
                Com.Ok(MFTEnumEx(G.CatEncoder, 0x4 | 0x40 /*HARDWARE|SORTANDFILTER*/, tin, tout, out acts, out n), "MFTEnumEx");
            }
            finally { Marshal.FreeHGlobal(tin); Marshal.FreeHGlobal(tout); }
            if (n == 0) { CoTaskMemFree(acts); throw new Exception("esta placa não tem compressor H.264 pelo Windows"); }
            string falhas = "";
            try
            {
                for (int i = 0; i < n && enc == IntPtr.Zero; i++)
                {
                    IntPtr act = Marshal.ReadIntPtr(acts, i * IntPtr.Size);
                    string nome = "?";
                    try
                    {
                        IntPtr s; uint len; Guid k = G.FriendlyName;
                        if (Com.F<D.GetAllocStr>(act, 13)(act, ref k, out s, out len) >= 0) { nome = Marshal.PtrToStringUni(s); CoTaskMemFree(s); }
                        Guid iid = G.IMFTransform; IntPtr t;
                        Com.Ok(Com.F<D.Activate>(act, 33)(act, ref iid, out t), "ActivateObject");
                        try { ConfigurarEncoder(t, larg, alt, fps, argb); enc = t; Encoder = nome; if (argb) Caminho = "direto"; }
                        catch (Exception e) { falhas += nome + ": " + e.Message + "; "; Com.Rel(ref t); }
                    }
                    catch (Exception e) { falhas += nome + ": " + e.Message + "; "; }
                }
            }
            finally
            {
                for (int i = 0; i < n; i++) { var a = Marshal.ReadIntPtr(acts, i * IntPtr.Size); Com.Rel(ref a); }
                CoTaskMemFree(acts);
            }
            if (enc == IntPtr.Zero) throw new Exception("nenhum compressor da placa aceitou (" + falhas + ")");
        }

        void ConfigurarEncoder(IntPtr t, int larg, int alt, int fps, bool argb)
        {
            IntPtr attrs;
            Com.Ok(Com.F<D.Ptr>(t, 8)(t, out attrs), "GetAttributes");
            try { Guid k = G.AsyncUnlock; Com.Ok(Com.F<D.SetU32>(attrs, 21)(attrs, ref k, 1), "ASYNC_UNLOCK"); }
            finally { Com.Rel(ref attrs); }
            Com.Ok(Com.F<D.ProcMsg>(t, 23)(t, 2 /*SET_D3D_MANAGER*/, mgr), "encoder d3d");
            // saída H.264: High; se não aceitar, Main; depois Baseline
            int hr = -1;
            foreach (uint perfil in new uint[] { 100, 77, 66 })
            {
                var to = TipoVideo(G.H264, larg, alt, fps);
                try
                {
                    Guid k = G.AvgBitrate; Com.F<D.SetU32>(to, 21)(to, ref k, (uint)Kbps * 1000);
                    k = G.Profile; Com.F<D.SetU32>(to, 21)(to, ref k, perfil);
                    Cor709(to, true);
                    hr = Com.F<D.SetType>(t, 16)(t, 0, to, 0);
                }
                finally { Com.Rel(ref to); }
                if (hr >= 0) break;
            }
            Com.Ok(hr, "tipo de saída H.264");
            if (argb)
            {
                // quais formatos de entrada ele aceita? (pra escolher o melhor; vai pra estatística)
                var lista = new System.Text.StringBuilder();
                for (uint i = 0; i < 20; i++)
                {
                    IntPtr at; if (Com.F<D.AvailType>(t, 13)(t, 0, i, out at) < 0) break;
                    Guid k = G.SubType, sub; if (Com.F<D.GetGuid>(at, 10)(at, ref k, out sub) >= 0) lista.Append(sub.ToString().Substring(0, 8)).Append(' ');
                    Com.Rel(ref at);
                }
                EntradasAceitas = lista.ToString();
            }
            IntPtr ti = IntPtr.Zero;
            if (argb)
            {
                // usa o tipo que o próprio compressor oferece pra BGRA (só ajusta tamanho e fps)
                for (uint i = 0; i < 20 && ti == IntPtr.Zero; i++)
                {
                    IntPtr at; if (Com.F<D.AvailType>(t, 13)(t, 0, i, out at) < 0) break;
                    Guid k = G.SubType, sub; Com.F<D.GetGuid>(at, 10)(at, ref k, out sub);
                    if (sub == G.ARGB32) ti = at; else Com.Rel(ref at);
                }
                if (ti == IntPtr.Zero) throw new Exception("não aceita BGRA");
                Guid kk = G.FrameSize; Com.F<D.SetU64>(ti, 22)(ti, ref kk, ((ulong)(uint)larg << 32) | (uint)alt);
                kk = G.FrameRate; Com.F<D.SetU64>(ti, 22)(ti, ref kk, ((ulong)(uint)fps << 32) | 1u);
                kk = G.Interlace; Com.F<D.SetU32>(ti, 21)(ti, ref kk, 2);
            }
            else { ti = TipoVideo(G.NV12, larg, alt, fps); Cor709(ti, true); }
            try { Com.Ok(Com.F<D.SetType>(t, 15)(t, 0, ti, 0), argb ? "tipo de entrada BGRA" : "tipo de entrada NV12"); }
            finally { Com.Rel(ref ti); }

            codec = Com.QI(t, G.ICodecAPI);
            if (codec != IntPtr.Zero)
            {
                SetBool(G.LowLatency, true);
                SetU32(G.BFrames, 0);
                SetU32(G.RateControl, 0); // CBR: taxa estável (o WebRTC gosta)
                SetU32(G.MeanBitRate, (uint)Kbps * 1000);
                SetU32(G.GopSize, (uint)(fps * 8)); // quadro-chave a cada 8 s, e na hora quando alguém pede
            }
            // SPS/PPS (vão na frente de cada quadro-chave)
            IntPtr cur;
            if (Com.F<D.CurType>(t, 18)(t, 0, out cur) >= 0)
            {
                Guid k = G.SeqHeader; uint tam;
                if (Com.F<D.GetBlobSize>(cur, 14)(cur, ref k, out tam) >= 0 && tam > 0 && tam < 4096)
                { var b = new byte[tam]; uint got; if (Com.F<D.GetBlob>(cur, 15)(cur, ref k, b, tam, out got) >= 0) cabecalho = b; }
                Com.Rel(ref cur);
            }
            var info = Marshal.AllocHGlobal(16);
            try { Com.Ok(Com.F<D.StreamInfo>(t, 7)(t, 0, info), "encoder info"); encDaSamples = (Marshal.ReadInt32(info, 0) & 0x300) != 0; encBufTam = (uint)Marshal.ReadInt32(info, 4); }
            finally { Marshal.FreeHGlobal(info); }
            Tamanhos = "tela " + telaW + "x" + telaH + " · enc entrada " + TamTipo(t, 17) + " saída " + TamTipo(t, 18) + (vp != IntPtr.Zero ? " · vp entrada " + TamTipo(vp, 17) + " saída " + TamTipo(vp, 18) : " · sem conversor");
            encGen = Com.QI(t, G.IMFMediaEventGenerator);
            if (encGen == IntPtr.Zero) throw new Exception("encoder sem eventos (não é assíncrono)");
            Com.Ok(Com.F<D.ProcMsg>(t, 23)(t, 0x10000000, IntPtr.Zero), "BEGIN_STREAMING");
            Com.Ok(Com.F<D.ProcMsg>(t, 23)(t, 0x10000003, IntPtr.Zero), "START_OF_STREAM");
            enc = t; // (o laço de eventos usa)
            var ev = new Thread(LacoEncoder) { IsBackground = true, Name = "encoder", Priority = ThreadPriority.AboveNormal };
            ev.SetApartmentState(ApartmentState.MTA);
            ev.Start();
        }

        public volatile string Tamanhos = "";
        static string TamTipo(IntPtr mft, int slot)
        {
            IntPtr ty; if (Com.F<D.CurType>(mft, slot)(mft, 0, out ty) < 0) return "?";
            Guid k = G.FrameSize; ulong tam; int hr = Com.F<D.GetU64>(ty, 8)(ty, ref k, out tam); Com.Rel(ref ty);
            return hr < 0 ? "?" : (tam >> 32) + "x" + (tam & 0xffffffff);
        }

        void SetU32(Guid api, uint v)
        {
            var var = Marshal.AllocHGlobal(24);
            try { for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(var, i, 0); Marshal.WriteInt16(var, 0, 19 /*VT_UI4*/); Marshal.WriteInt32(var, 8, (int)v); Com.F<D.CodecSet>(codec, 9)(codec, ref api, var); }
            finally { Marshal.FreeHGlobal(var); }
        }
        void SetBool(Guid api, bool v)
        {
            var var = Marshal.AllocHGlobal(24);
            try { for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(var, i, 0); Marshal.WriteInt16(var, 0, 11 /*VT_BOOL*/); Marshal.WriteInt16(var, 8, (short)(v ? -1 : 0)); Com.F<D.CodecSet>(codec, 9)(codec, ref api, var); }
            finally { Marshal.FreeHGlobal(var); }
        }

        // ------------------------------------------------------------------ laço do encoder (eventos)
        void LacoEncoder()
        {
            CoInitializeEx(IntPtr.Zero, 0);
            var t = enc;
            var bufs = Marshal.AllocHGlobal(32);
            try
            {
                while (rodando)
                {
                    IntPtr ev;
                    if (Com.F<D.GetEvent>(encGen, 3)(encGen, 0, out ev) < 0) break; // desligado
                    uint tipo; Com.F<D.GetU32Out>(ev, 33)(ev, out tipo);
                    Com.Rel(ref ev);
                    if (tipo == EV_NEED_INPUT) Interlocked.Increment(ref nPede);
                    else if (tipo == EV_HAVE_OUTPUT) Interlocked.Increment(ref nSaida);
                    else { Interlocked.Increment(ref nEventoOutro); UltimoHr = "evento " + tipo; }
                    if (tipo == EV_NEED_INPUT)
                    {
                        tPediu = relogio.Elapsed.TotalMilliseconds;
                        lock (trava)
                        {
                            if (pendente != IntPtr.Zero) { Entregar(pendente); Com.Rel(ref pendente); }
                            else precisaEntrada++;
                        }
                    }
                    else if (tipo == EV_HAVE_OUTPUT) Saida(t, bufs);
                }
            }
            catch (Exception e) { Erro = "encoder: " + e.Message; rodando = false; }
            finally { Marshal.FreeHGlobal(bufs); }
        }

        void Entregar(IntPtr amostra)
        {
            if (pedirChave && codec != IntPtr.Zero) { pedirChave = false; SetU32(G.ForceKey, 1); }
            int nt = novaTaxa; if (nt > 0 && codec != IntPtr.Zero) { novaTaxa = 0; SetU32(G.MeanBitRate, (uint)nt * 1000); }
            tEntregue = relogio.Elapsed.TotalMilliseconds;
            int hr = Com.F<D.ProcIn>(enc, 24)(enc, 0, amostra, 0);
            tProcIn = tProcIn * 0.95 + (relogio.Elapsed.TotalMilliseconds - tEntregue) * 0.05;
            Interlocked.Increment(ref nEntrega);
            if (hr < 0) { Interlocked.Increment(ref nRecusa); UltimoHr = "in 0x" + hr.ToString("x8"); }
            if (hr < 0 && hr != unchecked((int)0xC00D36B5)) Falha("ProcessInput 0x" + hr.ToString("x8"));
        }

        void Saida(IntPtr t, IntPtr bufs)
        {
            IntPtr minha = IntPtr.Zero;
            if (!encDaSamples)
            {
                IntPtr mb; Com.Ok(MFCreateSample(out minha), "MFCreateSample");
                Com.Ok(MFCreateMemoryBuffer(Math.Max(encBufTam, 4u << 20), out mb), "MFCreateMemoryBuffer");
                Com.F<D.VoidPtr>(minha, 42)(minha, mb); Com.Rel(ref mb);
            }
            for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(bufs, i, 0);
            Marshal.WriteIntPtr(bufs, 8, minha);
            uint status;
            int hr = Com.F<D.ProcOut>(t, 25)(t, 0, 1, bufs, out status);
            if (hr < 0) UltimoHr = "out 0x" + hr.ToString("x8");
            IntPtr amostra = Marshal.ReadIntPtr(bufs, 8), eventos = Marshal.ReadIntPtr(bufs, 24);
            Com.Rel(ref eventos);
            if (hr == MF_STREAM_CHANGE)
            {
                // o encoder mudou o formato de saída: aceita o novo
                IntPtr nt;
                if (Com.F<D.AvailType>(t, 14)(t, 0, 0, out nt) >= 0)
                {
                    Guid k = G.FrameSize; ulong tam; Com.F<D.GetU64>(nt, 8)(nt, ref k, out tam);
                    UltimoHr = "troca de formato: sugeriu " + (tam >> 32) + "x" + (tam & 0xffffffff);
                    // mantém o nosso tamanho e fps (o tipo sugerido pode vir com outro)
                    Com.F<D.SetU64>(nt, 22)(nt, ref k, ((ulong)(uint)Largura << 32) | (uint)AlturaSaida);
                    k = G.FrameRate; Com.F<D.SetU64>(nt, 22)(nt, ref k, ((ulong)(uint)Math.Max(10, Fps) << 32) | 1u);
                    int hs = Com.F<D.SetType>(t, 16)(t, 0, nt, 0);
                    if (hs < 0) UltimoHr += " · recusou o nosso 0x" + hs.ToString("x8");
                    Com.Rel(ref nt);
                }
                if (amostra != IntPtr.Zero && amostra != minha) Com.Rel(ref amostra);
                Com.Rel(ref minha);
                return;
            }
            try
            {
                if (hr < 0 || amostra == IntPtr.Zero) return;
                IntPtr buf; Com.Ok(Com.F<D.Ptr>(amostra, 41)(amostra, out buf), "ConvertToContiguousBuffer");
                byte[] dados;
                try
                {
                    IntPtr p; uint max, cur;
                    Com.Ok(Com.F<D.Lock>(buf, 3)(buf, out p, out max, out cur), "Lock");
                    dados = new byte[cur]; Marshal.Copy(p, dados, 0, (int)cur);
                    Com.F<D.Int0>(buf, 4)(buf);
                }
                finally { Com.Rel(ref buf); }
                uint limpo = 0; Guid k = G.CleanPoint; Com.F<D.GetU32>(amostra, 7)(amostra, ref k, out limpo);
                long tempo; Com.F<D.GetI64>(amostra, 35)(amostra, out tempo);
                bool chave = limpo != 0 || TemNal(dados, 5);
                if (chave && !TemNal(dados, 7) && cabecalho.Length > 0)
                {
                    var j = new byte[cabecalho.Length + dados.Length];
                    Buffer.BlockCopy(cabecalho, 0, j, 0, cabecalho.Length); Buffer.BlockCopy(dados, 0, j, cabecalho.Length, dados.Length);
                    dados = j;
                }
                Interlocked.Increment(ref contEnv);
                tComprimir = tComprimir * 0.95 + (relogio.Elapsed.TotalMilliseconds - tEntregue) * 0.05;
                var cb = AoQuadro; if (cb != null) cb(dados, chave, tempo / 10);
            }
            finally
            {
                if (amostra != IntPtr.Zero && amostra != minha) Com.Rel(ref amostra);
                Com.Rel(ref minha);
            }
        }

        // tem NAL desse tipo (5 = IDR, 7 = SPS)?
        static bool TemNal(byte[] d, int tipo)
        {
            for (int i = 0; i + 3 < d.Length && i < 4096; i++)
                if (d[i] == 0 && d[i + 1] == 0 && (d[i + 2] == 1 || (d[i + 2] == 0 && d[i + 3] == 1)))
                {
                    int h = d[i + 2] == 1 ? i + 3 : i + 4;
                    if (h < d.Length && (d[h] & 0x1F) == tipo) return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ captura (no ritmo do fps escolhido)
        void Capturar()
        {
            var info = Marshal.AllocHGlobal(64);
            var bufs = Marshal.AllocHGlobal(32);
            double proximo = 0;
            try
            {
                while (rodando)
                {
                    double intervalo = 1000.0 / Math.Max(10, Math.Min(240, Fps));
                    IntPtr res;
                    int hr = Com.F<D.Acquire>(dup, 8)(dup, (uint)Math.Max(1, intervalo), info, out res);
                    double agora = relogio.Elapsed.TotalMilliseconds;
                    if (hr == DXGI_ACCESS_LOST) { Thread.Sleep(50); CriarCaptura(); continue; } // trocou resolução, UAC, tela cheia...
                    bool novo = false;
                    if (hr >= 0)
                    {
                        // só copia no ritmo pedido (monitor de 144/240 Hz entrega mais quadros que o necessário)
                        if (Marshal.ReadInt64(info, 0) != 0 && agora >= proximo - 1.5)
                        {
                            IntPtr tex = Com.QI(res, G.ID3D11Texture2D);
                            if (tex != IntPtr.Zero) { Com.F<D.Copy>(ctx, 47)(ctx, encDireto ? poolBgra[poolBgraIdx = (poolBgraIdx + 1) % poolBgra.Length] : copia, tex); Com.Rel(ref tex); novo = true; }
                        }
                        Com.Rel(ref res);
                        Com.F<D.Int0>(dup, 14)(dup); // ReleaseFrame
                    }
                    else if (hr != DXGI_TIMEOUT) throw new Exception("AcquireNextFrame 0x" + hr.ToString("x8"));
                    // tela parada não manda nada (o WebRTC aguenta); mas um pedido de quadro-chave reenvia a última imagem
                    if (!novo && !(pedirChave && agora >= proximo)) { Estatistica(); continue; }
                    proximo = Math.Max(proximo + intervalo, agora + intervalo * 0.5);
                    Interlocked.Increment(ref contCap);
                    double tc = relogio.Elapsed.TotalMilliseconds;
                    if (encDireto) EntregarDireto((long)(agora * 10000));
                    else Converter(bufs, (long)(agora * 10000));
                    tConverter = tConverter * 0.95 + (relogio.Elapsed.TotalMilliseconds - tc) * 0.05;
                    Estatistica();
                }
            }
            finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(bufs); }
        }

        void Converter(IntPtr bufs, long tempo100ns)
        {
            IntPtr entrada, buf;
            Com.Ok(MFCreateSample(out entrada), "MFCreateSample");
            try
            {
                Guid iid = G.ID3D11Texture2D;
                Com.Ok(MFCreateDXGISurfaceBuffer(ref iid, copia, 0, 0, out buf), "MFCreateDXGISurfaceBuffer");
                Com.F<D.VoidPtr>(entrada, 42)(entrada, buf); Com.Rel(ref buf);
                Com.F<D.SetI64>(entrada, 36)(entrada, tempo100ns);
                Com.F<D.SetI64>(entrada, 38)(entrada, 10000000L / Math.Max(10, Fps));
                int hi = Com.F<D.ProcIn>(vp, 24)(vp, 0, entrada, 0);
                if (hi == unchecked((int)0xC00D36B5)) { nVpRecusa++; DescartarSaidaVp(bufs); hi = Com.F<D.ProcIn>(vp, 24)(vp, 0, entrada, 0); } // ainda tinha saída: tira e tenta de novo
                if (hi < 0) { if (hi != unchecked((int)0xC00D36B5)) Falha("VP ProcessInput 0x" + hi.ToString("x8")); return; }
            }
            finally { Com.Rel(ref entrada); }

            IntPtr minha = IntPtr.Zero;
            if (!vpDaSamples)
            {
                Com.Ok(MFCreateSample(out minha), "MFCreateSample");
                Guid iid = G.ID3D11Texture2D; IntPtr b;
                Com.Ok(MFCreateDXGISurfaceBuffer(ref iid, poolNv12[poolIdx++ % poolNv12.Length], 0, 0, out b), "NV12 buffer");
                Com.F<D.VoidPtr>(minha, 42)(minha, b); Com.Rel(ref b);
            }
            for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(bufs, i, 0);
            Marshal.WriteIntPtr(bufs, 8, minha);
            uint status;
            int hr = Com.F<D.ProcOut>(vp, 25)(vp, 0, 1, bufs, out status);
            if (hr < 0) { nVpFalha++; UltimoVp = "saída 0x" + hr.ToString("x8"); } else { nVpOk++; if ((Marshal.ReadInt32(bufs, 16) & 0x01000000) != 0) nVpIncompleto++; }
            IntPtr saida = Marshal.ReadIntPtr(bufs, 8), eventos = Marshal.ReadIntPtr(bufs, 24);
            Com.Rel(ref eventos);
            if (hr < 0 || saida == IntPtr.Zero) { if (saida != minha) Com.Rel(ref saida); Com.Rel(ref minha); if (hr != MF_NEED_INPUT) Falha("VP ProcessOutput 0x" + hr.ToString("x8") + " (dá amostras: " + vpDaSamples + ")"); return; }
            if (saida == minha) minha = IntPtr.Zero; // a amostra agora é "saida"
            Com.F<D.SetI64>(saida, 36)(saida, tempo100ns);
            Com.F<D.SetI64>(saida, 38)(saida, 10000000L / Math.Max(10, Fps));
            lock (trava)
            {
                if (precisaEntrada > 0) { precisaEntrada--; Entregar(saida); Com.Rel(ref saida); }
                else { Com.Rel(ref pendente); pendente = saida; } // o encoder ainda está ocupado: fica a mais nova
            }
        }

        void EntregarDireto(long tempo100ns)
        {
            IntPtr amostra, buf;
            Com.Ok(MFCreateSample(out amostra), "MFCreateSample");
            Guid iid = G.ID3D11Texture2D;
            if (MFCreateDXGISurfaceBuffer(ref iid, poolBgra[poolBgraIdx], 0, 0, out buf) < 0) { Com.Rel(ref amostra); return; }
            Com.F<D.VoidPtr>(amostra, 42)(amostra, buf); Com.Rel(ref buf);
            Com.F<D.SetI64>(amostra, 36)(amostra, tempo100ns);
            Com.F<D.SetI64>(amostra, 38)(amostra, 10000000L / Math.Max(10, Fps));
            lock (trava)
            {
                if (precisaEntrada > 0) { precisaEntrada--; Entregar(amostra); Com.Rel(ref amostra); }
                else { Com.Rel(ref pendente); pendente = amostra; }
            }
        }

        void DescartarSaidaVp(IntPtr bufs)
        {
            IntPtr minha = IntPtr.Zero;
            if (!vpDaSamples)
            {
                MFCreateSample(out minha); Guid iid = G.ID3D11Texture2D; IntPtr b;
                if (MFCreateDXGISurfaceBuffer(ref iid, poolNv12[poolIdx++ % poolNv12.Length], 0, 0, out b) >= 0) { Com.F<D.VoidPtr>(minha, 42)(minha, b); Com.Rel(ref b); }
            }
            for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(bufs, i, 0);
            Marshal.WriteIntPtr(bufs, 8, minha);
            uint st; int ho = Com.F<D.ProcOut>(vp, 25)(vp, 0, 1, bufs, out st);
            UltimoVp = "descarte 0x" + ho.ToString("x8");
            IntPtr saida = Marshal.ReadIntPtr(bufs, 8), ev = Marshal.ReadIntPtr(bufs, 24);
            Com.Rel(ref ev); if (saida != minha) Com.Rel(ref saida); Com.Rel(ref minha);
        }

        void Estatistica()
        {
            long s = relogio.ElapsedMilliseconds / 1000;
            if (s != ultSeg) { ultSeg = s; Capturados = Interlocked.Exchange(ref contCap, 0); Enviados = Interlocked.Exchange(ref contEnv, 0); }
        }

        void Desmontar()
        {
            try
            {
                if (enc != IntPtr.Zero)
                {
                    Com.F<D.ProcMsg>(enc, 23)(enc, 0x10000001 /*END_STREAMING*/, IntPtr.Zero);
                    var sd = Com.QI(enc, G.IMFShutdown); // destrava o laço de eventos
                    if (sd != IntPtr.Zero) { Com.F<D.Int0>(sd, 3)(sd); Com.Rel(ref sd); }
                }
            }
            catch { }
            Thread.Sleep(50);
            lock (trava) Com.Rel(ref pendente);
            Com.Rel(ref codec); Com.Rel(ref encGen); Com.Rel(ref enc);
            Com.Rel(ref vp);
            for (int i = 0; i < poolNv12.Length; i++) Com.Rel(ref poolNv12[i]);
            for (int i = 0; i < poolBgra.Length; i++) Com.Rel(ref poolBgra[i]);
            Com.Rel(ref copia); Com.Rel(ref dup); Com.Rel(ref mgr); Com.Rel(ref ctx); Com.Rel(ref dev);
            precisaEntrada = 0;
        }
    }
}
