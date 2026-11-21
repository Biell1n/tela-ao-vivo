// Desempenho: mede quanto o processador e a placa de vídeo estão sendo usados (os mesmos números do
// Gerenciador de Tarefas) e manda pro site, pra quem assiste saber de onde vem a queda de fps.
//  - gpu = o motor 3D da placa (o que o jogo usa). Perto de 100% = placa no limite, e o Windows não consegue
//          capturar a tela a tempo.
//  - enc = o motor de compressão de vídeo da placa (NVENC/AMF/QuickSync). Acima de 0 = alguém está comprimindo
//          vídeo pela placa (o navegador, quando a aceleração está funcionando).
// Leve de propósito (não pode tirar desempenho do jogo): UMA leitura da categoria inteira por vez, a cada 3 s,
// numa thread de prioridade baixa.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TelaAoVivoAudio
{
    class Desempenho
    {
        PerformanceCounter cpu;
        PerformanceCounterCategory categoria;
        Dictionary<string, CounterSample> antes = new Dictionary<string, CounterSample>();
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
                var dados = categoria.ReadCategory()["Utilization Percentage"];
                var agora = new Dictionary<string, CounterSample>();
                var soma3d = new Dictionary<string, float>(); var somaEnc = new Dictionary<string, float>();
                foreach (InstanceData inst in dados.Values)
                {
                    string n = inst.InstanceName;
                    bool e3d = n.EndsWith("engtype_3D"), enc = n.EndsWith("engtype_VideoEncode");
                    if (!e3d && !enc) continue;
                    agora[n] = inst.Sample;
                    CounterSample velho;
                    if (!antes.TryGetValue(n, out velho)) continue; // processo novo: conta na próxima
                    float v = CounterSample.Calculate(velho, inst.Sample);
                    // "pid_123_luid_0x0_0xABCD_phys_0_eng_0_engtype_3D": soma por placa (luid) e por motor (eng)
                    int a = n.IndexOf("luid_"), b = n.IndexOf("_phys_"), e = n.IndexOf("_eng_"), f = n.IndexOf("_engtype_");
                    if (a < 0 || b < 0 || e < 0 || f < 0) continue;
                    string chave = n.Substring(a, b - a) + n.Substring(e, f - e);
                    var alvo = e3d ? soma3d : somaEnc;
                    float s; alvo.TryGetValue(chave, out s); alvo[chave] = s + v;
                }
                bool primeira = antes.Count == 0;
                antes = agora;
                if (primeira) return;
                Gpu = (int)Math.Round(Math.Min(100, Maior(soma3d)));
                Enc = (int)Math.Round(Math.Min(100, Maior(somaEnc)));
            }
            catch { Gpu = -1; Enc = -1; }
        }

        static float Maior(Dictionary<string, float> d) { float m = 0; foreach (var v in d.Values) if (v > m) m = v; return m; }
    }
}
