# Tela ao Vivo

Compartilhe a tela com áudio direto pelo navegador, sem configurar porta nem firewall.

- **Quem transmite:** Transmitir → escolhe **Tela inteira / Janela / Aba** → escolhe o áudio → **Começar a transmitir** → manda o código ou o link.
- **Câmera:** escolha "Câmera" pra transmitir só ela (com microfone), ou marque "Câmera por cima da tela" (estilo OBS) e arraste a câmera na prévia pra posicionar; dá pra mudar tamanho, retângulo/círculo e esconder ao vivo.
- **Grupo:** "Criar grupo" na página inicial, escolhe um nome e manda o link. Todo mundo que entra pode compartilhar a tela (várias ao mesmo tempo), em grade ou com uma em destaque; miniaturas pedem 360p pra poupar internet. O grupo continua aberto enquanto tiver alguém nele (se quem segura a sala sai, outro assume); fechar o site não tira ninguém do grupo. Só quem criou pode expulsar (chave guardada no navegador). Tem chat do grupo e reações com emoji que sobem na tela (dá pra esconder as reações dos outros); quem entra depois recebe as últimas mensagens.
- **Quem assiste:** digita o código na página inicial, ou abre o link (`?c=codigo`).
- **Qualidade de quem transmite:** Leve (720p · 30 fps · 4 Mbps), Moderado (1080p · 60 fps · 12 Mbps), Alto (2K · 60 fps · 16 Mbps), Máximo (4K · 60 fps · 20 Mbps) ou Personalizado (resolução, fps de 24 a 144 e taxa de 1 a 50 Mbps). Dá pra trocar com a transmissão rolando.
- **Quem assiste** escolhe a qualidade que recebe (até 4K), a super resolução (rápida ou por IA, só no PC), o volume extra e, no grupo, o modo cinema (tecla T) e a luz ambiente.

## Servidor de repasse (opcional)

Com 2 ou mais pessoas assistindo na qualidade cheia, quem transmite manda a imagem uma vez só pro servidor da Cloudflare (Realtime SFU) e ele repassa pra cada um, igual ao Discord. Miniaturas do grupo e quem está com qualidade menor continuam direto. Pra ligar: crie um app em Cloudflare > Realtime > Serverless SFU e coloque `CF_SFU_APP_ID` e `CF_SFU_APP_SECRET` nas variáveis da Vercel. Grátis até 1.000 GB por mês (somando com o TURN), depois US$ 0,05 por GB.

## Áudio

| Opção | O que vai junto |
|---|---|
| **Pelo programa Tela ao Vivo Áudio** (recomendado) | Programinha de Windows (`TelaAoVivoAudio.exe`, baixado no próprio site): manda tudo menos o Discord, ou só os apps marcados numa lista. Código em `app-audio/`. |
| **Cabo virtual** | Você escolhe app por app o que entra (no Windows, em "Volume do app e preferências de dispositivo"), passando pelo Voicemeeter ou VB-Cable. O passo a passo está na própria página. |
| **Som do Windows inteiro** (tela inteira) | Tudo, inclusive a voz da call. |
| **Som só da janela** (janela) | Só o app escolhido, nas versões do Chrome que suportam áudio de janela. Se não vier som, use o cabo virtual. |
| **Som da aba** (aba) | Só o som da aba do Chrome. |
| **Sem áudio** | Só a imagem. |

O navegador não consegue tirar um programa específico (o Discord) do "som do Windows inteiro"; por isso a separação é feita no cabo virtual.

Atalhos no player: `F` tela cheia · `M` mudo · `↑↓` volume · `S` estatísticas · `P` janela flutuante.

Feito em uma página só (`index.html`), com WebRTC + PeerJS. Funciona no Chrome e no Edge.
