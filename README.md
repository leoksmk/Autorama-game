# ORBITAL DERBY

Front-end de um autorama de 2 pistas com tema espacial original. Nesta etapa
roda **100% simulado no PC**: teclado na entrada, pista integrada por software,
nenhuma dependência de GPIO. A corrida é completável do início ao fim sem
nenhum hardware conectado.

## Como rodar

```bash
pip install pygame-ce      # pygame 2.x também serve
python main.py
```

Testado com Python 3.12 e pygame 2.6.1. Janela de 1280x720, 60 FPS.

Se o terminal mostrar `no fast renderer available` e o jogo ficar lento (máquina
sem aceleração gráfica), rode sem o modo escalado:

```bash
ORBITAL_NO_SCALED=1 python main.py
```

## Controles

| | Acelerador (martele) | Ação |
| --- | --- | --- |
| ÍON (pista 1) | `A` | `S` |
| ÍGNIS (pista 2) | `L` | `K` |

`Espaço` começa · `R` reinicia · `F11` tela cheia · `Esc` sai · `F3` mostra FPS

## Como se joga

**O acelerador é de martelar, não de segurar.** Segurar o botão não faz
absolutamente nada. O que vira velocidade é a **frequência dos apertos**:

| Ritmo | Esforço | Volta |
| --- | --- | --- |
| 2 cliques/s | 25% | arrastando |
| 4 cliques/s | 50% | ~12 s |
| 8 cliques/s | 100% | ~6,2 s |

Acima de 8 cliques/s não adianta: o esforço satura. Parar de clicar corta o
motor em 0,6 s.

**Mas martelar forte esquenta.** Acima de 55% de esforço (≈4,4 cliques/s) o
calor sobe, e cheio corta o motor por 1,6 s. Ritmo abaixo disso é sustentável a
corrida inteira. É a mesma decisão de sempre — modular em vez de cravar — só
que agora o polegar paga por ela.

**A caixa de item tem hora.** Ao cruzar um dos três checkpoints com o slot
vazio, a caixa aparece na tela e fica aberta por **0,8 s**, com o seu botão
pulsando dentro dela. Não apertou nesse tempo, a chance passa.

**O giro não para a nave.** Apertar a ação não interrompe nada: a caixa cicla
por 1,5 s enquanto o carrinho continua andando, e o jogador segue martelando o
acelerador. Usar o item depois também não para.

Como girar não custa mais velocidade, o risco da caixa é outro: **uma das
quatro faces não dá nada** (18% dos giros). É o que impede que apertar em todo
checkpoint seja uma jogada sem contrapartida.

**Com item no slot, a ação usa o item a qualquer momento** — não precisa estar
em checkpoint nenhum. São três poderes:

| Face | Efeito | Peso |
| --- | --- | --- |
| Tiro | Deixa o adversário lento: teto de PWM ×0,45 por 2,5 s | 34 |
| Bomba | Para o adversário: PWM zerado por 2 s | 26 |
| Escudo | Bloqueia o próximo ataque recebido | 22 |
| Nada | A caixa veio vazia | 18 |

**Tiro e bomba só pegam de perto** — menos de 0,12 volta, medido pelo caminho
mais curto da pista, à frente ou atrás. Fora do alcance o ataque erra e o item
queima. É isso que obriga a escolher a hora em vez de apertar assim que a caixa
entrega.

**Tiro e bomba levam tempo para chegar.** O efeito só é aplicado quando o
projétil alcança o alvo (0,38 s e 0,55 s), não no disparo — então dá para
levantar o Escudo com a bomba já no ar. O impacto e o bloqueio acontecem sobre
a pista, na nave: explosão, tremor e um marcador curto em cima dela.

**A ÍRIS-9 é só cenário por enquanto.** A varredura está desligada em
`IRIS_EVENTO_ATIVO = False`; o maquinário continua em `station.py` e volta com
um `True`. Com ele ligado, a zona de sombra reaparece marcada na pista e a
barra de carga volta ao HUD, sem mais nenhuma mudança de código.

Vence quem completar 5 voltas.

## Arquitetura

A regra que organiza tudo: **o único atuador físico é o PWM de cada pista**
(0.0 a 1.0). Toda mecânica termina em um valor de PWM. Um poder que não seja
"aumenta o teto", "reduz o teto" ou "zera" não entra no jogo.

```text
main.py                 ponto de entrada; escolhe os drivers
game/
  config.py             TODAS as constantes de mundo (é aqui que se calibra)
  track.py              traçado paramétrico, t normalizado -> pixels
  ship.py               física, calor e cálculo do PWM de uma pista
  items.py              roleta, pesos, catch-up
  station.py            ÍRIS-9: carga, aviso, varredura
  race.py               orquestra tudo; único ponto que escreve no OutputBus
  render.py             pista, estação e naves em polígonos vetoriais
  effects.py            animações: partículas, ondas, projéteis, anúncios
  gfx.py                utilitários de desenho (cores, rotação, brilho)
  hud.py                painéis, caixa de item, telemetria, telas
  app.py                loop, máquina de telas, eventos de janela
drivers/
  input_driver.py       InputDriver, KeyboardDriver, GpioDriver (stub)
  output_bus.py         OutputBus, NullOutput, PwmOutput (stub)
```

O isolamento é real e verificável:

- Só `drivers/input_driver.py` lê teclado; só `game/app.py` conhece eventos de
  janela. Nenhum outro módulo de `game/` toca em entrada.
- `game/race.py` recebe o barramento por injeção. Não sabe o que há do outro
  lado.
- O rodapé de telemetria lê do `OutputBus`, não das naves. Se um efeito não
  aparece lá, ele não chegou ao PWM — e portanto não seria sentido pelo
  carrinho físico.
- `effects.py` é só desenho: a Race chama os emissores, mas nada ali altera o
  estado do jogo. Apagar o arquivo deixaria a corrida funcionando igual, sem
  animação — que é o teste de que a separação está certa. Por isso a `Race`
  aceita rodar sem ele (`Race(bus)` usa um objeto nulo), e é assim que os
  testes headless funcionam.

Nenhum asset externo: naves, estação e pista são polígonos desenhados no
pygame, e a fonte é a embutida da biblioteca.

## Onde plugar o hardware

Três pontos, nesta ordem.

### 1. Entrada — `drivers/input_driver.py`

`GpioDriver` já tem o mapa de pinos pretendido em comentário. Implemente
`read()` devolvendo os mesmos quatro booleanos e troque em `main.py`:

```python
from drivers.input_driver import GpioDriver
entrada = GpioDriver()
```

Debounce é obrigatório, e agora ainda mais: o acelerador é lido pela BORDA de
subida, então um botão que chacoalha vira cliques fantasmas e acelera sozinho.
`ButtonEdge` já faz a detecção de borda dos quatro botões, e
`CLIQUE_INTERVALO_MIN` limita a 25 Hz o que a lógica aceita como ritmo — mas
nenhum dos dois substitui um debounce decente no driver.

### 2. Saída — `drivers/output_bus.py`

`PwmOutput` é o stub. Implemente `set_lane(lane, pwm, effect_tag)` escrevendo no
controlador e troque em `main.py`:

```python
from drivers.output_bus import PwmOutput
saida = PwmOutput()
```

Se os motores tiverem zona morta (abaixo de ~0,25 de duty o carrinho não sai do
lugar), o remapeamento pertence a essa classe. O jogo continua raciocinando em
0..1 lineares.

### 3. Sensores de checkpoint — `Race.sync_position(lane, t)`

Hoje a posição `t` vem só da integração de `speed × dt`. **Com o hardware ela
continuará vindo da integração entre sensores** — o sensor não mede posição
contínua, só reancora quando o carrinho passa. Por isso `t` já é tratado como
estimativa corrigível em todo o código.

Quando um sensor disparar, chame:

```python
race.sync_position(lane, t_daquele_sensor)
```

A contagem de volta é feita dentro do método, na virada de `>0.9` para `<0.1`.

**A janela da roleta já está modelada como o sensor funciona.** Ela não é uma
janela de posição ("estar perto do checkpoint"), e sim de tempo: a PASSAGEM
abre a janela, que dura `ROLETA_OPORTUNIDADE` segundos. Hoje a passagem é
detectada pela integração (`track.cruzou_checkpoint`); com o hardware, o pulso
do sensor entra por `sync_position` e abre exatamente a mesma janela. Nenhuma
regra de jogo muda.

## Calibração: o que precisa ser remedido

Isto não é opcional. Se estes valores não baterem com a pista física, a nave na
tela dessincroniza do carrinho real. Todos estão em `game/config.py`:

| Constante | O que é |
| --- | --- |
| `CHECKPOINTS` | Posição normalizada de cada sensor na volta. **Medir na pista.** |
| `ROLETA_OPORTUNIDADE` | Quantos segundos a roleta fica aberta após a passagem |
| `SOMBRA_INICIO` / `SOMBRA_FIM` | Trecho da zona de sombra. Só importa se `IRIS_EVENTO_ATIVO` voltar; aí **medir na pista** e marcar fisicamente. |
| `COMPRIMENTO_VOLTA_M` | Comprimento real da volta (só telemetria) |
| `PWM_BASE` | Duty que corresponde ao teto base de velocidade |

`PWM_BASE = 0.55` fica abaixo de 1.0 de propósito: é a margem que o Impulso
(×1,7) usa sem estourar. Se aumentar `PWM_BASE`, o Impulso satura em 1.0 e
deixa de fazer diferença.

## Ajustes de balanceamento

Em `game/config.py`:

- `CATCHUP_ATIVO = False` desliga o viés de catch-up da roleta, para testar o
  balanceamento cru. O fator fica isolado em `CATCHUP_BIAS`.
- `ACCEL`, `DECEL`, `CAP` — física do acelerador. `CAP = 0.16` dá ~6,2 s por
  volta no teto absoluto.
- `CALOR_SUBIDA` / `CALOR_DESCIDA` — a razão entre os dois define o ciclo de
  trabalho sustentável. Com 4,5 s de subida e 3,0 s de descida, dá para
  acelerar ~60% do tempo, o que põe a volta real perto de 10 s e uma corrida de
  5 voltas em torno de 1 minuto. **Se quiser corridas mais curtas, mexa aqui
  antes de mexer em `VOLTAS_PARA_VENCER`.**
- `IRIS_EVENTO_ATIVO = True` religa a varredura da ÍRIS-9 (e com ela a zona de
  sombra na pista e a barra de carga no HUD).
- `ROLETA_OPORTUNIDADE`, `ROLETA_GIRO`, `ROLETA_REVELACAO` — o ritmo da caixa.
  Aumentar a oportunidade deixa o jogo mais permissivo. O giro não custa mais
  velocidade, então alongá-lo só atrasa o prêmio.
- O peso de `"nada"` em `ITEM_PESOS` é o risco da caixa, e a única coisa que
  ainda faz girar ser uma decisão. Zerá-lo transforma cada checkpoint em item
  garantido.
- `TIRO_VOO` / `BOMBA_VOO` — tempo de voo dos ataques. Aumentar dá mais janela
  para o adversário reagir com o Escudo; é balanceamento, não animação.
- `TIRO_ALCANCE` / `BOMBA_ALCANCE` — o quanto o alvo pode estar longe e ainda
  ser acertado, em voltas. Aumentar torna os ataques quase automáticos.
- `CADENCIA_PLENA_HZ` — quantos cliques por segundo dão PWM cheio. Baixar
  facilita o jogo para quem tem o polegar lento; subir exige mais.
- `CALOR_LIMIAR` — a partir de que esforço o motor começa a esquentar. Define o
  ritmo que dá para manter a corrida inteira.
- `CLIQUE_TIMEOUT`, `CLIQUE_INTERVALO_MIN` — quando o motor corta por falta de
  apertos, e o teto anti-repique. No hardware, `CLIQUE_INTERVALO_MIN` é a
  primeira linha de defesa contra um botão que chacoalha.
- `ITEM_CORES` — a cor de cada item vale para a caixa, o slot e a animação de
  uso ao mesmo tempo. Mexer em uma muda as três, de propósito.

Se as animações pesarem em máquina fraca, `effects.MAX_PARTICULAS` limita o
total de partículas vivas (padrão 320).
