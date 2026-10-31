// Tela ao Vivo Áudio: entrega pro site o som do PC sem o Discord, ou só dos apps escolhidos.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Tela ao Vivo Áudio")]
[assembly: AssemblyProduct("Tela ao Vivo Áudio")]
[assembly: AssemblyDescription("Manda pro site Tela ao Vivo o som do PC sem o Discord, ou só dos apps escolhidos.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace TelaAoVivoAudio
{
    static class Programa
    {
        public const string Versao = "1.0.0";
        public const string Site = "https://tela-ao-vivo.vercel.app/";
        public static bool WindowsSuporta;

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool novo;
            using (var unico = new Mutex(true, "TelaAoVivoAudio-1", out novo))
            {
                if (!novo)
                {
                    // já está aberto: só mostra a janela da cópia que está rodando
                    try { EventWaitHandle.OpenExisting("TelaAoVivoAudio-mostrar").Set(); } catch { }
                    return;
                }
                WindowsSuporta = Build() >= 19041;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var motor = new Motor();
                if (WindowsSuporta) motor.Iniciar();
                Application.Run(new Janela(motor, args.Contains("--bandeja")));
                motor.Parar();
            }
        }

        static int Build()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    return int.Parse((string)k.GetValue("CurrentBuildNumber"));
            }
            catch { return 0; }
        }
    }
}
