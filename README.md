# Tela ao Vivo

Compartilhe a tela com áudio direto pelo navegador, sem configurar porta nem firewall.

- **Quem transmite:** Transmitir → escolhe **Tela inteira / Janela / Aba** → escolhe o áudio → **Começar a transmitir** → manda o código ou o link.
- **Quem assiste:** digita o código na página inicial, ou abre o link (`?c=codigo`).

## Áudio

| Opção | O que vai junto |
|---|---|
| **Cabo virtual, sem o Discord** (recomendado) | Você escolhe app por app o que entra (no Windows, em "Volume do app e preferências de dispositivo"), passando pelo Voicemeeter ou VB-Cable. O passo a passo está na própria página. |
| **Som do Windows inteiro** (tela inteira) | Tudo, inclusive a voz da call. |
| **Som só da janela** (janela) | Só o app escolhido, nas versões do Chrome que suportam áudio de janela. Se não vier som, use o cabo virtual. |
| **Som da aba** (aba) | Só o som da aba do Chrome. |
| **Sem áudio** | Só a imagem. |

O navegador não consegue tirar um programa específico (o Discord) do "som do Windows inteiro"; por isso a separação é feita no cabo virtual.

Atalhos no player: `F` tela cheia · `M` mudo · `↑↓` volume · `S` estatísticas · `P` janela flutuante.

Feito em uma página só (`index.html`), com WebRTC + PeerJS. Funciona no Chrome e no Edge.
