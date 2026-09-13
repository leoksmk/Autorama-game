# Firmware recuperado do cache do Arduino

Isto não é código escrito aqui: é código **resgatado**. O
`firmware/orbital_controle/orbital_controle.ino` foi sobrescrito em algum
momento e voltou de uma versão com ESP-NOW (2.0) para uma versão só-USB (1.2).
O que estava lá antes só existia no cache do Arduino Language Server, em
`%LOCALAPPDATA%\Temp\arduino-language-server*\`, que some quando a IDE fecha.

## De onde veio

O Arduino não reescreve o sketch ao compilar: ele insere protótipos de função e
marca cada trecho com `#line N "arquivo"`. Isso é reversível — cada linha volta
para o número que o `#line` manda, e os protótipos inseridos são sobrescritos
quando o corpo da função chega. `tools/recuperar_ino.py` faz isso.

O método foi conferido antes de ser usado: reconstruindo o cache da versão 1.2,
o resultado saiu **byte a byte igual** ao arquivo que ainda existe no
repositório, diferindo só no pino do acelerador (o cache é anterior à troca).

`orbital_controle_2_0/` foi conferido de outro jeito também: **compila limpo**
para a SuperMini, 71% da flash.

```bash
arduino-cli compile --fqbn esp32:esp32:nologo_esp32c3_super_mini \
    firmware/recuperado/orbital_controle_2_0
```

## O que tem aqui

### `orbital_controle_2_0/` — controle + ponte de rádio, versão 2.0

Faz o que o 1.2 faz (dois switches viram `T`/`A` na USB) **e mais**: recebe
`M <duty>` do jogo e repassa por ESP-NOW para o carrinho, com prazo de validade
no pacote. Também tem modo bancada (anda sozinho sem PC), `D <0|1>` para o
diagnóstico e as linhas `[ctrl] ...` a cada 3 s. É este comportamento que o
README documenta na tabela do protocolo.

## O que este código NÃO resolve

**O 3.0 acabou.** Era a versão que rodava na bancada (`HELLO ORBITAL <id> 3.0`),
com ESP-NOW e link versão 3. O fonte não estava em cache nenhum — nem no
sketchbook, nem no Temp, nem em disco — e o binário foi sobrescrito quando o
1.2 foi gravado na placa. Não há de onde recuperar.

Este 2.0 é, hoje, **o único fonte de controle com rádio que existe**.

E ele não é substituto direto do 3.0, porque o link mudou no meio:

| | controle 2.0 (recuperado) | controle 3.0 (perdido) | carrinho (removido) |
| --- | --- | --- | --- |
| `LINK_VERSAO` | **2** | 3 (presumido) | **3** |
| emparelhamento | broadcast | PAREAR/PARCEIRO | PAREAR/PARCEIRO |
| `LINK_PROTOCOLO` | LR puro (`0x08`) | — | 11b/g/n + LR (`0x0F`) |

O `espnow_link.h` que veio junto é o da **versão 2** do link, e está aqui por
isso: sem ele o 2.0 nem compila. Mas ele é uma geração atrás do que o carrinho
já falava — em link 2 o pacote sai em broadcast e sem o conserto de alcance, e
o carrinho de link 3 receberia, veria a versão errada e reclamaria no serial em
vez de andar. O comentário do `espnow_link.h` v3 explica por que o LR puro foi
abandonado: ~96% de perda, porque `0x08` desliga 11b/g/n e o ESP-NOW em
broadcast usa taxa legada.

O firmware do carrinho **não está mais na árvore** — o motor saiu do escopo.
Ele está na história do git, junto com o `espnow_link.h` v3, que é a referência
certa se um dia o link voltar:

```bash
git log --diff-filter=D --oneline -- firmware/orbital_carrinho
git show <commit>^:firmware/orbital_carrinho/espnow_link.h
```

Se o motor voltar ao escopo, o caminho é **subir este 2.0 para o link 3**, não
rebaixar o carrinho: a versão 3 é que tem o emparelhamento e o conserto do
alcance.

## A lição, para não acontecer de novo

O 3.0 sumiu por dois motivos somados: o fonte nunca foi versionado (a pasta
`firmware/` inteira ainda está fora do git) e o arquivo foi sobrescrito no
mesmo caminho por uma versão anterior. O binário na placa era a última cópia,
e um upload comum a apagou.

Antes de gravar em cima de um firmware que só existe na placa, tire o binário:

```bash
esptool --port COM9 read_flash 0 0x400000 backup.bin
```

Não devolve o fonte, mas devolve o comportamento — dá para regravar e voltar
a ter uma placa que funciona enquanto o código é reescrito.
