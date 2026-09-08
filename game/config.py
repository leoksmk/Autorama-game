# -*- coding: utf-8 -*-
"""
Constantes de configuração do ORBITAL DERBY.

Este é o único lugar onde números "de mundo" devem existir. Quando o autorama
físico for montado, as posições de checkpoint e a zona de sombra terão que ser
REMEDIDAS para bater com a posição real dos sensores na pista, senão a nave
desenhada na tela dessincroniza do carrinho de verdade. Nada disso pode virar
número solto no meio da lógica.
"""

import sys

# No navegador (build WebAssembly via pygbag) não há janela para redimensionar
# nem processo para encerrar: tela cheia e "sair" não fazem sentido.
NO_NAVEGADOR = sys.platform == "emscripten"

# ---------------------------------------------------------------------------
# Tela
# ---------------------------------------------------------------------------
LARGURA = 1280
ALTURA = 720
FPS = 60
TITULO = "Orbital Derby"

# ---------------------------------------------------------------------------
# Paleta (identidade original, sem referência a franquias)
# ---------------------------------------------------------------------------
COR_FUNDO = (0x04, 0x06, 0x0D)
COR_PAINEL = (0x0C, 0x12, 0x20)
COR_TEXTO = (0xE6, 0xED, 0xF7)
COR_TEXTO_FRACO = (0x74, 0x84, 0x9E)
COR_ESTACAO = (0x7C, 0x5C, 0xFF)
COR_PISTA = (0x1A, 0x24, 0x3A)
COR_LINHA = (0x2A, 0x37, 0x52)
COR_SOMBRA = (0x18, 0x3A, 0x33)
COR_ALERTA = (0xFF, 0x4D, 0x6D)
COR_OK = (0x3F, 0xE0, 0xA0)

# Jogadores
COR_P1 = (0x2F, 0xD6, 0xFF)   # ÍON
COR_P2 = (0xFF, 0xA0, 0x2E)   # ÍGNIS
NOME_P1 = "ÍON"
NOME_P2 = "ÍGNIS"

# ---------------------------------------------------------------------------
# Traçado da pista
# ---------------------------------------------------------------------------
# Elipse com modulação r = 1 + MODULACAO * cos(HARMONICA * a)
PISTA_CX = 640
PISTA_CY = 372
PISTA_RX = 386.0
PISTA_RY = 248.0
PISTA_MODULACAO = 0.075
PISTA_HARMONICA = 3
PISTA_AMOSTRAS = 720          # resolução da LUT usada para desenhar

# Deslocamento de cada faixa pela normal da curva, em pixels
FAIXA_OFFSET = 15.0

# COMPRIMENTO_VOLTA_M: comprimento físico de uma volta, em metros.
# Só é usado para telemetria legível. No hardware real este valor precisa ser
# medido na pista; ele não altera a simulação (t é sempre normalizado).
COMPRIMENTO_VOLTA_M = 4.20

VOLTAS_PARA_VENCER = 5

# ---------------------------------------------------------------------------
# Física do acelerador
# ---------------------------------------------------------------------------
# O acelerador NÃO é de segurar: é de martelar. O que vira PWM é a FREQUÊNCIA
# dos apertos, medida pelo intervalo entre um clique e o próximo:
#
#     2 cliques/s -> esforço 0,25   (arrastando)
#     4 cliques/s -> esforço 0,50
#     8 cliques/s -> esforço 1,00   (PWM cheio)
#
# Medir o intervalo, e não acumular pulsos que decaem, é o que mantém o PWM
# ESTÁVEL entre um clique e outro. Com acumulador, o duty oscilaria uns 50% a
# cada aperto e o carrinho real trepidaria.
CADENCIA_PLENA_HZ = 8.0       # cliques por segundo que dão esforço 1,0
CADENCIA_SUAVIZACAO = 0.55    # 0..1: quanto cada novo intervalo pesa
CLIQUE_INTERVALO_MIN = 0.04   # 25 Hz: teto anti-repique e anti-turbo
CLIQUE_TIMEOUT = 0.6          # sem aperto por este tempo, o motor corta de vez
# O primeiro aperto depois de uma pausa não tem intervalo anterior para medir.
# Sem este empurrão inicial, apertar uma vez não faria absolutamente nada e o
# botão pareceria quebrado.
CADENCIA_INICIAL_HZ = 2.5

# speed é medido em VOLTAS POR SEGUNDO.
ACCEL = 0.25                  # voltas/s² ganhos enquanto acelera
DECEL = 0.35                  # voltas/s² perdidos quando solta
CAP = 0.16                    # teto base de velocidade (~6,2 s por volta)

# Regra de ouro: o único atuador físico é o PWM da pista (0.0 a 1.0).
# PWM_BASE é o duty que corresponde ao teto base CAP com o acelerador cravado.
# Ele fica abaixo de 1.0 de propósito, para sobrar margem para o Impulso (x1,7).
PWM_BASE = 0.55
# Conversão PWM -> teto de velocidade. Derivada, nunca digitada à mão.
VEL_POR_PWM = CAP / PWM_BASE

# ---------------------------------------------------------------------------
# Calor
# ---------------------------------------------------------------------------
CALOR_SUBIDA = 1.0 / 4.5      # 0 -> 1 em 4,5 s martelando a plenos pulmões
CALOR_DESCIDA = 1.0 / 3.0     # 1 -> 0 em 3,0 s em ritmo baixo

# Abaixo deste esforço o motor esfria; acima, esquenta proporcionalmente. É o
# que separa "manter o ritmo" de "martelar sem pensar".
CALOR_LIMIAR = 0.55
SUPERAQUECIMENTO_DURACAO = 1.6
SUPERAQUECIMENTO_CALOR_RESIDUAL = 0.75

# ---------------------------------------------------------------------------
# Checkpoints e roleta
# ---------------------------------------------------------------------------
# ATENÇÃO: remedir contra a posição física dos sensores antes de plugar o GPIO.
CHECKPOINTS = (0.12, 0.45, 0.78)

# A janela de roleta é de TEMPO, não de posição: cruzar o checkpoint abre a
# oportunidade e ela dura ROLETA_OPORTUNIDADE segundos. Isso casa exatamente
# com o sensor físico, que dispara um evento pontual e não mede posição
# contínua — no hardware, o pulso do sensor abre a mesma janela.
ROLETA_OPORTUNIDADE = 0.8     # tempo para decidir; se não apertar, some
ROLETA_GIRO = 1.5             # a nave continua andando durante o giro
ROLETA_REVELACAO = 1.0        # quanto tempo o prêmio fica em destaque

# Rótulo do botão de ação mostrado no miolo da roleta. É só texto de tela:
# na simulação são teclas, no autorama vira o nome do botão físico.
ROTULO_ACAO = {0: "S", 1: "K"}

# ---------------------------------------------------------------------------
# Itens
# ---------------------------------------------------------------------------
# Três poderes, e todos terminam num valor de PWM:
#   tiro   -> derruba o teto de PWM do adversário
#   bomba  -> zera o PWM do adversário
#   escudo -> cancela o próximo ataque recebido
#
# E uma quarta face que não dá nada. Como girar não custa mais velocidade, a
# chance de sair vazio é o ÚNICO risco da caixa — é o que impede que apertar
# em todo checkpoint seja uma jogada sem contrapartida.
ITEM_PESOS = {
    "tiro": 34,
    "bomba": 26,
    "escudo": 22,
    "nada": 18,
}

ITEM_NOMES = {
    "tiro": "Tiro",
    "bomba": "Bomba",
    "escudo": "Escudo",
    "nada": "Nada",
}

# Cor de cada item: usada na caixa da roleta, no slot e nas animações.
ITEM_CORES = {
    "tiro": (0xFF, 0x6B, 0x5C),
    "bomba": (0xFF, 0xC4, 0x3D),
    "escudo": (0x5A, 0xE0, 0xD8),
    "nada": (0x6B, 0x74, 0x86),
}

# Frase curta mostrada na revelação e na animação de uso.
ITEM_DESCRICOES = {
    "tiro": "deixa o adversário lento",
    "bomba": "para o adversário por 2 s",
    "escudo": "bloqueia o próximo ataque",
    "nada": "a caixa veio vazia",
}

BOMBA_DURACAO = 2.0           # PWM do adversário zerado

# Tempo de voo de cada ataque. NÃO é enfeite: o efeito só é aplicado quando o
# projétil chega, então dá para levantar o Escudo com a bomba já no ar. Por
# isso mora aqui e não no módulo de animação.
BOMBA_VOO = 0.55
TIRO_VOO = 0.38

# Alcance dos ataques, em voltas. Medido pelo caminho MAIS CURTO da pista, em
# qualquer sentido: perto é perto, esteja o alvo à frente ou atrás. Fora do
# alcance o ataque erra e o item queima — é o que obriga a escolher a hora.
TIRO_ALCANCE = 0.12
BOMBA_ALCANCE = 0.12

TIRO_MULT = 0.45              # teto de PWM do alvo enquanto durar
TIRO_DURACAO = 2.5

# Viés de catch-up nos pesos da roleta para quem está atrás.
# Deixe CATCHUP_ATIVO = False para testar o balanceamento cru.
CATCHUP_ATIVO = True
CATCHUP_BIAS = 0.55           # 0.0 = sem viés
# Itens favorecidos / desfavorecidos para quem está atrás
CATCHUP_FAVORECE = ("tiro", "bomba")
CATCHUP_DESFAVORECE = ("nada",)

# ---------------------------------------------------------------------------
# Estação ÍRIS-9
# ---------------------------------------------------------------------------
# A varredura está DESLIGADA por enquanto: a ÍRIS-9 é cenário, não ameaça.
# O maquinário continua no código (station.py) e volta com um True aqui —
# a zona de sombra some da pista junto, e o HUD esconde a barra de carga.
IRIS_EVENTO_ATIVO = False

IRIS_CARGA_DURACAO = 25.0     # segundos para encher a barra
IRIS_AVISO = 0.80             # fração da carga onde começa o aviso
IRIS_VARREDURA_DURACAO = 1.5  # o feixe percorre a órbita inteira nesse tempo
IRIS_MULT = 0.45              # teto de PWM aplicado a quem é atingido
IRIS_DURACAO = 2.5            # por quanto tempo o teto fica reduzido

# Zona de sombra: trecho da órbita protegido da varredura.
# ATENÇÃO: também precisa ser remedida contra a pista física.
SOMBRA_INICIO = 0.60
SOMBRA_FIM = 0.75

# Raio da estação em pixels
IRIS_RAIO = 96

# ---------------------------------------------------------------------------
# Telas
# ---------------------------------------------------------------------------
CONTAGEM_DURACAO = 3.0
