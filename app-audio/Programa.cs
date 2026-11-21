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
[assembly: AssemblyVersion("1.3.3.0")]
[assembly: AssemblyFileVersion("1.3.3.0")]

namespace TelaAoVivoAudio
{
    static class Programa
    {
        public const string Versao = "1.3.3";
        public const string Site = "https://tela-ao-vivo.vercel.app/";
        public static bool WindowsSuporta;
        public static bool NaBandeja;
        // fecha o programa pela thread da janela (o atualizador chama depois de pôr o .exe novo no lugar)
        public static void Sair()
        {
            try { var f = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null; if (f != null) f.BeginInvoke(new Action(Application.Exit)); else Application.Exit(); }
            catch { Environment.Exit(0); }
        }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            Atualizador.DepoisDeAtualizar(args); // se veio de uma atualização: espera o antigo fechar
            NaBandeja = args.Contains("--bandeja");
            // veio de uma atualização: se reabre uma vez "limpo". O 1.3.0 abria a versão nova herdando a porta
            // do antigo (e aí o som/vídeo não conectavam até fechar e abrir de novo).
            if (args.Contains("--esperar") && !args.Contains("--limpo"))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, "--limpo --esperar " + System.Diagnostics.Process.GetCurrentProcess().Id + (NaBandeja ? " --bandeja" : "")) { UseShellExecute = true });
                    return;
                }
                catch { }
            }
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
                // confere se tem versão nova (ao abrir, e a cada 6 horas); só atualiza se ninguém estiver usando
                new Thread(() => { Thread.Sleep(5000); while (true) { Atualizador.Tentar(motor, true); Thread.Sleep(6 * 3600 * 1000); } }) { IsBackground = true, Name = "atualizar" }.Start();
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
