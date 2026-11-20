// Servidor de repasse (SFU) da Cloudflare Realtime: quem transmite manda a imagem UMA vez pro servidor
// e ele repassa pra cada pessoa (igual ao Discord). Sem isso, o PC de quem transmite manda uma cópia
// pra cada um e a internet/placa de vídeo dele não dá conta.
//
// Configurado na Vercel:
//  - CF_SFU_APP_ID e CF_SFU_APP_SECRET (painel da Cloudflare: Realtime > Serverless SFU)
//  - um contador grátis (Vercel > Storage > Upstash Redis), que cria KV_REST_API_URL e KV_REST_API_TOKEN
//
// TRAVA DO GRÁTIS: a Cloudflare dá 1.000 GB por mês. Quem assiste pelo servidor conta aqui o que recebeu
// (com folga pros cabeçalhos da rede). Chegando em LIMITE_GB no mês, o servidor não abre mais sessão e
// quem transmite volta pro modo direto até virar o mês. Sem o contador configurado, o servidor fica DESLIGADO
// (na dúvida, não usa: assim nunca cobra).
// O segredo fica só aqui: o navegador pede cada passo por esta função.
const BASE = 'https://rtc.live.cloudflare.com/v1/apps/';
const LIMITE_GB = 850;            // de 1.000 grátis: sobra folga pro que já está rolando e pro que não foi contado
const FOLGA_REDE = 1.15;          // cabeçalhos IP/UDP/RTP e reenvios não aparecem no que o navegador mede
const OPS = {
  nova:       { metodo: 'POST', caminho: () => 'sessions/new' },
  faixas:     { metodo: 'POST', caminho: s => `sessions/${s}/tracks/new` },
  renegociar: { metodo: 'PUT',  caminho: s => `sessions/${s}/renegotiate` },
  atualizar:  { metodo: 'PUT',  caminho: s => `sessions/${s}/tracks/update` },
  fechar:     { metodo: 'PUT',  caminho: s => `sessions/${s}/tracks/close` },
};
const mes = () => new Date().toISOString().slice(0, 7); // AAAA-MM (UTC)

async function kv(comando) {
  const { KV_REST_API_URL: url, KV_REST_API_TOKEN: tok } = process.env;
  const r = await fetch(url.replace(/\/$/, '') + '/' + comando.map(encodeURIComponent).join('/'), { headers: { Authorization: `Bearer ${tok}` } });
  if (!r.ok) throw new Error('kv ' + r.status);
  return (await r.json()).result;
}
async function usoGB() { return (+(await kv(['get', 'sfu-bytes:' + mes()])) || 0) / 1e9; }

module.exports = async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  const origem = req.headers.origin || req.headers.referer || '';
  if (origem && !origem.includes(req.headers.host)) return res.status(403).end();

  const env = process.env;
  if (!env.CF_SFU_APP_ID || !env.CF_SFU_APP_SECRET || !env.KV_REST_API_URL || !env.KV_REST_API_TOKEN) return res.status(204).end();

  if (req.method === 'GET') {
    try {
      const gb = await usoGB();
      return res.status(200).json({ ok: gb < LIMITE_GB, gb: Math.round(gb * 10) / 10, limite: LIMITE_GB });
    } catch { return res.status(200).json({ ok: false, erro: 'contador' }); }
  }
  if (req.method !== 'POST') return res.status(405).end();

  let corpo = req.body;
  if (typeof corpo === 'string') try { corpo = JSON.parse(corpo); } catch { corpo = null; }
  if (!corpo) return res.status(400).json({ erro: 'corpo' });

  // quem assiste pelo servidor conta o que recebeu (só soma; no máximo 3 GB por aviso)
  if (corpo.op === 'uso') {
    const bytes = Math.round(Math.min(3e9, Math.max(0, +corpo.bytes || 0)) * FOLGA_REDE);
    try { if (bytes) await kv(['incrby', 'sfu-bytes:' + mes(), String(bytes)]); return res.status(200).json({ ok: true }); }
    catch { return res.status(502).json({ erro: 'contador' }); }
  }

  const op = OPS[corpo.op];
  if (!op) return res.status(400).json({ erro: 'op' });
  const sessao = String(corpo.sessao || '');
  if (corpo.op !== 'nova' && !/^[a-f0-9]{16,64}$/i.test(sessao)) return res.status(400).json({ erro: 'sessao' });

  // sessão nova só abre se o mês ainda está dentro do grátis (e se deu pra conferir)
  if (corpo.op === 'nova') {
    try { if (await usoGB() >= LIMITE_GB) return res.status(429).json({ erro: 'limite' }); }
    catch { return res.status(503).json({ erro: 'contador' }); }
  }

  try {
    const r = await fetch(BASE + env.CF_SFU_APP_ID + '/' + op.caminho(sessao), {
      method: op.metodo,
      headers: { Authorization: `Bearer ${env.CF_SFU_APP_SECRET}`, 'Content-Type': 'application/json' },
      body: corpo.op === 'nova' ? undefined : JSON.stringify(corpo.dados || {}),
    });
    const texto = await r.text();
    res.status(r.status).setHeader('Content-Type', 'application/json');
    return res.send(texto || '{}');
  } catch (e) {
    return res.status(502).json({ erro: 'cloudflare' });
  }
};
