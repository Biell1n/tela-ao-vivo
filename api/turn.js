// Entrega os servidores de retransmissão (TURN) pro site. Configurados na Vercel:
//  1) Cloudflare Realtime TURN (perto: tem servidor em São Paulo): CF_TURN_KEY_ID e CF_TURN_API_TOKEN
//  2) Usuário e senha fixos (ExpressTURN, Metered etc.): TURN_URLS (separadas por vírgula), TURN_USERNAME, TURN_CREDENTIAL
// Com os dois, a Cloudflare vem primeiro (o navegador prefere o primeiro da lista) e o outro fica de reserva.
// Sem nada configurado, responde 204 e o site segue com os servidores padrão do PeerJS.
module.exports = async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');

  // só entrega credencial pra páginas do próprio site
  const origem = req.headers.origin || req.headers.referer || '';
  if (origem && !origem.includes(req.headers.host)) return res.status(403).end();

  const env = process.env;
  const iceServers = [];

  if (env.CF_TURN_KEY_ID && env.CF_TURN_API_TOKEN) {
    try {
      const r = await fetch(`https://rtc.live.cloudflare.com/v1/turn/keys/${env.CF_TURN_KEY_ID}/credentials/generate-ice-servers`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${env.CF_TURN_API_TOKEN}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ ttl: 86400 }),
      });
      if (r.ok) {
        const j = await r.json();
        const lista = Array.isArray(j.iceServers) ? j.iceServers : [j.iceServers];
        // a Cloudflare recomenda tirar a porta 53: navegador costuma travar nela
        for (const s of lista.filter(Boolean)) {
          const urls = [].concat(s.urls).filter(u => !/:53(\?|$)/.test(u));
          if (urls.length) iceServers.push({ ...s, urls });
        }
      }
    } catch { /* Cloudflare fora: segue com a reserva */ }
  }

  if (env.TURN_URLS && env.TURN_USERNAME && env.TURN_CREDENTIAL) {
    const base = env.TURN_URLS.split(',').map(u => u.trim()).filter(Boolean)
      .map(u => /^(turns?|stun):/.test(u) ? u : 'turn:' + u);
    // junto de cada turn: em UDP, oferece também por TCP (rede que bloqueia UDP)
    const urls = [...new Set(base.flatMap(u =>
      /^turn:/.test(u) && !/transport=/.test(u) ? [u, u + '?transport=tcp'] : [u]))];
    iceServers.push({ urls, username: env.TURN_USERNAME, credential: env.TURN_CREDENTIAL });
  }

  if (!iceServers.length) return res.status(204).end();
  res.status(200).json({ iceServers });
};
