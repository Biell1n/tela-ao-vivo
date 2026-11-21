// Desempenho: mede quanto o processador e a placa de vídeo estão sendo usados (os mesmos números do
// Gerenciador de Tarefas) e manda pro site, pra quem assiste saber de onde vem a queda de fps.
//  - gpu = o motor 3D da placa (o que o jogo usa). Perto de 100% = placa no limite, e o Windows não consegue
//          capturar a tela a tempo.
//  - enc = o motor de compressão de vídeo da placa (NVENC/AMF/QuickSync). Acima de 0 = alguém está comprimindo
//          vídeo pela placa (o navegador, quando a aceleração está funcionando).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TelaAoVivoAudio
{
    class Desempenho
    {
        PerformanceCounter cpu;
        readonly Dictionary<string, PerformanceCounter> motores = new Dictionary<string, PerformanceCounter>();
        PerformanceCounterCategory categoria;
        public int Cpu = -1, Gpu = -1, Enc = -1;

        public void Medir()
        {
            try
            {
                if (cpu == null) { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); cpu.NextValue(); }
                else Cpu = (int)Math.Round(Math.Min(100, cpu.NextValue()));
            }
            catch { Cpu = -1; }
            try
            {
                if (categoria == null) categoria = new PerformanceCounterCategory("GPU Engine");
                var nomes = categoria.GetInstanceNames().Where(n => n.EndsWith("engtype_3D") || n.EndsWith("engtype_VideoEncode")).ToArray();
                var vivos = new HashSet<string>(nomes);
                foreach (var velho in motores.Keys.Where(k => !vivos.Contains(k)).ToList()) { motores[velho].Dispose(); motores.Remove(velho); }
                var soma3d = new Dictionary<string, float>(); var somaEnc = new Dictionary<string, float>();
                foreach (var n in nomes)
                {
                    PerformanceCounter pc;
                    if (!motores.TryGetValue(n, out pc))
                    {
                        pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                        motores[n] = pc; pc.NextValue(); continue; // a 1ª leitura é sempre 0
                    }
                    float v;
                    try { v = pc.NextValue(); } catch { continue; }
                    // "pid_123_luid_0x0_0xABCD_phys_0_eng_0_engtype_3D": soma por placa (luid) e por motor (eng)
                    int a = n.IndexOf("luid_"), b = n.IndexOf("_phys_");
                    int e = n.IndexOf("_eng_"), f = n.IndexOf("_engtype_");
                    if (a < 0 || b < 0 || e < 0 || f < 0) continue;
                    string chave = n.Substring(a, b - a) + n.Substring(e, f - e);
                    var alvo = n.EndsWith("engtype_3D") ? soma3d : somaEnc;
                    float s; alvo.TryGetValue(chave, out s); alvo[chave] = s + v;
                }
                Gpu = soma3d.Count > 0 ? (int)Math.Round(Math.Min(100, soma3d.Values.Max())) : 0;
                Enc = somaEnc.Count > 0 ? (int)Math.Round(Math.Min(100, somaEnc.Values.Max())) : 0;
            }
            catch { Gpu = -1; Enc = -1; }
        }
    }
}
