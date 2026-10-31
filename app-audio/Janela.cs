// Janelinha do programa (mesmo visual do site) + ícone perto do relógio.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TelaAoVivoAudio
{
    class Janela : Form
    {
        static readonly Color Fundo = Color.FromArgb(13, 14, 16), Sup = Color.FromArgb(22, 23, 27), Linha = Color.FromArgb(37, 39, 46);
        static readonly Color Texto = Color.FromArgb(236, 237, 240), Sub = Color.FromArgb(146, 150, 161), Fraco = Color.FromArgb(98, 102, 111);
        static readonly Color Acento = Color.FromArgb(255, 91, 55), Ok = Color.FromArgb(63, 216, 132), Aviso = Color.FromArgb(244, 181, 63);
        const string ChaveIniciar = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Motor motor;
        readonly NotifyIcon bandeja;
        readonly Label estado, detalhe;
        readonly Panel ponto;
        readonly CheckBox iniciar;
        readonly System.Windows.Forms.Timer relogio;
        Color corPonto;
        bool sair, comecarEscondida;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public Janela(Motor motor, bool escondida)
        {
            this.motor = motor;
            comecarEscondida = escondida;
            Text = "Tela ao Vivo Áudio";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(420, 268);
            BackColor = Fundo;
            ForeColor = Texto;
            Font = new Font("Segoe UI", 9.75f);

            var marca = new Label { Text = "●  Tela ao Vivo", ForeColor = Acento, Font = new Font("Segoe UI Semibold", 9.5f), AutoSize = true, Location = new Point(24, 20) };
            var titulo = new Label { Text = "Áudio do PC, sem o Discord", Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Location = new Point(22, 42) };

            var cartao = new Panel { Location = new Point(24, 88), Size = new Size(372, 74), BackColor = Sup };
            cartao.Paint += (s, e) => { using (var p = new Pen(Linha)) e.Graphics.DrawRectangle(p, 0, 0, cartao.Width - 1, cartao.Height - 1); };
            ponto = new Panel { Location = new Point(16, 20), Size = new Size(10, 10), BackColor = Sup };
            ponto.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(corPonto)) e.Graphics.FillEllipse(b, 0, 0, 9, 9); };
            estado = new Label { Location = new Point(34, 14), AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), BackColor = Sup };
            detalhe = new Label { Location = new Point(34, 38), Size = new Size(326, 34), ForeColor = Sub, BackColor = Sup };
            cartao.Controls.AddRange(new Control[] { ponto, estado, detalhe });

            var abrir = new Button { Text = "Abrir o site", Location = new Point(24, 178), Size = new Size(150, 38), FlatStyle = FlatStyle.Flat, BackColor = Acento, ForeColor = Color.FromArgb(23, 8, 4), Font = new Font("Segoe UI Semibold", 10f), Cursor = Cursors.Hand };
            abrir.FlatAppearance.BorderSize = 0;
            abrir.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 118, 86);
            abrir.Click += (s, e) => AbrirSite();

            iniciar = new CheckBox { Text = "Abrir junto com o Windows", Location = new Point(190, 186), AutoSize = true, ForeColor = Sub, Cursor = Cursors.Hand };
            iniciar.Checked = IniciaComWindows();
            iniciar.CheckedChanged += (s, e) => DefinirIniciar(iniciar.Checked);

            var rodape = new Label { Text = "Pode fechar esta janela: o programa continua perto do relógio.", Location = new Point(24, 234), AutoSize = true, ForeColor = Fraco, Font = new Font("Segoe UI", 8.75f) };

            Controls.AddRange(new Control[] { marca, titulo, cartao, abrir, iniciar, rodape });

            var menu = new ContextMenuStrip();
            menu.Items.Add("Mostrar", null, (s, e) => Mostrar());
            menu.Items.Add("Abrir o site", null, (s, e) => AbrirSite());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, e) => { sair = true; Close(); });
            bandeja = new NotifyIcon { Icon = Icon, Text = "Tela ao Vivo Áudio", Visible = true, ContextMenuStrip = menu };
            bandeja.DoubleClick += (s, e) => Mostrar();

            relogio = new System.Windows.Forms.Timer { Interval = 500 };
            relogio.Tick += (s, e) => AtualizarEstado();
            relogio.Start();
            AtualizarEstado();

            // outra cópia aberta pediu pra mostrar esta janela
            var sinal = new EventWaitHandle(false, EventResetMode.AutoReset, "TelaAoVivoAudio-mostrar");
            new Thread(() => { while (true) { sinal.WaitOne(); try { BeginInvoke((Action)Mostrar); } catch { return; } } }) { IsBackground = true }.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int sim = 1; // barra de título escura (Windows 10 20H1+ / 11)
            DwmSetWindowAttribute(Handle, 20, ref sim, 4);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (comecarEscondida) { comecarEscondida = false; CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!sair && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                bandeja.ShowBalloonTip(3000, "Tela ao Vivo Áudio", "Continua rodando aqui. Clique com o botão direito pra sair.", ToolTipIcon.None);
                return;
            }
            bandeja.Visible = false;
            base.OnFormClosing(e);
        }

        void Mostrar() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        void AbrirSite() { try { Process.Start(Programa.Site); } catch { } }

        void AtualizarEstado()
        {
            string est = motor.Estado, det = motor.Detalhe;
            Color cor = motor.Conectado ? Ok : Fraco;
            if (!Programa.WindowsSuporta) { est = "Windows antigo demais"; det = "Precisa do Windows 10 versão 2004 (maio de 2020) ou mais novo."; cor = Aviso; }
            else if (motor.ErroPorta != null) { est = "Não consegui abrir a porta " + Motor.Porta; det = "Outro programa está usando. Feche cópias antigas e abra de novo."; cor = Aviso; }
            if (estado.Text != est) estado.Text = est;
            if (detalhe.Text != det) detalhe.Text = det;
            if (corPonto != cor) { corPonto = cor; ponto.Invalidate(); }
            var dica = "Tela ao Vivo Áudio · " + est;
            bandeja.Text = dica.Length > 63 ? dica.Substring(0, 63) : dica;
        }

        static bool IniciaComWindows()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(ChaveIniciar)) return k != null && k.GetValue("TelaAoVivoAudio") != null; }
            catch { return false; }
        }

        static void DefinirIniciar(bool sim)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(ChaveIniciar))
                {
                    if (sim) k.SetValue("TelaAoVivoAudio", "\"" + Application.ExecutablePath + "\" --bandeja");
                    else k.DeleteValue("TelaAoVivoAudio", false);
                }
            }
            catch { }
        }
    }
}
