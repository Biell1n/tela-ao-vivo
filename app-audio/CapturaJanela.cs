// CapturaJanela: a captura de JANELA do Windows (Windows.Graphics.Capture), a mesma que o Discord/OBS usam.
// Pega a imagem da própria janela, mesmo se outra janela estiver na frente. Já vem com o cursor.
// Tudo pela tabela de funções do WinRT (sem bibliotecas: o programa continua um .exe só).
using System;
using System.Runtime.InteropServices;

namespace TelaAoVivoAudio
{
    [StructLayout(LayoutKind.Sequential)] struct TamanhoInt { public int W, H; }

    sealed class CapturaJanela : IDisposable
    {
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int len, out IntPtr h);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);
        [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CriarParaJanelaD(IntPtr s, IntPtr hwnd, ref Guid iid, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int TamanhoD(IntPtr s, out TamanhoInt t);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CriarPoolD(IntPtr s, IntPtr dev, int formato, int buffers, TamanhoInt t, out IntPtr pool);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RecriarD(IntPtr s, IntPtr dev, int formato, int buffers, TamanhoInt t);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SessaoD(IntPtr s, IntPtr item, out IntPtr sessao);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetBoolD(IntPtr s, byte v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetInterfaceD(IntPtr s, ref Guid iid, out IntPtr o);

        static readonly Guid IItemInterop = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
        static readonly Guid IItem = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
        static readonly Guid IPoolStatics2 = new Guid("589b103f-6bbc-5df5-a991-02e28b3b66d5");
        static readonly Guid ISessao2 = new Guid("2c39ae40-7d2e-5044-804e-8b6799d4cf9e");
        static readonly Guid ISessao3 = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be");
        static readonly Guid IClosable = new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e");
        static readonly Guid IDxgiAccess = new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
        static readonly Guid ITexture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        IntPtr devWinRT, item, pool, sessao;
        public TamanhoInt Tamanho;

        static IntPtr Fabrica(string classe, Guid iid)
        {
            IntPtr h; Com.Ok(WindowsCreateString(classe, classe.Length, out h), "WindowsCreateString");
            try { IntPtr f; Com.Ok(RoGetActivationFactory(h, ref iid, out f), "RoGetActivationFactory " + classe); return f; }
            finally { WindowsDeleteString(h); }
        }

        public CapturaJanela(IntPtr devD3D, IntPtr hwnd)
        {
            var dxgi = Com.QI(devD3D, G.IDXGIDevice);
            try { Com.Ok(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out devWinRT), "CreateDirect3D11DeviceFromDXGIDevice"); }
            finally { Com.Rel(ref dxgi); }
            // o "item" da janela
            var interop = Fabrica("Windows.Graphics.Capture.GraphicsCaptureItem", IItemInterop);
            try { Guid i = IItem; Com.Ok(Com.F<CriarParaJanelaD>(interop, 3)(interop, hwnd, ref i, out item), "não deu pra capturar essa janela"); }
            finally { Com.Rel(ref interop); }
            Com.Ok(Com.F<TamanhoD>(item, 7)(item, out Tamanho), "tamanho da janela");
            // o "estoque" de quadros (livre de thread: dá pra pegar os quadros na nossa thread de captura)
            var est = Fabrica("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IPoolStatics2);
            try { Com.Ok(Com.F<CriarPoolD>(est, 6)(est, devWinRT, 87 /*B8G8R8A8*/, 2, Tamanho, out pool), "CreateFreeThreaded"); }
            finally { Com.Rel(ref est); }
            Com.Ok(Com.F<SessaoD>(pool, 10)(pool, item, out sessao), "CreateCaptureSession");
            var s2 = Com.QI(sessao, ISessao2);       // cursor junto
            if (s2 != IntPtr.Zero) { Com.F<SetBoolD>(s2, 7)(s2, 1); Com.Rel(ref s2); }
            var s3 = Com.QI(sessao, ISessao3);       // sem a borda amarela (só no Windows 11; no 10 não tem como)
            if (s3 != IntPtr.Zero) { Com.F<SetBoolD>(s3, 7)(s3, 0); Com.Rel(ref s3); }
            Com.Ok(Com.F<D.Int0>(sessao, 6)(sessao), "StartCapture");
        }

        // o próximo quadro da janela (textura na placa + o tamanho real do conteúdo), ou nada se não chegou quadro novo
        public bool Proximo(out IntPtr textura, out TamanhoInt conteudo, out IntPtr quadro)
        {
            textura = IntPtr.Zero; conteudo = new TamanhoInt(); quadro = IntPtr.Zero;
            IntPtr f;
            if (Com.F<D.Ptr>(pool, 7)(pool, out f) < 0 || f == IntPtr.Zero) return false; // TryGetNextFrame
            quadro = f;
            Com.F<TamanhoD>(f, 8)(f, out conteudo);                                         // ContentSize
            IntPtr sup; if (Com.F<D.Ptr>(f, 6)(f, out sup) < 0 || sup == IntPtr.Zero) { Soltar(ref quadro); return false; } // Surface
            try
            {
                var acesso = Com.QI(sup, IDxgiAccess);
                if (acesso == IntPtr.Zero) { Soltar(ref quadro); return false; }
                try { Guid t = ITexture2D; Com.F<GetInterfaceD>(acesso, 3)(acesso, ref t, out textura); }
                finally { Com.Rel(ref acesso); }
            }
            finally { Com.Rel(ref sup); }
            if (textura == IntPtr.Zero) { Soltar(ref quadro); return false; }
            return true;
        }

        // a janela mudou de tamanho: o estoque passa a ter o tamanho novo
        public void Recriar(TamanhoInt t)
        {
            Tamanho = t;
            Com.F<RecriarD>(pool, 6)(pool, devWinRT, 87, 2, t);
        }

        public static void Soltar(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            var c = Com.QI(obj, IClosable);
            if (c != IntPtr.Zero) { Com.F<D.Int0>(c, 6)(c); Com.Rel(ref c); }
            Com.Rel(ref obj);
        }

        public void Dispose()
        {
            Soltar(ref sessao); Soltar(ref pool); Com.Rel(ref item); Com.Rel(ref devWinRT);
        }
    }
}
