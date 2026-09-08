# -*- coding: utf-8 -*-
"""
Estado de uma nave / pista.

Aqui mora a regra de ouro: o frame termina calculando UM número, `self.pwm`,
e a velocidade é consequência dele. Nada altera `speed` diretamente. Se algum
efeito quisesse mexer na velocidade sem passar pelo PWM, ele não teria como
existir no carrinho físico.

Ordem de resolução dentro de um frame:

    1. envelhece os temporizadores e vaza a cadência de cliques
    2. registra o clique do jogador, se ele for honrado
    3. atualiza o calor conforme o esforço
    4. calcula o teto de PWM (multiplicadores) e o PWM alvo
    5. persegue a velocidade correspondente com ACCEL / DECEL
    6. integra a posição
"""

from __future__ import annotations

import math

from . import config as cfg
from . import track
from .items import Roleta


class Ship:
    def __init__(self, lane: int, nome: str, cor: tuple[int, int, int], t_inicial: float) -> None:
        self.lane = lane
        self.nome = nome
        self.cor = cor

        # Posição: ESTIMATIVA. Hoje vem só da integração; no hardware será
        # corrigida a cada sensor via sync_position().
        self.t = t_inicial % 1.0
        self.voltas = 0
        self.speed = 0.0          # voltas por segundo
        self.pwm = 0.0            # o que iria para o motor neste frame

        # Acelerador de martelar: guardamos a frequência de clique estimada e
        # há quanto tempo o último aperto aconteceu.
        self.cadencia_hz = 0.0
        self.desde_clique = 99.0

        # Calor
        self.calor = 0.0
        self.superaquecimento = 0.0

        # Slot único, sem estoque
        self.item: str | None = None
        self.roleta = Roleta()

        # Temporizadores de efeito
        self.lento = 0.0          # teto x TIRO_MULT (levou um tiro)
        self.atordoado = 0.0      # PWM zerado (levou uma bomba)

        # Carga defensiva
        self.escudo = False       # bloqueia o próximo ataque recebido

        # Evento de passagem por sensor, válido só no frame em que acontece.
        # É consumido pela Race. Quando os sensores físicos existirem, eles
        # preenchem este mesmo campo por sync_position() e nada mais muda.
        self.checkpoint_cruzado: int | None = None

        # Feedback para o HUD (não afeta a simulação)
        self.aviso = ""
        self.aviso_tempo = 0.0
        self.flash = 0.0
        self.terminou = False
        self.tempo_final = 0.0

    # -- consultas ----------------------------------------------------------

    @property
    def progresso(self) -> float:
        """Voltas acumuladas + fração da volta atual. Serve para ordenar."""
        return self.voltas + self.t

    @property
    def travado(self) -> bool:
        """
        Situações em que os cliques do jogador são ignorados.

        Girar a caixa NÃO entra aqui: a nave continua andando enquanto a roleta
        roda, e o jogador segue martelando o acelerador com a outra mão.
        """
        return self.superaquecimento > 0.0 or self.atordoado > 0.0

    @property
    def esforco(self) -> float:
        """Ritmo de clique normalizado, 0 a 1. É o que vira PWM."""
        return min(1.0, self.cadencia_hz / cfg.CADENCIA_PLENA_HZ)

    def na_sombra(self) -> bool:
        return track.dentro_da_sombra(self.t)

    def teto_pwm(self) -> float:
        """Teto de PWM depois dos multiplicadores ativos."""
        teto = cfg.PWM_BASE
        if self.lento > 0.0:
            teto *= cfg.TIRO_MULT
        return min(1.0, teto)

    def effect_tag(self) -> str:
        """
        Rótulo dominante para a telemetria. A ordem é de prioridade: o que
        mais restringe o motor aparece primeiro.
        """
        if self.terminou:
            return "chegou"
        if self.superaquecimento > 0.0:
            return "superaquecido"
        if self.atordoado > 0.0:
            return "parado pela bomba"
        partes = []
        if self.lento > 0.0:
            partes.append("teto reduzido")
        if self.escudo:
            partes.append("escudo")
        return " + ".join(partes) if partes else "livre"

    # -- efeitos recebidos --------------------------------------------------

    def receber(self, ataque: str) -> str:
        """
        Aplica um ataque do adversário.

        Devolve "bloqueado" ou "atingido", que é o que as animações e o rodapé
        usam para narrar. O Escudo é gasto no primeiro ataque que chegar,
        qualquer que seja ele.
        """
        if self.escudo:
            self.escudo = False
            self.mensagem("Escudo bloqueou")
            self.flash = 0.30
            return "bloqueado"

        if ataque == "bomba":
            self.atordoado = max(self.atordoado, cfg.BOMBA_DURACAO)
        elif ataque == "tiro":
            self.lento = max(self.lento, cfg.TIRO_DURACAO)

        self.flash = 0.35
        return "atingido"

    def mensagem(self, texto: str, duracao: float = 1.6) -> None:
        self.aviso = texto
        self.aviso_tempo = duracao

    # -- correção de posição (ponte para o hardware) ------------------------

    def sync_position(self, t: float) -> None:
        """
        Corrige a estimativa de posição a partir de uma leitura de sensor.

        Não é usado nesta etapa (a pista é 100% simulada), mas a lógica já
        trata `t` como corrigível. No autorama real, cada sensor de checkpoint
        chama isto com o t configurado daquele sensor. A integração continua
        rodando entre um sensor e outro; o sensor só reancora.

        A virada de volta é detectada aqui também: se o carrinho estava perto
        do fim da volta e o sensor reancora no começo, a volta contou. E a
        passagem pelo sensor também registra o evento de checkpoint, que é o
        que abre a janela da roleta — no hardware, o pulso do sensor faz o
        mesmo caminho que a integração faz hoje.
        """
        anterior = self.t
        novo = t % 1.0
        if anterior > 0.9 and novo < 0.1:
            self.voltas += 1
        cruzou = track.cruzou_checkpoint(anterior, novo)
        if cruzou is not None:
            self.checkpoint_cruzado = cruzou
        self.t = novo

    # -- ciclo --------------------------------------------------------------

    def atualizar(self, dt: float, clique: bool, correndo: bool) -> None:
        """
        `clique` é a BORDA de subida do botão de acelerador, não o estado dele.

        Segurar o botão não faz nada: o que anda é a sequência de apertos.
        """
        # 0. o evento de sensor vale um frame só
        self.checkpoint_cruzado = None

        # 1. temporizadores
        self.lento = max(0.0, self.lento - dt)
        self.atordoado = max(0.0, self.atordoado - dt)
        self.aviso_tempo = max(0.0, self.aviso_tempo - dt)
        self.flash = max(0.0, self.flash - dt)
        if self.aviso_tempo == 0.0:
            self.aviso = ""

        em_superaquecimento = self.superaquecimento > 0.0
        if em_superaquecimento:
            self.superaquecimento = max(0.0, self.superaquecimento - dt)

        # 2. cadência de clique
        self.desde_clique += dt
        vale = correndo and not self.travado and not self.terminou

        if em_superaquecimento or self.atordoado > 0.0 or not vale:
            # Motor cortado ou nave travada: os apertos se perdem e o embalo
            # some junto. Voltar exige recomeçar o ritmo.
            self.cadencia_hz = 0.0
            self.desde_clique = 99.0
        elif clique:
            if self.desde_clique > cfg.CLIQUE_TIMEOUT:
                # Primeiro aperto depois de uma pausa: não há intervalo para
                # medir, então o botão responde com um empurrão fixo.
                self.cadencia_hz = cfg.CADENCIA_INICIAL_HZ
            else:
                intervalo = max(cfg.CLIQUE_INTERVALO_MIN, self.desde_clique)
                nova = 1.0 / intervalo
                # Média entre os últimos intervalos: absorve um clique
                # irregular sem transformar o PWM num serrote.
                self.cadencia_hz += (nova - self.cadencia_hz) * cfg.CADENCIA_SUAVIZACAO
            self.desde_clique = 0.0
        else:
            # Sem clique novo, a frequência não pode ser maior do que o tempo
            # já esperado permite. É assim que soltar o botão desacelera, sem
            # precisar de nenhum decaimento arbitrário.
            teto = 1.0 / max(1e-6, self.desde_clique)
            if self.cadencia_hz > teto:
                self.cadencia_hz = teto
            # Passou do tempo limite: parou de clicar, o motor corta. Sem isso
            # a queda 1/t deixaria um duty residual de alguns por cento para
            # sempre — inofensivo na tela, sujeira no motor de verdade.
            if self.desde_clique > cfg.CLIQUE_TIMEOUT or self.cadencia_hz < 0.05:
                self.cadencia_hz = 0.0

        esforco = self.esforco if vale else 0.0

        # 3. calor: proporcional ao esforço acima do limiar; abaixo dele esfria
        if em_superaquecimento:
            self.calor = max(0.0, self.calor - cfg.CALOR_DESCIDA * dt)
        elif esforco > cfg.CALOR_LIMIAR:
            excesso = (esforco - cfg.CALOR_LIMIAR) / (1.0 - cfg.CALOR_LIMIAR)
            self.calor = min(1.0, self.calor + cfg.CALOR_SUBIDA * excesso * dt)
            if self.calor >= 1.0:
                # Superaquecimento: corta o PWM e devolve o calor já parcial.
                self.superaquecimento = cfg.SUPERAQUECIMENTO_DURACAO
                self.calor = cfg.SUPERAQUECIMENTO_CALOR_RESIDUAL
                self.cadencia = 0.0
                self.mensagem("Superaquecimento", 1.6)
                esforco = 0.0
        else:
            self.calor = max(0.0, self.calor - cfg.CALOR_DESCIDA * dt)

        # 4. PWM. Este é o único ponto do jogo que decide o que vai ao motor.
        self.pwm = self.teto_pwm() * esforco

        # 5. velocidade persegue o teto correspondente ao PWM
        alvo = self.pwm * cfg.VEL_POR_PWM
        if self.speed < alvo:
            self.speed = min(alvo, self.speed + cfg.ACCEL * dt)
        elif self.speed > alvo:
            self.speed = max(alvo, self.speed - cfg.DECEL * dt)

        # 6. posição (estimativa por integração). A passagem por checkpoint é
        # detectada aqui, do mesmo jeito que o sensor físico detectaria: pela
        # travessia entre dois instantes, não por "estar perto".
        if correndo and not self.terminou:
            anterior = self.t
            self.t = (self.t + self.speed * dt) % 1.0
            cruzou = track.cruzou_checkpoint(anterior, self.t)
            if cruzou is not None:
                self.checkpoint_cruzado = cruzou
            if anterior > 0.9 and self.t < 0.1:
                self.voltas += 1
