# -*- coding: utf-8 -*-
"""
Estação ÍRIS-9.

DESLIGADA por enquanto: com cfg.IRIS_EVENTO_ATIVO = False a Race nunca chama
atualizar(), a estação vira só cenário animado e a zona de sombra some da
pista. O maquinário abaixo continua íntegro e volta com um True lá no config.

Não é decoração. É um relógio que obriga os dois jogadores a pensar em ritmo,
não só em velocidade: a barra enche sozinha, avisa aos 80%, e quem não estiver
na zona de sombra quando o feixe passar leva teto de PWM reduzido.

O feixe VARRE: ele percorre a órbita inteira durante IRIS_VARREDURA_DURACAO, e
cada nave é avaliada no instante em que o feixe cruza a posição dela. É por isso
que o aviso existe — a decisão de correr para a sombra ou aceitar o dano é
tomada durante os ~5 s de aviso, não durante a varredura.
"""

from __future__ import annotations

from . import config as cfg
from . import track


class Iris9:
    def __init__(self) -> None:
        self.carga = 0.0          # 0..1
        self.varrendo = False
        self.varredura_t = 0.0    # 0..1, posição angular do feixe
        self.tempo_varredura = 0.0
        self._avaliadas: set[int] = set()
        self.pulso_visual = 0.0   # animação do diafragma
        self.eventos: list[tuple[int, str]] = []  # (lane, resultado)

    @property
    def avisando(self) -> bool:
        return not self.varrendo and self.carga >= cfg.IRIS_AVISO

    def reset(self) -> None:
        self.__init__()

    def animar(self, dt: float) -> None:
        """Avança só o relógio visual. Usado quando o evento está desligado."""
        self.pulso_visual += dt

    def atualizar(self, dt: float, naves: list) -> None:
        """
        Avança a carga e, durante a varredura, resolve quem foi atingido.

        `naves` é a lista de Ship. A estação chama Ship.receber(), que é quem
        sabe lidar com Escudo e Fantasma.
        """
        self.eventos.clear()
        self.pulso_visual += dt

        if self.varrendo:
            self.tempo_varredura += dt
            self.varredura_t = min(
                1.0, self.tempo_varredura / cfg.IRIS_VARREDURA_DURACAO
            )

            for nave in naves:
                if nave.lane in self._avaliadas:
                    continue
                # O feixe cruzou a posição desta nave?
                if self.varredura_t >= nave.t:
                    self._avaliadas.add(nave.lane)
                    if track.dentro_da_sombra(nave.t):
                        self.eventos.append((nave.lane, "sombra"))
                    else:
                        self.eventos.append((nave.lane, nave.receber("tiro")))

            if self.tempo_varredura >= cfg.IRIS_VARREDURA_DURACAO:
                # Naves que o feixe não alcançou (só acontece com t muito alto
                # e arredondamento) são avaliadas no fecho, para não escapar.
                for nave in naves:
                    if nave.lane not in self._avaliadas:
                        self._avaliadas.add(nave.lane)
                        if not track.dentro_da_sombra(nave.t):
                            self.eventos.append((nave.lane, nave.receber("tiro")))
                self.varrendo = False
                self.varredura_t = 0.0
                self.tempo_varredura = 0.0
                self.carga = 0.0
            return

        self.carga = min(1.0, self.carga + dt / cfg.IRIS_CARGA_DURACAO)
        if self.carga >= 1.0:
            self.disparar()

    def disparar(self) -> None:
        self.varrendo = True
        self.varredura_t = 0.0
        self.tempo_varredura = 0.0
        self._avaliadas.clear()

    def abertura(self) -> float:
        """
        Quanto o diafragma está aberto, 0..1. Puramente visual.

        Com a varredura desligada a estação respira devagar, para não virar uma
        bola parada no meio da tela. Com ela ligada: fechado em repouso,
        pulsando no aviso, escancarado na varredura.
        """
        import math

        if not cfg.IRIS_EVENTO_ATIVO:
            return 0.20 + 0.16 * (0.5 + 0.5 * math.sin(self.pulso_visual * 0.55))

        if self.varrendo:
            return 1.0
        if self.avisando:
            # Pulsação acelera conforme a carga completa.
            faixa = (self.carga - cfg.IRIS_AVISO) / max(1e-6, 1.0 - cfg.IRIS_AVISO)
            freq = 3.0 + 6.0 * faixa
            return 0.30 + 0.35 * (0.5 + 0.5 * math.sin(self.pulso_visual * freq))
        return 0.12 + 0.10 * self.carga
