# -*- coding: utf-8 -*-
"""
Utilitários de desenho compartilhados por render, effects e hud.

Nada aqui conhece o jogo: são só cores, geometria e sprites de brilho.
"""

from __future__ import annotations

import math

import pygame


def mistura(a, b, k: float):
    """Interpola duas cores. k=0 devolve a, k=1 devolve b."""
    k = 0.0 if k < 0.0 else (1.0 if k > 1.0 else k)
    return (
        int(a[0] + (b[0] - a[0]) * k),
        int(a[1] + (b[1] - a[1]) * k),
        int(a[2] + (b[2] - a[2]) * k),
    )


def rotacionar(pontos, ang: float, cx: float, cy: float, escala: float = 1.0):
    ca, sa = math.cos(ang), math.sin(ang)
    return [
        (cx + (x * ca - y * sa) * escala, cy + (x * sa + y * ca) * escala)
        for x, y in pontos
    ]


class GlowCache:
    """
    Sprites de brilho radial para blit aditivo.

    O truque que mantém isso barato: num blit aditivo, uma cor mais escura já
    É um brilho mais fraco. Então o fade de uma partícula não precisa mexer em
    alpha — basta escurecer a cor antes de pedir o sprite. Com a cor
    quantizada em passos de 16, o cache converge para poucas dezenas de
    sprites mesmo com centenas de partículas em tela.
    """

    def __init__(self, limite: int = 700) -> None:
        self._cache: dict[tuple, pygame.Surface] = {}
        self._limite = limite

    def get(self, cor, raio: int) -> pygame.Surface:
        raio = max(1, int(raio))
        cor = (int(cor[0]) & 0xF0, int(cor[1]) & 0xF0, int(cor[2]) & 0xF0)
        chave = (cor, raio)
        s = self._cache.get(chave)
        if s is None:
            lado = raio * 4
            s = pygame.Surface((lado, lado), pygame.SRCALPHA)
            c = lado // 2
            for i in range(6):
                k = i / 5
                rr = int(raio * (2.0 - 1.7 * k))
                if rr <= 0:
                    continue
                pygame.draw.circle(s, mistura((0, 0, 0), cor, 0.10 + 0.90 * k), (c, c), rr)
            if len(self._cache) > self._limite:
                self._cache.clear()
            self._cache[chave] = s
        return s

    def blit(self, tela: pygame.Surface, cor, raio: int, x: float, y: float) -> None:
        s = self.get(cor, raio)
        tela.blit(s, (x - s.get_width() / 2, y - s.get_height() / 2),
                  special_flags=pygame.BLEND_ADD)
