// Atualizador: o programa se atualiza sozinho pelo site (igual ao Discord).
//  1) lê https://tela-ao-vivo.vercel.app/versao.json  -> { "versao": "1.3.0", "sha256": "..." }
//  2) se for mais nova, baixa o .exe, confere o tamanho/assinatura do Windows ("MZ") e o SHA-256
//  3) renomeia o .exe que está rodando (o Windows deixa), põe o novo no lugar, abre o novo e fecha este
//  4) o novo espera este fechar, apaga o antigo e segue normal
// Nunca atualiza no meio de uma transmissão de vídeo pelo programa. Se der errado, nada muda (o antigo continua).
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TelaAoVivoAudio
{
    static class Atualizador
    {
        public static volatile string Estado = "";   // pra janela e pro site
        public static volatile string Disponivel = ""; // versão nova encontrada (vazio = está em dia)
        static int rodando;

        public static bool MaisNova(string a, string b)
        {
            var x = (a ?? "").Split('.'); var y = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = 0, q = 0;
                if (i < x.Length) int.TryParse(x[i], out p);
                if (i < y.Length) int.TryParse(y[i], out q);
                if (p != q) return p > q;
            }
            return false;
        }

        static string Baixar(string url, string destino)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 20000; req.ReadWriteTimeout = 20000; req.UserAgent = "TelaAoVivoAudio/" + Programa.Versao;
            req.Headers.Add("Cache-Control", "no-cache");
            using (var r = req.GetResponse())
            using (var s = r.GetResponseStream())
            {
                if (destino == null) using (var sr = new StreamReader(s)) return sr.ReadToEnd();
                using (var f = File.Create(destino)) s.CopyTo(f);
                return destino;
            }
        }

        // confere e, se tiver versão nova, atualiza. "automatico": só se ninguém estiver usando o programa agora.
        public static void Tentar(Motor motor, bool automatico)
        {
            if (Interlocked.Exchange(ref rodando, 1) == 1) return;
            string novo = null;
            try
            {
                var info = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                    Baixar(Programa.Site + "versao.json?t=" + DateTime.UtcNow.Ticks, null));
                string versao = info.ContainsKey("versao") ? info["versao"] as string : "";
                string sha = info.ContainsKey("sha256") ? (info["sha256"] as string ?? "").ToLowerInvariant() : "";
                if (!MaisNova(versao, Programa.Versao)) { Disponivel = ""; if (!automatico) Estado = "Você já tem a versão mais nova (" + Programa.Versao + ")."; return; }
                Disponivel = versao;
                if (motor.TransmitindoVideo) { Estado = "Versão " + versao + " disponível: atualiza quando a transmissão terminar."; return; }
                if (automatico && motor.Clientes > 0) { Estado = "Versão " + versao + " disponível."; return; }

                Estado = "Baixando a versão " + versao + "...";
                string exe = Application.ExecutablePath;
                novo = exe + ".novo";
                try { File.Delete(novo); } catch { }
                Baixar(Programa.Site + "TelaAoVivoAudio.exe?v=" + versao, novo);
                var bytes = File.ReadAllBytes(novo);
                if (bytes.Length < 20000 || bytes[0] != 'M' || bytes[1] != 'Z') throw new Exception("o arquivo baixado não é um programa");
                if (sha.Length == 64)
                    using (var h = SHA256.Create())
                        if (BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != sha) throw new Exception("o arquivo chegou incompleto");

                // troca: o .exe em uso pode ser renomeado (não apagado); o novo fica com o nome original
                string antigo = exe + ".antigo";
                try { File.Delete(antigo); } catch { }
                File.Move(exe, antigo);
                try { File.Move(novo, exe); }
                catch { File.Move(antigo, exe); throw; } // não deu: volta o antigo
                novo = null;
                Estado = "Atualizado pra " + versao + ". Reabrindo...";
                bool bandeja = Programa.NaBandeja;
                // fecha as conexões ANTES (senão o novo herda a porta presa) e abre o novo "limpo" (sem herdar nada)
                try { motor.Parar(); } catch { }
                Process.Start(new ProcessStartInfo(exe, "--esperar " + Process.GetCurrentProcess().Id + (bandeja ? " --bandeja" : "")) { UseShellExecute = true });
                Programa.Sair();
            }
            catch (Exception e)
            {
                Estado = "Não deu pra atualizar sozinho (" + e.Message + "). Baixe de novo pelo site.";
                if (novo != null) try { File.Delete(novo); } catch { }
            }
            finally { Interlocked.Exchange(ref rodando, 0); }
        }

        // chamado pelo programa novo, logo ao abrir: espera o antigo fechar e apaga o arquivo dele
        public static void DepoisDeAtualizar(string[] args)
        {
            int i = Array.IndexOf(args, "--esperar");
            if (i >= 0 && i + 1 < args.Length)
            {
                int pid;
                if (int.TryParse(args[i + 1], out pid))
                    try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
            }
            string antigo = Application.ExecutablePath + ".antigo";
            for (int k = 0; k < 10 && File.Exists(antigo); k++) { try { File.Delete(antigo); } catch { Thread.Sleep(300); } }
        }
    }
}
