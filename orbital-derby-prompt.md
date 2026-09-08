# Prompt — ORBITAL DERBY (front simulável)

Cole isto no chat da IDE. Está escrito como instrução direta para o agente.

---

## Contexto

Estou construindo um autorama físico de 2 pistas. Cada carrinho carrega uma nave espacial impressa em 3D. Cada jogador tem **um botão de acelerador** (segurar = anda, soltar = para) e **um botão de ação**. Sensores de checkpoint na pista e LEDs entram depois.

Nesta etapa quero **só o front-end, rodando 100% simulado no PC**, sem nenhuma dependência de GPIO. O jogo precisa ser jogável hoje com teclado, e a troca para hardware real depois deve ser feita trocando uma classe de driver, sem tocar na lógica de jogo.

Alvo de execução: Python 3.11+, `pygame-ce`, tela 1280x720 (com suporte a fullscreen), 60 FPS estáveis num Raspberry Pi 4 ou mini PC.

## Identidade (não usar IP de terceiros)

Universo original. Nada de nomes, silhuetas ou elementos reconhecíveis de franquias existentes.

- **Estação ÍRIS-9**: estação de mineração esférica no centro da pista, com uma abertura em diafragma que abre e fecha. Placas hexagonais, sem trincheira equatorial, sem canhão planetário. Ela não é decoração — é uma ameaça cíclica (ver mecânica).
- **Pista**: um anel orbital de detritos ao redor da ÍRIS-9.
- **Jogador 1 — ÍON**: casco angular, cor `#2FD6FF`.
- **Jogador 2 — ÍGNIS**: casco alongado, cor `#FFA02E`.
- Paleta base: fundo `#04060D`, painéis `#0C1220`, texto `#E6EDF7`, roxo da estação `#7C5CFF`.
- Naves desenhadas em polígonos vetoriais no próprio pygame. Sem assets externos.

## Regra de ouro da arquitetura

O único atuador físico real é o **PWM de cada pista** (0.0 a 1.0). Toda mecânica de jogo precisa terminar em um valor de PWM. Se um poder não puder ser expresso como "aumenta teto de PWM", "zera PWM" ou "reduz PWM", ele não entra no jogo.

Implemente:

- `InputDriver` (interface): retorna por frame `{p1_throttle: bool, p1_action: bool, p2_throttle: bool, p2_action: bool}`.
  - `KeyboardDriver` — implementar agora.
  - `GpioDriver` — apenas stub com o mapa de pinos comentado, sem importar bibliotecas de hardware.
- `OutputBus`: a cada frame recebe `set_lane(lane, pwm, effect_tag)` e guarda o estado. Implementar `NullOutput` (só armazena). O HUD mostra os valores de PWM ao vivo em rodapé, porque esse é o contrato que o back-end vai consumir.
- Detecção de borda no botão de ação (não usar estado contínuo).

## Mecânica do acelerador (calor)

- Segurar acelera; soltar desacelera rápido até parar.
- `speed` em voltas/s. `ACCEL = 0.25`, `DECEL = 0.35`, teto base `CAP = 0.16` (≈ 6,2 s por volta).
- Barra de calor: sobe enquanto acelera (0 → 1 em 4,5 s), desce quando solta (1 → 0 em 3,0 s).
- Calor cheio = **superaquecimento**: PWM zerado por 1,6 s, calor volta em 0,75 e drena.
- Consequência intencional: é impossível segurar o botão a volta inteira. O jogador tem que modular. Isso é o que dá profundidade a um jogo de um botão.

## Roleta nos checkpoints (é uma aposta, não um brinde)

- 3 checkpoints por volta, em `t = 0.12`, `0.45`, `0.78`, janela de ±0.03.
- Dentro da janela e sem item no slot: botão de ação inicia a roleta.
- **Durante o giro (1,7 s) o acelerador é ignorado.** O carrinho desacelera. Girar custa velocidade real; esse é o trade-off.
- Fora da janela e com item no slot: botão de ação usa o item.
- Slot único. Sem estoque.

## Itens e efeito em PWM

| Item | Efeito | Peso |
|---|---|---|
| Impulso | Teto de PWM ×1,7 por 3 s e calor congelado | 22 |
| Pulso | PWM do adversário = 0 por 1,2 s | 18 |
| Rajada | Só acerta se o adversário estiver a menos de 0,25 volta à frente; teto ×0,45 por 2,5 s. Erra e queima o item | 20 |
| Escudo | Absorve o próximo efeito recebido | 15 |
| Fantasma | Imune à próxima varredura da ÍRIS-9 | 10 |
| Sucata | Nada. É o que torna a aposta uma aposta | 15 |

Quem está atrás em voltas recebe leve viés nos pesos (catch-up). Deixe o fator isolado numa constante, para eu poder desligar e testar sem ele.

## Evento ÍRIS-9

- Barra de carga enche em 25 s. Aos 80% começa aviso visual e sonoro-visual (pulsação da abertura).
- Ao disparar, varre a órbita por 1,5 s.
- **Zona de sombra** fixa em `t ∈ [0.60, 0.75]`, marcada na pista. Quem estiver fora dela durante a varredura leva teto ×0,45 por 2,5 s, salvo Escudo ou Fantasma.
- Isso força uma decisão de ritmo: correr para alcançar a sombra ou aceitar o dano.

## Pista e posição

- Posição normalizada `t ∈ [0,1)` por nave. Volta conta ao cruzar de `>0.9` para `<0.1`.
- Traçado paramétrico (elipse com modulação `r = 1 + 0.075·cos(3a)`), duas faixas com deslocamento pela normal da curva.
- Vitória: 5 voltas.
- **Nesta etapa a pista é inteiramente simulada.** Não existe sensor: `t` avança apenas pela integração de `speed × dt`, e as naves são desenhadas e movidas sobre o traçado na tela. O jogo tem que ser completável do início ao fim sem nenhum hardware conectado.
- Anote em comentário: no hardware real, `t` entre checkpoints continuará sendo **estimado** por integração e apenas corrigido a cada sensor. O front já deve tratar `t` como estimativa corrigível, expondo um método `sync_position(lane, t)`.
- As posições dos checkpoints (`0.12`, `0.45`, `0.78`) e o comprimento da volta são **constantes de configuração**, não números fixos no código. Elas terão que ser remedidas para bater com a posição física real dos sensores no autorama, senão a nave na tela dessincroniza do carrinho real. O mesmo vale para a zona de sombra da ÍRIS-9.

## Telas

`ATRAÇÃO → CONTAGEM → CORRIDA → RESULTADO`, com reinício por tecla.

## HUD

- Painel por jogador: voltas, barra de calor, slot de item, posição.
- Barra de carga da ÍRIS-9 no centro.
- Rodapé de telemetria com PWM alvo de cada pista e efeitos ativos.
- Mensagens curtas, em frase, sem caixa alta decorativa.

## Teclado (simulação)

- ÍON: `A` acelerador, `S` ação.
- ÍGNIS: `L` acelerador, `K` ação.
- `Espaço` inicia, `R` reinicia, `F11` fullscreen, `Esc` sai.

## Critérios de aceite

1. Roda com `python main.py` sem hardware e sem assets externos.
2. Dois jogadores completam 5 voltas com todos os itens funcionando.
3. Nenhuma referência a GPIO fora do stub.
4. Toda alteração de velocidade passa pelo `OutputBus` e aparece na telemetria.
5. 60 FPS estáveis.

## Não faça

- Não invente poderes que não sejam redutíveis a PWM.
- Não use imagens, fontes ou sons externos.
- Não misture leitura de hardware com a lógica de jogo.
- Não use elementos visuais derivados de franquias existentes.

## Entregue

Estrutura de módulos (`main.py`, `game/`, `drivers/`), código comentado em português, e um `README.md` com como rodar e onde plugar o hardware depois.
