# Ícones do Streamline

Ícones do mod (não do jogo), baixados pelo Streamline MCP. Plano gratuito: crédito ao Streamline pela licença
<https://home.streamlinehq.com/license-free>. O PNG vai embutido na DLL em base64
(`src/HellClockCoop/Interface/IconesStreamline.cs`); o SVG fica como fonte para trocar o ícone.

| Arquivo | Ícone | Set | Uso |
|---|---|---|---|
| `coroa.png` / `coroa.svg` | Crown (`ico_suzNHVVQsGrCEgaN`) | Core Solid - Free | marca o host no painel de grupo (0.27.0); PNG 64x64 branco, tingido de dourado no código |
| `raio.png` / `raio.svg` | Flash 1 (`ico_vMPiG1HZNSqWivN3`) | Core Solid - Free | teleporte até o aliado no painel de grupo (0.30.0, opção A "raio + aura"; substitui o Location Target 1 da 0.29.0); PNG 64x64 branco, tingido na cor do aliado no código |

Para trocar: baixe outro ícone do mesmo Set em PNG 64x64 branco, substitua o arquivo, gere o base64
(`base64 -w0 icons/streamline/coroa.png`) e cole na constante dele em `IconesStreamline` (`CoroaPng`, `TpPng`).
