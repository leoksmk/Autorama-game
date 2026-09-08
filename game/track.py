# -*- coding: utf-8 -*-
"""
Traçado da pista.

A posição de cada nave é um t normalizado em [0,1). O traçado converte esse t
em pixels. É só geometria: nada aqui sabe o que é calor, item ou PWM.

Nota para o hardware: t é sempre uma ESTIMATIVA. Hoje ela vem só da integração
de speed * dt. Com sensores na pista, ela continuará vindo da integração entre
checkpoints, e será apenas corrigida quando um sensor disparar
(ver Race.sync_position). Por isso o traçado precisa aceitar qualquer t, e não
supor que o t chegue "certinho" nos pontos de checkpoint.
"""

from __future__ import annotations

import math

from . import config as cfg


def _ponto_base(t: float) -> tuple[float, float]:
    """Ponto da linha de centro da pista para um t normalizado."""
    a = 2.0 * math.pi * t
    r = 1.0 + cfg.PISTA_MODULACAO * math.cos(cfg.PISTA_HARMONICA * a)
    return (
        cfg.PISTA_CX + cfg.PISTA_RX * r * math.cos(a),
        cfg.PISTA_CY + cfg.PISTA_RY * r * math.sin(a),
    )


def tangente(t: float) -> tuple[float, float]:
    """Tangente unitária da curva em t (derivada numérica, boa o bastante)."""
    h = 1e-4
    x0, y0 = _ponto_base(t - h)
    x1, y1 = _ponto_base(t + h)
    dx, dy = x1 - x0, y1 - y0
    n = math.hypot(dx, dy) or 1.0
    return dx / n, dy / n


def ponto(t: float, offset: float = 0.0) -> tuple[float, float]:
    """
    Ponto da faixa deslocada em `offset` pixels pela normal da curva.

    offset positivo empurra para fora do centro da pista.
    """
    t = t % 1.0
    x, y = _ponto_base(t)
    tx, ty = tangente(t)
    # Normal = tangente girada 90°. Com o sentido de percurso desta curva,
    # (ty, -tx) aponta para fora.
    return x + ty * offset, y - tx * offset


def angulo(t: float) -> float:
    """Direção de deslocamento em t, em radianos. Usado para orientar a nave."""
    tx, ty = tangente(t)
    return math.atan2(ty, tx)


def offset_da_faixa(lane: int) -> float:
    """Pista 0 por dentro, pista 1 por fora."""
    return -cfg.FAIXA_OFFSET if lane == 0 else cfg.FAIXA_OFFSET


def poligono_faixa(offset: float, amostras: int | None = None) -> list[tuple[float, float]]:
    """Lista de pontos fechando a volta inteira, para desenho."""
    n = amostras or cfg.PISTA_AMOSTRAS
    return [ponto(i / n, offset) for i in range(n)]


def arco(t_inicio: float, t_fim: float, offset: float, amostras: int = 48) -> list[tuple[float, float]]:
    """Pontos de um trecho da pista, usado para pintar a zona de sombra."""
    span = (t_fim - t_inicio) % 1.0
    return [ponto(t_inicio + span * (i / amostras), offset) for i in range(amostras + 1)]


# Ordem estável, para o índice devolvido ser sempre o mesmo checkpoint.
CHECKPOINTS_ORDENADOS = tuple(cfg.CHECKPOINTS)


def distancia_curta(t_a: float, t_b: float) -> float:
    """
    Menor distância entre duas naves na pista, em voltas, em qualquer sentido.

    Sempre em [0, 0.5]. É o que os ataques usam: perto é perto, esteja o alvo
    à frente ou atrás.
    """
    d = abs(t_a - t_b) % 1.0
    return min(d, 1.0 - d)


def cruzou_checkpoint(t_anterior: float, t_novo: float) -> int | None:
    """
    Devolve o índice do checkpoint cruzado entre dois frames, ou None.

    É a mesma coisa que o sensor físico faz: detecta a PASSAGEM, um evento
    pontual, e não "estar perto". Por isso a janela de roleta é de tempo e não
    de posição — quando os sensores existirem, o pulso de cada um chama isto
    e nada mais na lógica precisa mudar.

    Um avanço grande (ou o wrap da volta) é tratado medindo o deslocamento
    para frente, nunca a diferença crua.
    """
    avanco = (t_novo - t_anterior) % 1.0
    if avanco <= 0.0 or avanco >= 1.0:
        return None
    for i, cp in enumerate(CHECKPOINTS_ORDENADOS):
        # Distância do ponto anterior até o checkpoint, andando para frente.
        ate_cp = (cp - t_anterior) % 1.0
        if 0.0 < ate_cp <= avanco:
            return i
    return None


def dentro_da_sombra(t: float) -> bool:
    """A zona de sombra é um trecho fixo da órbita, definido em config."""
    t = t % 1.0
    if cfg.SOMBRA_INICIO <= cfg.SOMBRA_FIM:
        return cfg.SOMBRA_INICIO <= t <= cfg.SOMBRA_FIM
    # Trecho que cruza a linha de largada
    return t >= cfg.SOMBRA_INICIO or t <= cfg.SOMBRA_FIM
