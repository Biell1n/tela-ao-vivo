// Entrega os servidores de retransmissão (TURN) pro site. Duas formas, configuradas na Vercel:
//  1) Usuário e senha fixos (ExpressTURN, Metered etc.):
//     TURN_URLS (separadas por vírgula), TURN_USERNAME, TURN_CREDENTIAL
//  2) Cloudflare Realtime TURN (credencial temporária): CF_TURN_KEY_ID e CF_TURN_API_TOKEN
// Sem nada configurado, responde 204 e o site segue com os servidores padrão do PeerJS.
module.exports = async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');

  // só entrega credencial pra páginas do próprio site
  const origem = req.headers.origin || req.headers.referer || '';
  if (origem && !origem.includes(req.headers.host)) return res.status(403).end();

  const env = process.env;
  if (env.TURN_URLS && env.TURN_USERNAME && env.TURN_CREDENTIAL) {
    const base = env.TURN_URLS.split(',').map(u => u.trim()).filter(Boolean)
      .map(u => /^(turns?|stun):/.test(u) ? u : 'turn:' + u);
    // junto de cada turn: em UDP, oferece também por TCP (rede que bloqueia UDP)
    const urls = [...new Set(base.flatMap(u =>
      /^turn:/.test(u) && !/transport=/.test(u) ? [u, u + '?transport=tcp'] : [u]))];
    return res.status(200).json({ iceServers: [{ urls, username: env.TURN_USERNAME, credential: env.TURN_CREDENTIAL }] });
  }

  const id = env.CF_TURN_KEY_ID, token = env.CF_TURN_API_TOKEN;
  if (!id || !token) return res.status(204).end();
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
