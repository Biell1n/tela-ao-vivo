// Gera credenciais temporárias de TURN na Cloudflare (Realtime TURN, grátis até 1 TB/mês).
// Variáveis na Vercel: CF_TURN_KEY_ID e CF_TURN_API_TOKEN (Cloudflare › Realtime › TURN Server).
// Sem elas, responde 204 e o site segue com os servidores padrão do PeerJS.
module.exports = async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  const id = process.env.CF_TURN_KEY_ID, token = process.env.CF_TURN_API_TOKEN;
  if (!id || !token) return res.status(204).end();

  // só entrega credencial pra páginas do próprio site
  const origem = req.headers.origin || req.headers.referer || '';
  if (origem && !origem.includes(req.headers.host)) return res.status(403).end();

  try {
    const r = await fetch(`https://rtc.live.cloudflare.com/v1/turn/keys/${id}/credentials/generate-ice-servers`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ ttl: 86400 }),
    });
    if (!r.ok) return res.status(502).json({ erro: 'cloudflare ' + r.status });
    const j = await r.json();
    const lista = Array.isArray(j.iceServers) ? j.iceServers : [j.iceServers];
    // a Cloudflare recomenda tirar a porta 53: navegador costuma travar nela
    const iceServers = lista.filter(Boolean).map(s => ({
      ...s, urls: [].concat(s.urls).filter(u => !/:53(\?|$)/.test(u)),
    })).filter(s => s.urls.length);
    res.status(200).json({ iceServers });
  } catch (e) {
    res.status(502).json({ erro: String(e) });
  }
};
