// Servidor de repasse (SFU) da Cloudflare Realtime: quem transmite manda a imagem UMA vez pro servidor
// e ele repassa pra cada pessoa (igual ao Discord). Sem isso, o PC de quem transmite manda uma cópia
// pra cada um e a internet/placa de vídeo dele não dá conta.
// Configurado na Vercel: CF_SFU_APP_ID e CF_SFU_APP_SECRET (painel da Cloudflare: Realtime > Serverless SFU).
// Sem as duas, responde 204 e o site segue só com a ligação direta.
// O segredo fica só aqui: o navegador pede cada passo por esta função.
const BASE = 'https://rtc.live.cloudflare.com/v1/apps/';
const OPS = {
  nova:       { metodo: 'POST', caminho: () => 'sessions/new' },
  faixas:     { metodo: 'POST', caminho: s => `sessions/${s}/tracks/new` },
  renegociar: { metodo: 'PUT',  caminho: s => `sessions/${s}/renegotiate` },
  atualizar:  { metodo: 'PUT',  caminho: s => `sessions/${s}/tracks/update` },
  fechar:     { metodo: 'PUT',  caminho: s => `sessions/${s}/tracks/close` },
};

module.exports = async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  const origem = req.headers.origin || req.headers.referer || '';
  if (origem && !origem.includes(req.headers.host)) return res.status(403).end();

  const { CF_SFU_APP_ID: app, CF_SFU_APP_SECRET: segredo } = process.env;
  if (!app || !segredo) return res.status(204).end();
  if (req.method === 'GET') return res.status(200).json({ ok: true }); // só pra saber se está ligado
  if (req.method !== 'POST') return res.status(405).end();

  let corpo = req.body;
  if (typeof corpo === 'string') try { corpo = JSON.parse(corpo); } catch { corpo = null; }
  const op = corpo && OPS[corpo.op];
  if (!op) return res.status(400).json({ erro: 'op' });
  const sessao = String(corpo.sessao || '');
  if (corpo.op !== 'nova' && !/^[a-f0-9]{16,64}$/i.test(sessao)) return res.status(400).json({ erro: 'sessao' });

  try {
    const r = await fetch(BASE + app + '/' + op.caminho(sessao), {
      method: op.metodo,
      headers: { Authorization: `Bearer ${segredo}`, 'Content-Type': 'application/json' },
      body: corpo.op === 'nova' ? undefined : JSON.stringify(corpo.dados || {}),
    });
    const texto = await r.text();
    res.status(r.status).setHeader('Content-Type', 'application/json');
    return res.send(texto || '{}');
  } catch (e) {
    return res.status(502).json({ erro: 'cloudflare' });
  }
};
