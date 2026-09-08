# -*- coding: utf-8 -*-
"""
Roleta e itens.

Três poderes, e todos terminam num valor de PWM — que é o único atuador real
do autorama:

    tiro   -> multiplica o teto de PWM do adversário (para baixo)
    bomba  -> zera o PWM do adversário
    escudo -> cancela o próximo ataque recebido
    nada   -> a caixa veio vazia

O escudo não mexe em PWM diretamente; ele impede que outro efeito mexa.
Continua dentro da regra: o resultado observável ainda é um valor de PWM.

Ciclo da roleta:

    PARADA -> (cruzou checkpoint com o slot vazio) -> OPORTUNIDADE
    OPORTUNIDADE -> (não apertou em ROLETA_OPORTUNIDADE) -> PARADA
    OPORTUNIDADE -> (apertou a ação)               -> GIRANDO
    GIRANDO      -> (1,5 s, a nave segue andando)  -> REVELANDO
    REVELANDO    -> (1,0 s)                        -> PARADA, prêmio no slot

O giro é do tipo caixa de item: o ícone troca rápido e vai desacelerando até
travar. A sequência de faces é montada DE TRÁS PARA FRENTE a partir do prêmio
já sorteado, então a última face é sempre o prêmio de verdade — a animação não
tem como mostrar uma coisa e o slot receber outra.
"""

from __future__ import annotations

import random

from . import config as cfg

PARADA = "parada"
OPORTUNIDADE = "oportunidade"
GIRANDO = "girando"
REVELANDO = "revelando"

# Ordem fixa das faces. Nunca embaralhar: o jogador aprende a sequência e passa
# a ler a desaceleração da caixa.
ORDEM = tuple(cfg.ITEM_PESOS.keys())
N_ITENS = len(ORDEM)

# Quantas trocas de face o giro inteiro tem.
CICLOS_DO_GIRO = 22


def sortear(atrasado: bool = False) -> str:
    """
    Sorteia um item da roleta.

    `atrasado` indica que este jogador está atrás em progresso. Nesse caso os
    pesos recebem o viés de catch-up. O viés inteiro está em config e pode ser
    desligado com CATCHUP_ATIVO = False, para testar o balanceamento cru.
    """
    nomes = list(cfg.ITEM_PESOS.keys())
    pesos = []
    for nome in nomes:
        p = float(cfg.ITEM_PESOS[nome])
        if atrasado and cfg.CATCHUP_ATIVO:
            if nome in cfg.CATCHUP_FAVORECE:
                p *= 1.0 + cfg.CATCHUP_BIAS
            elif nome in cfg.CATCHUP_DESFAVORECE:
                p *= max(0.0, 1.0 - cfg.CATCHUP_BIAS)
        pesos.append(p)
    return random.choices(nomes, weights=pesos, k=1)[0]


def nome_legivel(item: str | None) -> str:
    if not item:
        return "vazio"
    return cfg.ITEM_NOMES.get(item, item)


def cor_do_item(item: str | None) -> tuple[int, int, int]:
    if not item:
        return cfg.COR_TEXTO_FRACO
    return cfg.ITEM_CORES.get(item, cfg.COR_TEXTO)


def _ease_out(p: float) -> float:
    """Desaceleração cúbica. Rápido no começo, arrastado no fim."""
    return 1.0 - (1.0 - p) ** 3


class Roleta:
    """Estado da caixa de item de um jogador."""

    def __init__(self) -> None:
        self.estado = PARADA
        self.tempo = 0.0
        self.resultado: str | None = None
        self.checkpoint: int | None = None
        self.face = ORDEM[0]       # item mostrado na caixa agora
        self._faces: list[str] = []
        self._idle = 0.0

    # -- consultas ----------------------------------------------------------

    @property
    def ativa(self) -> bool:
        """Girando. Não trava mais a nave: ela segue enquanto a caixa roda."""
        return self.estado == GIRANDO

    @property
    def visivel(self) -> bool:
        return self.estado != PARADA

    @property
    def pode_girar(self) -> bool:
        return self.estado == OPORTUNIDADE

    @property
    def fracao_restante(self) -> float:
        """Quanto sobra da janela de oportunidade, de 1 a 0."""
        if self.estado != OPORTUNIDADE:
            return 0.0
        return max(0.0, 1.0 - self.tempo / cfg.ROLETA_OPORTUNIDADE)

    @property
    def progresso_giro(self) -> float:
        if self.estado == GIRANDO:
            return min(1.0, self.tempo / cfg.ROLETA_GIRO)
        return 1.0 if self.estado == REVELANDO else 0.0

    # -- transições ---------------------------------------------------------

    def abrir(self, checkpoint: int) -> None:
        """Cruzou um checkpoint com o slot vazio: a janela abre."""
        self.estado = OPORTUNIDADE
        self.tempo = 0.0
        self.checkpoint = checkpoint
        self.resultado = None
        self._idle = 0.0

    def girar(self, atrasado: bool) -> bool:
        """
        Apertou a ação dentro da janela. Devolve False se não havia janela.

        A sequência de faces é montada de trás para frente a partir do prêmio,
        de modo que a última face exibida SEJA o prêmio.
        """
        if self.estado != OPORTUNIDADE:
            return False

        self.estado = GIRANDO
        self.tempo = 0.0
        self.resultado = sortear(atrasado)

        idx = ORDEM.index(self.resultado)
        n = CICLOS_DO_GIRO
        self._faces = [ORDEM[(idx - (n - 1 - i)) % N_ITENS] for i in range(n)]
        self.face = self._faces[0]
        return True

    def cancelar(self) -> None:
        self.estado = PARADA
        self.tempo = 0.0
        self.resultado = None
        self.checkpoint = None
        self._faces = []

    # -- ciclo --------------------------------------------------------------

    def atualizar(self, dt: float) -> str | None:
        """
        Avança a máquina de estados.

        Devolve o item no frame em que a revelação termina; None nos outros.
        """
        if self.estado == PARADA:
            return None

        self.tempo += dt

        if self.estado == OPORTUNIDADE:
            # A caixa fica trocando de face devagar, convidando a apertar.
            self._idle += dt
            if self._idle >= 0.22:
                self._idle = 0.0
                self.face = ORDEM[(ORDEM.index(self.face) + 1) % N_ITENS]
            if self.tempo >= cfg.ROLETA_OPORTUNIDADE:
                self.cancelar()
            return None

        if self.estado == GIRANDO:
            p = min(1.0, self.tempo / cfg.ROLETA_GIRO)
            i = int(_ease_out(p) * (len(self._faces) - 1))
            self.face = self._faces[i]
            if self.tempo >= cfg.ROLETA_GIRO:
                self.face = self.resultado
                self.estado = REVELANDO
                self.tempo = 0.0
            return None

        # REVELANDO
        self.face = self.resultado
        if self.tempo >= cfg.ROLETA_REVELACAO:
            item = self.resultado
            self.cancelar()
            return item
        return None
