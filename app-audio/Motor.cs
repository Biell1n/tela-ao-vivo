// Motor: servidor local (WebSocket em localhost:47800) que só aceita o site Tela ao Vivo,
// manda a lista de apps com som e o áudio escolhido (PCM 48 kHz estéreo 16 bits, pacotes de 10 ms).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TelaAoVivoAudio
{
    class Cliente
    {
        public TcpClient Tcp;
        public NetworkStream Rede;
        public readonly BlockingCollection<byte[]> Fila = new BlockingCollection<byte[]>(200);
        public readonly HashSet<string> IconesEnviados = new HashSet<string>();
        public volatile bool Fechado;
        public string Origem;

        public void Enviar(byte[] quadro)
        {
            if (Fechado) return;
            if (!Fila.TryAdd(quadro)) { } // fila cheia (internet/aba travada): descarta pra não acumular atraso
        }
        public void Fechar()
        {
            if (Fechado) return;
            Fechado = true;
            try { Fila.CompleteAdding(); } catch { }
            try { Tcp.Close(); } catch { }
        }
    }

    class Motor
    {
        public const int Porta = 47800;
        static readonly string[] Discords = { "discord.exe", "discordptb.exe", "discordcanary.exe" };

        readonly List<Cliente> clientes = new List<Cliente>();
        readonly Dictionary<string, Captura> capturas = new Dictionary<string, Captura>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        readonly List<TcpListener> ouvintes = new List<TcpListener>();
        volatile bool parar;

        // o que o site pediu
        string modo = "excluir";                       // "excluir" = tudo menos o Discord · "incluir" = só os apps marcados
        HashSet<string> apps = new HashSet<string>();  // exes marcados (modo incluir)
        readonly object travaConfig = new object();
        readonly AutoResetEvent mudouConfig = new AutoResetEvent(false);

        // pra janela mostrar
        public volatile string Estado = "Esperando o site";
        public volatile string Detalhe = "";
        public volatile bool Conectado;
        public volatile string ErroPorta;

        public int Clientes { get { lock (clientes) return clientes.Count; } }

        public void Iniciar()
        {
            foreach (var ip in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                try
                {
                    var l = new TcpListener(ip, Porta);
                    l.Start();
                    ouvintes.Add(l);
                    var t = new Thread(() => Aceitar(l)) { IsBackground = true, Name = "aceitar " + ip };
                    t.Start();
                }
                catch (Exception e) { if (ip.Equals(IPAddress.Loopback)) ErroPorta = e.Message; }
            }
            var s = new Thread(LacoSessoes) { IsBackground = true, Name = "sessoes" };
            s.SetApartmentState(ApartmentState.MTA);
            s.Start();
            new Thread(LacoMixer) { IsBackground = true, Name = "mixer", Priority = ThreadPriority.AboveNormal }.Start();
        }

        public void Parar()
        {
            parar = true;
            foreach (var l in ouvintes) try { l.Stop(); } catch { }
            lock (clientes) foreach (var c in clientes) c.Fechar();
            lock (capturas) foreach (var c in capturas.Values) c.Parar();
        }

        // ================================================================== conexões
        void Aceitar(TcpListener l)
        {
            while (!parar)
            {
                TcpClient tcp;
                try { tcp = l.AcceptTcpClient(); } catch { if (parar) return; Thread.Sleep(200); continue; }
                new Thread(() => Atender(tcp)) { IsBackground = true, Name = "cliente" }.Start();
            }
        }

        static bool OrigemPermitida(string o)
        {
            if (string.IsNullOrEmpty(o)) return false;
            Uri u;
            if (!Uri.TryCreate(o, UriKind.Absolute, out u)) return false;
            if (u.Scheme == "https" && (u.Host == "tela-ao-vivo.vercel.app" || (u.Host.StartsWith("tela-ao-vivo-") && u.Host.EndsWith(".vercel.app")))) return true;
            if (u.Scheme == "http" && (u.Host == "localhost" || u.Host == "127.0.0.1")) return true; // testes locais
            return false;
        }

        void Atender(TcpClient tcp)
        {
            Cliente c = null;
            try
            {
                tcp.NoDelay = true;
                var rede = tcp.GetStream();
                rede.ReadTimeout = 10000;
                var cab = LerCabecalho(rede);
                if (cab == null) { tcp.Close(); return; }
                string origem; cab.TryGetValue("origin", out origem);
                string chave; cab.TryGetValue("sec-websocket-key", out chave);
                string up; cab.TryGetValue("upgrade", out up);

                if (up == null || !up.Equals("websocket", StringComparison.OrdinalIgnoreCase) || chave == null)
                {
                    // não é WebSocket: responde um "estou aqui" simples (útil pra testar no navegador)
                    var corpo = Encoding.UTF8.GetBytes("{\"app\":\"tela-ao-vivo-audio\",\"versao\":\"" + Programa.Versao + "\"}");
                    var r = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + corpo.Length +
                            (OrigemPermitida(origem) ? "\r\nAccess-Control-Allow-Origin: " + origem + "\r\nAccess-Control-Allow-Private-Network: true" : "") +
                            "\r\nConnection: close\r\n\r\n";
                    var rb = Encoding.ASCII.GetBytes(r);
                    rede.Write(rb, 0, rb.Length); rede.Write(corpo, 0, corpo.Length);
                    tcp.Close(); return;
                }
                if (!OrigemPermitida(origem))
                {
                    // outro site tentando ouvir o seu PC: recusa
                    var rb = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                    rede.Write(rb, 0, rb.Length); tcp.Close(); return;
                }
                string aceite;
                using (var sha = SHA1.Create())
                    aceite = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(chave + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var resp = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + aceite + "\r\n\r\n");
                rede.Write(resp, 0, resp.Length);
                rede.ReadTimeout = Timeout.Infinite;

                c = new Cliente { Tcp = tcp, Rede = rede, Origem = origem };
                lock (clientes) clientes.Add(c);
                var cc = c;
                new Thread(() => Escrever(cc)) { IsBackground = true, Name = "escrever" }.Start();
                EnviarTexto(c, json.Serialize(new Dictionary<string, object> {
                    { "tipo", "ola" }, { "versao", Programa.Versao }, { "app", "tela-ao-vivo-audio" } }));
                mudouConfig.Set();
                Ler(c);
            }
            catch { }
            finally
            {
                if (c != null)
                {
                    c.Fechar();
                    lock (clientes) clientes.Remove(c);
                    mudouConfig.Set();
                }
                else try { tcp.Close(); } catch { }
            }
        }

        static Dictionary<string, string> LerCabecalho(NetworkStream rede)
        {
            var sb = new StringBuilder();
            var b = new byte[1];
            while (sb.Length < 16384)
            {
                if (rede.Read(b, 0, 1) <= 0) return null;
                sb.Append((char)b[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb.ToString(sb.Length - 4, 4) == "\r\n\r\n") break;
            }
            var linhas = sb.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < linhas.Length; i++)
            {
                int p = linhas[i].IndexOf(':');
                if (p > 0) d[linhas[i].Substring(0, p).Trim().ToLowerInvariant()] = linhas[i].Substring(p + 1).Trim();
            }
            return d;
        }

        static void LerTudo(NetworkStream s, byte[] b, int n)
        {
            int k = 0;
            while (k < n) { int r = s.Read(b, k, n - k); if (r <= 0) throw new Exception("fechou"); k += r; }
        }

        void Ler(Cliente c)
        {
            var h = new byte[14];
            var texto = new List<byte>();
            while (!parar && !c.Fechado)
            {
                LerTudo(c.Rede, h, 2);
                int op = h[0] & 0x0F; bool fim = (h[0] & 0x80) != 0;
                bool mascara = (h[1] & 0x80) != 0;
                long len = h[1] & 0x7F;
                if (len == 126) { LerTudo(c.Rede, h, 2); len = (h[0] << 8) | h[1]; }
                else if (len == 127) { LerTudo(c.Rede, h, 8); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | h[i]; }
                if (len > 1 << 20) return;
                var m = new byte[4];
                if (mascara) LerTudo(c.Rede, m, 4);
                var dados = new byte[len];
                LerTudo(c.Rede, dados, (int)len);
                if (mascara) for (int i = 0; i < dados.Length; i++) dados[i] ^= m[i & 3];

                if (op == 8) return;                                   // fechar
                if (op == 9) { c.Enviar(Quadro(10, dados)); continue; } // ping -> pong
                if (op == 1 || op == 0) { texto.AddRange(dados); if (fim) { Mensagem(c, Encoding.UTF8.GetString(texto.ToArray())); texto.Clear(); } }
            }
        }

        void Mensagem(Cliente c, string s)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(s); } catch { return; }
            object tipo; m.TryGetValue("tipo", out tipo);
            if ((tipo as string) != "config") return;
            lock (travaConfig)
            {
                object mo; if (m.TryGetValue("modo", out mo) && (mo as string == "incluir" || mo as string == "excluir")) modo = (string)mo;
                object ap;
                if (m.TryGetValue("apps", out ap) && ap is System.Collections.ArrayList)
                    apps = new HashSet<string>(((System.Collections.ArrayList)ap).OfType<string>().Select(x => x.ToLowerInvariant()));
            }
            mudouConfig.Set();
        }

        static byte[] Quadro(int op, byte[] dados)
        {
            int n = dados.Length, h = n < 126 ? 2 : n < 65536 ? 4 : 10;
            var q = new byte[h + n];
            q[0] = (byte)(0x80 | op);
            if (n < 126) q[1] = (byte)n;
            else if (n < 65536) { q[1] = 126; q[2] = (byte)(n >> 8); q[3] = (byte)n; }
            else { q[1] = 127; for (int i = 0; i < 8; i++) q[2 + i] = (byte)((long)n >> (56 - 8 * i)); }
            Buffer.BlockCopy(dados, 0, q, h, n);
            return q;
        }

        void EnviarTexto(Cliente c, string s) { c.Enviar(Quadro(1, Encoding.UTF8.GetBytes(s))); }

        void Escrever(Cliente c)
        {
            try
            {
                foreach (var q in c.Fila.GetConsumingEnumerable())
                    c.Rede.Write(q, 0, q.Length);
            }
            catch { c.Fechar(); }
        }

        // ================================================================== lista de apps + quais capturas rodar
        void LacoSessoes()
        {
            while (!parar)
            {
                try { Atualizar(); } catch { }
                mudouConfig.WaitOne(700);
            }
        }

        void Atualizar()
        {
            var procs = Sessoes.Processos();
            Cliente[] cs; lock (clientes) cs = clientes.ToArray();
            Conectado = cs.Length > 0;

            string m; HashSet<string> marcados;
            lock (travaConfig) { m = modo; marcados = new HashSet<string>(apps); }

            // quais capturas deveriam estar rodando
            var desejadas = new Dictionary<string, Captura>();
            List<AppComSom> lista = null;
            if (cs.Length > 0)
            {
                lista = Sessoes.Listar(procs);
                if (m == "excluir")
                {
                    // tudo menos o Discord: o Windows tira um processo (e os filhos dele) do som
                    uint alvo = 0;
                    foreach (var d in Discords)
                    {
                        var r = Sessoes.Raizes(d, procs);
                        if (r.Count > 0) { alvo = r[0]; break; }
                    }
                    if (alvo == 0) alvo = (uint)Process.GetCurrentProcess().Id; // Discord fechado: tira só a gente (nada)
                    var c = new Captura(alvo, true); desejadas[c.Chave] = c;
                }
                else
                {
                    foreach (var exe in marcados)
                        foreach (var pid in Sessoes.Raizes(exe, procs).Take(4))
                        { var c = new Captura(pid, false); desejadas[c.Chave] = c; }
                }
            }

            lock (capturas)
            {
                foreach (var k in capturas.Keys.ToList())
                    if (!desejadas.ContainsKey(k) || capturas[k].Erro != null) { capturas[k].Parar(); capturas.Remove(k); }
                foreach (var kv in desejadas)
                    if (!capturas.ContainsKey(kv.Key)) { capturas[kv.Key] = kv.Value; kv.Value.Iniciar(); }
            }

            // status pra janela
            bool discordAberto = Discords.Any(d => Sessoes.Raizes(d, procs).Count > 0);
            if (cs.Length == 0) { Estado = "Esperando o site"; Detalhe = "Abra o Tela ao Vivo no Chrome e escolha o áudio pelo programa."; }
            else if (m == "excluir") { Estado = "Conectado ao site"; Detalhe = discordAberto ? "Mandando todo o som do PC, menos o Discord." : "Mandando todo o som do PC (o Discord está fechado)."; }
            else { Estado = "Conectado ao site"; Detalhe = marcados.Count == 0 ? "Nenhum app marcado: indo sem som." : "Mandando o som de " + marcados.Count + (marcados.Count == 1 ? " app." : " apps."); }

            if (cs.Length == 0 || lista == null) return;
            string[] erros; lock (capturas) erros = capturas.Values.Where(x => x.Erro != null).Select(x => x.Erro).ToArray();
            foreach (var c in cs)
            {
                var itens = new List<object>();
                foreach (var a in lista)
                {
                    var d = new Dictionary<string, object> {
                        { "exe", a.Exe }, { "nome", a.Nome }, { "pico", Math.Round(a.Pico, 3) },
                        { "tocando", a.Tocando }, { "discord", Discords.Contains(a.Exe) } };
                    if (!c.IconesEnviados.Contains(a.Exe)) { d["icone"] = Sessoes.Icone(a.Caminho); c.IconesEnviados.Add(a.Exe); }
                    itens.Add(d);
                }
                EnviarTexto(c, json.Serialize(new Dictionary<string, object> {
                    { "tipo", "apps" }, { "lista", itens }, { "modo", m }, { "discordAberto", discordAberto },
                    { "capturando", desejadas.Count }, { "erro", erros.FirstOrDefault() } }));
            }
        }

        // ================================================================== mistura e envia a cada 10 ms
        void LacoMixer()
        {
            const int Q = Captura.Taxa / 100;                 // 480 quadros = 10 ms
            const int B = Q * Captura.BytesPorQuadro;         // 1920 bytes
            const int Prepara = B * 4, Teto = B * 20, Alvo = B * 6; // segura ~40 ms, corta acima de 200 ms
            var tmp = new byte[B];
            var soma = new int[Q * 2];
            var sw = Stopwatch.StartNew();
            long feitos = 0;
            while (!parar)
            {
                long devidos = sw.ElapsedTicks * Captura.Taxa / Stopwatch.Frequency;
                if (devidos - feitos < Q) { Thread.Sleep(2); continue; }
                if (devidos - feitos > Captura.Taxa / 5) feitos = devidos - Q; // travou (PC suspenso etc.): pula

                Captura[] caps; lock (capturas) caps = capturas.Values.ToArray();
                Array.Clear(soma, 0, soma.Length);
                foreach (var cap in caps)
                {
                    var a = cap.Anel;
                    int disp = a.Disponivel;
                    if (disp > Teto) { a.Descartar(disp - Alvo); disp = a.Disponivel; }
                    if (!a.Pronto) { if (disp >= Prepara) a.Pronto = true; else continue; }
                    if (disp < B) { a.Pronto = false; continue; }
                    a.Ler(tmp, B);
                    for (int i = 0; i < soma.Length; i++) soma[i] += (short)(tmp[2 * i] | (tmp[2 * i + 1] << 8));
                }
                feitos += Q;

                Cliente[] cs; lock (clientes) cs = clientes.ToArray();
                if (cs.Length == 0) continue;
                var pcm = new byte[B];
                for (int i = 0; i < soma.Length; i++)
                {
                    int v = soma[i]; if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                    pcm[2 * i] = (byte)v; pcm[2 * i + 1] = (byte)(v >> 8);
                }
                var q = Quadro(2, pcm);
                foreach (var c in cs) c.Enviar(q);
            }
        }
    }
}
