# ORBITAL DERBY

Front-end de um autorama de 2 pistas com tema espacial original. O mesmo jogo
existe em duas versões:

- **3D — Godot 4 / C#** (`godot/`). A versão para o PC da pista: cena 3D
  inteira gerada em código e controles ESP32 ligados pela USB.
- **2D — Python / pygame** (raiz do repositório). Roda no navegador e é a
  referência das regras: o núcleo C# da versão 3D é conferido contra ela.
  No desktop também aceita os controles ESP32 (`python main.py`); no navegador
  não há porta COM, então lá é sempre teclado.

**Versão 2D no navegador: <https://leoksmk.github.io/Autorama-game/>** — a
exportação web do Godot não suporta C#, então o link continua sendo a 2D.

As duas rodam **sem hardware de pista**: a posição de cada carrinho é integrada
por software e o PWM vai para um barramento nulo que só alimenta a telemetria.

---

## Versão 3D (Godot)

### O que precisa

- Godot **4.7.2 .NET** (a edição "mono"; a edição comum não roda C#)
- .NET SDK 9
- Placa de vídeo com Vulkan. Medido numa RTX 2050 de notebook, qualidade alta,
  V-Sync ligado: 59–61 fps em 30 s de corrida de demonstração, sem queda.

### Como rodar

Dois cliques em `godot\jogar.bat`, ou pelo terminal:

```powershell
cd godot
.\jogar.bat
```

O `jogar.bat` compila e abre o jogo. Ele procura o Godot em
`%LOCALAPPDATA%\Programs\Godot\Godot_v4.7.2-stable_mono_win64\`; se estiver
instalado em outro lugar, defina a variável `GODOT` com o caminho do `.exe`.
O `godot` não está no PATH, então chamar `godot --path .` direto não funciona.
Sem o `.bat`, o caminho completo é:

```powershell
cd godot
dotnet build OrbitalDerby.csproj
& "$env:LOCALAPPDATA\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path .
```

Também dá para abrir `godot/project.godot` no editor do Godot e apertar F5.

Com o `.bat` as opções vão direto (`.\jogar.bat --p1=controle --p2=teclado`);
chamando o executável do Godot, elas vão depois de `--`:

| Opção | Efeito |
| --- | --- |
| `--p1=` / `--p2=` | entrada da nave: `teclado`, `controle` (ou `esp`, `serial`), `cpu` |
| `--demo` | CPU contra CPU, em loop |
| `--qualidade=` | `alta` (padrão), `media`, `baixa` |
| `--captura=<pasta>` | roteiro fixo que salva 12 capturas de tela e fecha |
| `--captura=rajada:<pasta>` | 60 fotos seguidas na câmera de perseguição |
| `--sair-em=<s>` | fecha sozinho depois de *s* segundos |

### Teclas

| | Acelerador (martele) | Ação |
| --- | --- | --- |
| ÍON (pista 1) | `A` | `S` |
| ÍGNIS (pista 2) | `L` | `K` |

`Espaço` começa · `R` reinicia · `C` troca a câmera · `Q` troca a qualidade ·
`M` muta o som · `F11` tela cheia · `F3` FPS ·
`Esc` sai

Na tela de abertura, **`1` e `2` trocam a entrada de cada nave** entre teclado,
controle ESP e CPU. A escolha fica salva em
`%APPDATA%\Godot\app_userdata\Orbital Derby\controles.cfg`.

Câmeras: **transmissão** (fora da pista, junto de quem lidera, com a estação ao
fundo; abre o quadro quando as naves se afastam), **visão geral** (quase de
cima) e **perseguição** (atrás do líder).

### Qualidade

| | O que liga |
| --- | --- |
| alta | névoa volumétrica, reflexos (SSR), oclusão (SSAO), sombra do sol em 4 cascatas (8192), MSAA 4x, pedras com sombra |
| média | sem névoa volumétrica, 2 cascatas (4096), MSAA 2x, pedras sem sombra |
| baixa | também sem SSR e SSAO, sem MSAA, luz da estação sem sombra |

### O visual

Nada é importado: naves, estação, pedras, pista, céu e partículas são malhas e
shaders gerados em código. O acabamento vem da iluminação — HDR com ACES,
bloom, sombras suaves, reflexos em tela, névoa volumétrica acesa pelo núcleo da
ÍRIS-9. É um visual **cinematográfico estilizado, não fotorrealista**: chegar
perto de foto exigiria modelos e texturas feitos por artista, que ficaram fora
por decisão do projeto.

Nomes das naves e marcadores de efeito ("atingido", "bloqueado"...) são
desenhados em 2D, presos à posição da nave na tela. Como texto 3D eles eram
borrados pelo desfoque de distância da câmera, porque texto transparente não
grava profundidade.

### O som

Também não é importado: **não existe um único arquivo de áudio no projeto**.
Tudo é sintetizado em C# quando o jogo abre — osciladores, envelopes, filtros
biquad e saturação, em `scripts/audio/`. O banco inteiro fica pronto em cerca
de meio segundo e, depois disso, a corrida não lê disco nem aloca áudio.

A escola dos **eventos** é a do som analógico de nave: grave primeiro (todo
impacto tem um seno abaixo de 90 Hz descendo), nada de tom puro (passam por
filtro ressonante e distorção) e atraso curto como metal (um eco de 5 ms não
soa como eco, soa como cabo de aço ressoando — é assim que o disparo ganha
corpo sem sample nenhum). São técnicas, não citações: a identidade sonora é do
Orbital Derby, como a paleta.

**O motor é a peça central** e segue regras opostas, de propósito. Ele é
sintetizado ao vivo, amostra a amostra, e não é um loop com o pitch esticado: o
acelerador aqui é de martelar, então cada aperto empurra a afinação e o volume
por uns 60 ms, e o jogador ouve o próprio ritmo. O esforço abre o filtro, o
tiro abafa o motor (a nave soa *segurada*, não desistindo), o calor instabiliza
a afinação e o superaquecimento corta com uma tosse descendente. Custa cerca de
1 ms por quadro para as duas pistas — o laço das parciais para sozinho onde
elas deixam de contar, o que paga a maior parte da conta em marcha lenta.

O timbre do motor é **aditivo**: as parciais são somadas à mão sobre a
sub-oitava, nenhuma acima de 15 kHz. As pares são a série harmônica do motor;
as ímpares caem no meio dela e são o **rosnado**, que cresce com o esforço — o
motor abre a garganta quando o jogador força. Por cima vêm três **formantes**,
ressonâncias em frequências fixas que *não* seguem a afinação: é o que dá
caráter, porque conforme o motor sobe seus harmônicos atravessam os formantes e
o timbre se transforma sozinho. É o equivalente sintético de processar um bicho
de verdade, que era como se fazia som de nave na era analógica. Um atraso de
3 ms realimentado acrescenta o **casco** metálico, e acima de 62% de esforço
entra a **pós-combustão**: o motor muda de estado, não só de volume.

Do parado ao talo o centroide vai de 43 Hz a 1069 Hz e o volume sobe cinco
vezes — a transformação é o que dá emoção a martelar. Nada disso reintroduz
estridência, porque quatro regras são respeitadas: sem aliasing (band-limit em
15 kHz), sem ressonância estreita (passa-baixa com Q 0,7, formantes largos),
saturação *antes* do filtro e nenhum batimento rápido. No talo, 21% da energia
fica acima de 2 kHz, contra 44% de uma versão anterior que soava estridente.

As duas pistas têm vozes separadas — ÍON mais agudo à esquerda, ÍGNIS mais
grave à direita. Num autorama com dois jogadores lado a lado, isso deixa cada
um achar o próprio motor sem olhar a tela. O desvio de timbre dos filtros é
metade do desvio de afinação: aplicar os dois cheios afastava demais as naves e
deixava o ÍGNIS sem presença.

Três canais de mesa (`Motor`, `Sfx`, `Musica`) entram num master com limitador.
A trilha tem três camadas que tocam sempre e só trocam de volume: o drone do
casco, o pulso da corrida e a tensão que entra na última volta.

Para ouvir e ajustar timbre sem jogar, o banco inteiro sai em `.wav`:

```powershell
cd godot
.\jogar.bat --som-wav=C:\temp\som
```

São 49 arquivos, um por som, com as duas vozes de cada pista e uma varredura do
motor de ponta a ponta (marcha lenta, martelar até o teto, superaquecer, voltar
com o teto reduzido pelo tiro). O jogo nunca lê esses arquivos — quem toca é
sempre a síntese em memória; eles servem só para ouvir.

---

## Controles ESP32

Um ESP32 por nave, dois botões em cada, um cabo USB por controle. **As duas
versões do jogo falam com eles**: a 3D pela `GerenteSerial` em C#, a 2D pelo
`drivers/serial_driver.py`. O protocolo, a descoberta das portas e a regra de
quem é qual nave são os mesmos nos dois lados.

### Ligação

O controle deste projeto usa a **ESP32-C3 SuperMini**.

| Placa | Acelerador | Ação |
| --- | --- | --- |
| **ESP32-C3 SuperMini** | pino **3** | pino **5** |
| ESP32 clássico (DevKit) | GPIO 25 | GPIO 26 |
| ESP32-S2 / S3 / C6 | GPIO 3 | GPIO 5 |

Estes números saem dos `#define` no alto do `orbital_controle.ino` — se os
switches estiverem soldados em outros pinos, troque lá. Pino errado não dá erro
de compilação nem de gravação: o acelerador só não responde. O acelerador já
morou no pino 4 (versões 1.0, 1.2 e 2.0 do firmware); a tabela acima e os
`#define` têm de andar juntos.

### O botão não responde

Não adivinhe o pino — pergunte à placa:

```bash
python tools/controle_esp.py --achar-pino COM9    # aperte durante o teste
python tools/controle_esp.py --diagnostico COM9   # nível cru dos pinos, ao vivo
```

A varredura passa por todos os pinos livres duas vezes: com pull-up procurando
quem vai a **zero** (switch no GND, o certo) e com pull-down procurando quem vai
a **um** (switch no 3V3, invertido). O segundo passo existe porque o primeiro é
cego para a ligação invertida: apertar empurra o pino para onde ele já estava.

Só conta o pino que **muda** durante o teste. Pino ativo em 100% das amostras
está preso num trilho — na C3, o GPIO 2 e o GPIO 9 sempre aparecem assim no
passo do pull-down, porque a placa tem pull-up de fábrica neles. O firmware
marca e descarta esses sozinho.

O LED azul acompanha a varredura ao vivo: acende no instante em que algum pino
reage. Dá para achar o pad certo encostando o fio, sem olhar a tela.

Nenhum pino reagindo nas duas polaridades encerra o assunto software. Sobra:
solda fria, fio rompido, pad errado, ou os dois fios no mesmo par interno do
switch — num tátil de 4 pernas, 1-2 e 3-4 já saem ligados de fábrica, e é
preciso usar uma perna de **cada** par. A prova final é o multímetro em
continuidade nas duas pernas: solto abre, apertado fecha.

Cada botão vai **entre o pino e o GND**, sem resistor: o firmware liga o
pull-up interno. O debounce é por estado estável (5 ms). O LED da placa acende
enquanto qualquer botão está apertado, o que permite testar a fiação sem PC —
na SuperMini é o LED azul (GPIO 8). Na C3, não use os pinos 2, 8 e 9 (definem
o modo de boot) nem 20/21 (UART).

### Gravar o firmware

Para a SuperMini, a placa certa é **Nologo ESP32C3 Super Mini** (com esse nome
na Arduino IDE; no terminal, pelo FQBN abaixo). Troque `COM9` pela porta da sua
placa — `arduino-cli board list` mostra.

```bash
arduino-cli compile --fqbn esp32:esp32:nologo_esp32c3_super_mini firmware/orbital_controle
arduino-cli upload  --fqbn esp32:esp32:nologo_esp32c3_super_mini -p COM9 firmware/orbital_controle
```

**Não use a genérica "ESP32C3 Dev Module".** A SuperMini não tem conversor
USB-serial (a porta COM é a USB do próprio C3), e a genérica vem com "USB CDC
On Boot" desligado: o firmware gravaria sem erro, o `Serial` sairia pelos pinos
20/21 e a porta ficaria muda. Para não cair nisso, o firmware se recusa a
compilar para C3 com o CDC desligado — o `#error` diz o que fazer.

Se a gravação não conectar: segure **BOOT**, aperte e solte **RESET**, solte
BOOT e grave de novo; no fim, aperte RESET para o firmware rodar.

Se o `arduino-cli` não estiver no PATH, ele vem junto da Arduino IDE:
`%LOCALAPPDATA%\Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe`.

Outras placas: ESP32 clássico `esp32:esp32:esp32`; S3 `esp32:esp32:esp32s3`
(com USB nativa, acrescente `:CDCOnBoot=cdc`). Tamanho com o core 3.3.11: 21% da
flash na SuperMini e no ESP32 clássico, 23% no S3.

### Qual controle é qual nave

O id fica gravado no próprio ESP: **1 = ÍON, 2 = ÍGNIS**. Todo controle sai do
firmware como 1; o segundo precisa ser trocado uma vez:

```bash
pip install pyserial
python tools/controle_esp.py                       # lista os controles plugados
python tools/controle_esp.py --definir-id COM6 2   # este vira ÍGNIS
python tools/controle_esp.py --monitor COM6        # mostra os apertos ao vivo
```

O jogo acha os controles sozinho: varre as portas COM a cada 1,5 s, e a porta
que responde `HELLO ORBITAL` vira controle. A nave vem do id, não do número da
COM — trocar o cabo de entrada USB não troca a nave. Dois ESP com o mesmo id
ainda funcionam (o segundo assume a nave livre), mas o HUD avisa.

### Protocolo

115200 baud, uma mensagem ASCII por linha.

| Direção | Mensagem | Significado |
| --- | --- | --- |
| ESP → PC | `HELLO ORBITAL <id> <versão>` | ao ligar e em resposta a `?` |
| ESP → PC | `T1` / `T0` | acelerador apertado / solto |
| ESP → PC | `A1` / `A0` | ação apertada / solta |
| ESP → PC | `K xy` | sinal de vida a cada 500 ms, com o estado dos dois botões |
| PC → ESP | `?` | pergunta quem é |
| PC → ESP | `ID n` | grava o id (1 ou 2) |
| PC → ESP | `M <duty>` | reservado: PWM daquela pista, 0..1000. O jogo não manda e o firmware ignora |
| PC → ESP | `D <0\|1>` | liga/desliga o diagnóstico: nível cru dos pinos a cada 500 ms |
| PC → ESP | `VARRER` | acha em que GPIO o switch está ligado (trava o loop por ~11 s) |
| ESP → PC | `[ctrl] ...` | resposta do diagnóstico e da varredura |

O jogo **ignora em silêncio** toda linha que não reconhece — é o que faz o
mesmo código servir aos dois firmwares e aguentar o log de boot da ROM do ESP,
que sai grudado antes da apresentação.

Sem sinal de vida por 1,6 s o controle é dado como desconectado. O jogo abre a
porta com DTR e RTS desligados, porque em muitas placas eles resetam o ESP. O
acelerador conta as bordas de subida vindas dos eventos, não o estado lido por
frame: um aperto curto que caiba inteiro entre dois frames não se perde.

---

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
vazio, a caixa aparece na tela e fica aberta por **0,8 s**, e o portal do
checkpoint acende na cor da nave. Não apertou nesse tempo, a chance passa.

**O giro não para a nave.** Apertar a ação não interrompe nada: a caixa cicla
por 1,5 s enquanto o carrinho continua andando, e o jogador segue martelando o
acelerador. Usar o item depois também não para.

Como girar não custa velocidade, o risco da caixa é outro: **uma das quatro
faces não dá nada** (18% dos giros). É o que impede que apertar em todo
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

**A ÍRIS-9 é só cenário por enquanto.** Na versão 2D a varredura está desligada
em `IRIS_EVENTO_ATIVO = False` (o maquinário continua em `station.py`); na 3D a
estação só respira — o diafragma abre e fecha e a luz do núcleo acompanha.

Vence quem completar 5 voltas.

---

## Arquitetura

A regra que organiza tudo: **o único atuador físico é o PWM de cada pista**
(0.0 a 1.0). Toda mecânica termina em um valor de PWM. Um poder que não seja
"aumenta o teto", "reduz o teto" ou "zera" não entra no jogo.

### 3D

```text
godot/
  scripts/core/          a regra em C# puro, sem Godot: Config, Pista, Nave,
                         Roleta, Corrida, Protocolo
  scripts/input/         GerenteSerial (ESP pela USB), Fontes (teclado/controle/CPU)
  scripts/world/         o mundo 3D: traçado, pista, estação, naves, pedras,
                         câmera, partículas, efeitos
  scripts/audio/         o som, todo sintetizado: Sintese (DSP), Banco (as
                         receitas), MotorSom (motor ao vivo), Trilha, Mixagem
  scripts/ui/Hud.cs      painéis de vidro, caixa de item, telemetria, nomes nas naves
  scripts/Main.cs        telas, teclas, liga uma coisa na outra
  shaders/               céu, pista, estação, pedra, chama, escudo, vidro
firmware/orbital_controle/   firmware dos controles ESP32
tools/controle_esp.py        utilitário de bancada dos controles
tests/                       testes do núcleo C#
```

- `Corrida` é o único ponto que escreve no `IBarramentoSaida`. O rodapé de
  telemetria lê do barramento (`SaidaNula`), não das naves: se um efeito não
  aparece ali, ele não chegou ao PWM.
- `Efeitos3D` implementa `IEfeitos` e não decide nada. Os projéteis seguem a
  lista oficial `Corrida.EmVoo`; quando a regra resolve a chegada, o estouro cai
  no mesmo frame em que o PWM do alvo muda.
- `Som` entra pela mesma porta: um `EfeitosCompostos` reparte os eventos da
  regra entre o 3D e o áudio. O núcleo não ganhou nenhuma linha por causa do
  som — o que não é evento da regra (volta, ultrapassagem, calor no vermelho, o
  próprio motor) o `Som` lê do estado a cada quadro, porque inventar evento novo
  no núcleo custaria a paridade com a versão 2D.
- Nada da serial roda na thread do jogo: uma porta que trava ao abrir, ou um
  cabo puxado no meio da corrida, não congela a tela.

### 2D

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

- Só `drivers/input_driver.py` lê teclado; só `game/app.py` conhece eventos de
  janela.
- `effects.py` é só desenho: a `Race` aceita rodar sem ele (`Race(bus)` usa um
  objeto nulo), e é assim que os testes headless funcionam.

---

## Testes

```bash
dotnet test tests/OrbitalDerby.Core.Tests
```

São 61 testes do núcleo C#: acelerador, roleta, poderes, corrida, protocolo
serial. `ReferenciaPythonTests` roda cenários fixos nas duas implementações e
compara com o JSON gerado pela versão Python (`python tests/gerar_referencia.py`),
com tolerância de 1e-9 — se a regra mudar de um lado só, o teste acusa.

---

## Onde plugar o hardware da pista

### Saída — o PWM das pistas

- **3D:** implemente `IBarramentoSaida` (em `godot/scripts/core/Corrida.cs`) e
  entregue a sua classe no lugar da `SaidaNula` onde `Main.cs` cria a `Corrida`.
- **2D:** `PwmOutput` em `drivers/output_bus.py` é o stub; implemente
  `set_lane(lane, pwm, effect_tag)` e troque em `main.py`.

Se os motores tiverem zona morta (abaixo de ~0,25 de duty o carrinho não sai do
lugar), o remapeamento pertence a essa classe. O jogo continua raciocinando em
0..1 lineares.

### Entrada

A entrada já é real nas duas versões: os controles ESP32 acima — `GerenteSerial`
na 3D, `drivers/serial_driver.py` na 2D. O `GpioDriver` em
`drivers/input_driver.py` é stub morto: guarda o mapa de pinos de Raspberry Pi
que existia antes de a entrada virar ESP32 pela USB.

Debounce é obrigatório: o acelerador é lido pela BORDA de subida, então um botão
que chacoalha vira cliques fantasmas e acelera sozinho. O firmware faz o
debounce, e o intervalo mínimo entre cliques aceitos (`CLIQUE_INTERVALO_MIN`,
25 Hz) é a segunda linha de defesa — não a primeira.

### Sensores de checkpoint

Hoje a posição `t` vem só da integração de `speed × dt`. **Com o hardware ela
continuará vindo da integração entre sensores** — o sensor não mede posição
contínua, só reancora quando o carrinho passa. Por isso `t` já é tratado como
estimativa corrigível em todo o código. Quando um sensor disparar:

```text
3D:  corrida.SincronizarPosicao(lane, t_daquele_sensor)
2D:  race.sync_position(lane, t_daquele_sensor)
```

A contagem de volta é feita dentro do método, na virada de `>0.9` para `<0.1`.

**A janela da roleta já está modelada como o sensor funciona.** Ela não é uma
janela de posição ("estar perto do checkpoint"), e sim de tempo: a PASSAGEM
abre a janela, que dura `ROLETA_OPORTUNIDADE` segundos. Com o hardware, o pulso
do sensor entra por `sync_position` e abre exatamente a mesma janela.

---

## Calibração: o que precisa ser remedido

Se estes valores não baterem com a pista física, a nave na tela dessincroniza
do carrinho real. Na 2D ficam em `game/config.py`; na 3D, em
`godot/scripts/core/Config.cs` (classe `Cfg`), com nomes equivalentes em
PascalCase.

| Constante | O que é |
| --- | --- |
| `CHECKPOINTS` | Posição normalizada de cada sensor na volta. **Medir na pista.** |
| `ROLETA_OPORTUNIDADE` | Quantos segundos a roleta fica aberta após a passagem |
| `SOMBRA_INICIO` / `SOMBRA_FIM` | Trecho da zona de sombra. Só importa se `IRIS_EVENTO_ATIVO` voltar; aí **medir na pista** e marcar fisicamente. |
| `COMPRIMENTO_VOLTA_M` | Comprimento real da volta (só telemetria) |
| `PWM_BASE` | Duty que corresponde ao teto base de velocidade |

## Ajustes de balanceamento

- `CATCHUP_ATIVO = False` desliga o viés de catch-up da roleta, para testar o
  balanceamento cru. O fator fica isolado em `CATCHUP_BIAS`.
- `ACCEL`, `DECEL`, `CAP` — física do acelerador. `CAP = 0.16` dá ~6,2 s por
  volta no teto absoluto.
- `CALOR_SUBIDA` / `CALOR_DESCIDA` — a razão entre os dois define o ciclo de
  trabalho sustentável. **Se quiser corridas mais curtas, mexa aqui antes de
  mexer em `VOLTAS_PARA_VENCER`.**
- `ROLETA_OPORTUNIDADE`, `ROLETA_GIRO`, `ROLETA_REVELACAO` — o ritmo da caixa.
- O peso de `"nada"` em `ITEM_PESOS` é o risco da caixa, e a única coisa que
  ainda faz girar ser uma decisão. Zerá-lo transforma cada checkpoint em item
  garantido.
- `TIRO_VOO` / `BOMBA_VOO` — tempo de voo dos ataques. Aumentar dá mais janela
  para o adversário reagir com o Escudo; é balanceamento, não animação.
- `TIRO_ALCANCE` / `BOMBA_ALCANCE` — o quanto o alvo pode estar longe e ainda
  ser acertado, em voltas. Aumentar torna os ataques quase automáticos.
- `CADENCIA_PLENA_HZ` — quantos cliques por segundo dão PWM cheio.
- `CALOR_LIMIAR` — a partir de que esforço o motor começa a esquentar.
- `CLIQUE_TIMEOUT`, `CLIQUE_INTERVALO_MIN` — quando o motor corta por falta de
  apertos, e o teto anti-repique.

Mudou uma regra? Mude nas duas versões e rode `python tests/gerar_referencia.py`
seguido de `dotnet test` — o teste de referência existe para pegar a divergência.

---

## Versão 2D (pygame)

```bash
pip install pygame-ce      # pygame 2.x também serve
python main.py
```

Testado com Python 3.12 e pygame 2.6.1. Janela de 1280x720, 60 FPS. Se o
terminal mostrar `no fast renderer available` e o jogo ficar lento, rode sem o
modo escalado: `ORBITAL_NO_SCALED=1 python main.py`.

Teclas: as mesmas da 3D (`A`/`S` e `L`/`K`), mais `Espaço`, `R`, `F11`, `Esc`
e `F3`.

### Controles ESP32 na 2D

Por padrão o `main.py` liga teclado **e** controles ao mesmo tempo: quem
apertar, vale. Dá para jogar com um ESP e um teclado enquanto o segundo
controle não fica pronto, e o teclado continua valendo se um cabo cair no meio
da corrida.

| Opção | Efeito |
| --- | --- |
| (nenhuma) | teclado + ESP32; varre as portas COM a cada 1,5 s |
| `--teclado` | só teclado; não abre porta COM nenhuma |
| `--esp` | só os controles ESP32 |

A varredura precisa do pyserial (`pip install pyserial`); sem ele o jogo avisa
uma linha e segue no teclado. A tela de abertura mostra qual porta virou ÍON e
qual virou ÍGNIS, ou "aguardando" enquanto nenhuma respondeu.

Se as portas COM da máquina forem lentas de abrir (adaptadores Bluetooth são o
caso clássico), `--teclado` pula a varredura inteira. Ela roda em thread
separada de qualquer jeito: nenhuma porta travada congela a tela.

### Build web

O build está em `docs/`, a pasta servida pelo GitHub Pages. Para regenerar:

```bash
pip install pygbag
python tools/build_web.py
```

O script existe porque o pygbag batiza o pacote com o nome da pasta do projeto
— que aqui tem acento e cedilha, o que quebraria a URL. Ele compila numa cópia
temporária com nome ASCII e traz o resultado para `docs/`.

Duas coisas no código existem por causa do navegador:

- `main.py` importa `pygame` explicitamente, mesmo sem usar: o pygbag decide o
  que embarcar lendo os imports do ponto de entrada.
- `KeyboardDriver` resolve o mapa de teclas no primeiro `read()`, não em tempo
  de import: as constantes `pygame.K_*` ainda não existem quando o módulo é
  carregado no navegador.

Se as animações pesarem em máquina fraca, `effects.MAX_PARTICULAS` limita o
total de partículas vivas (padrão 320).

---

## Limitações conhecidas

- **O controle está testado numa C3 SuperMini de verdade**: apresenta-se na
  USB, manda os botões e as duas versões do jogo jogam com ele.
- A saída de PWM para as pistas continua nula nas duas versões: a regra calcula
  o PWM e a telemetria mostra, mas nada sai para um motor ainda. **Não há mais
  firmware de carrinho neste repositório** — o elo controle → motor foi tirado
  do escopo. O que existia está na história do git, no commit que trouxe o
  `firmware/` para o controle de versão:

  ```bash
  git log --diff-filter=D --oneline -- firmware/orbital_carrinho
  git show <commit>^:firmware/orbital_carrinho/orbital_carrinho.ino
  ```

- **O fonte do firmware 3.0 do controle se perdeu.** O 3.0 era a versão com
  ESP-NOW que rodava na bancada; o fonte não estava em cache nenhum e o binário
  foi sobrescrito ao gravar o 1.2. O antecessor dele (**2.0**, com ESP-NOW,
  `M <duty>` e modo bancada) foi resgatado do cache do Arduino para
  [firmware/recuperado/](firmware/recuperado/) e compila. É o único fonte de
  controle com rádio que existe hoje — guardado como ponto de partida, caso o
  motor volte ao escopo.
- A 2D no navegador não tem controles ESP32: WebAssembly não tem porta COM.
  O link do GitHub Pages é sempre teclado.
- A versão 3D não tem build web (limitação do Godot com C#) nem executável
  exportado: roda pelo Godot. Exportar exige baixar os templates de exportação
  do Godot 4.7.2 .NET.
