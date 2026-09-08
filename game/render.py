# -*- coding: utf-8 -*-
"""
Desenho do mundo: campo estelar, anel de detritos, pista, ÍRIS-9 e naves.

Tudo é polígono vetorial desenhado no próprio pygame. Nenhum asset externo,
nenhuma imagem, nenhuma fonte de arquivo.

Estratégia de desempenho, que é o que segura os 60 FPS num Pi 4:

- O que nunca muda (campo estelar, detritos parados, leito da pista, zona de
  sombra, luz da estação no chão, casco da ÍRIS-9) é rasterizado UMA vez em
  Surfaces e depois só blitado.
- O que se mexe é desenhado com primitivas baratas: polígonos e círculos.
- Os brilhos são blits aditivos de sprites cacheados (gfx.GlowCache), nunca
  composição de camadas do tamanho da tela.
"""

from __future__ import annotations

import math
import random

import pygame

from . import config as cfg
from . import track
from .gfx import GlowCache, mistura, rotacionar
from .items import OPORTUNIDADE

# Mantido por compatibilidade com quem já importava daqui.
_mistura = mistura
_rotacionar = rotacionar


# ---------------------------------------------------------------------------
# Cascos (coordenadas locais: +x é a proa)
# ---------------------------------------------------------------------------

# ÍON: casco angular, proa em cunha, asas em delta agudo.
CASCO_ION = [
    (19, 0), (5, -6), (-1, -13), (-9, -10), (-6, -4),
    (-14, -3), (-14, 3), (-6, 4), (-9, 10), (-1, 13), (5, 6),
]
# Chapas internas: dão volume sem custar mais que dois polígonos.
CHAPA_ION = [(13, 0), (2, -4), (-8, -3), (-8, 3), (2, 4)]
COCKPIT_ION = [(9, 0), (2, -3), (-2, 0), (2, 3)]
# Luzes de navegação nas pontas das asas
LUZES_ION = [(-1, -12), (-1, 12)]

# ÍGNIS: casco alongado, fuselagem fina, empenas curtas atrás.
CASCO_IGNIS = [
    (23, 0), (12, -3), (-1, -5), (-8, -12), (-14, -10), (-11, -3),
    (-18, -3), (-18, 3), (-11, 3), (-14, 10), (-8, 12), (-1, 5), (12, 3),
]
CHAPA_IGNIS = [(17, 0), (6, -2), (-10, -2), (-10, 2), (6, 2)]
COCKPIT_IGNIS = [(13, 0), (5, -2), (0, 0), (5, 2)]
LUZES_IGNIS = [(-8, -11), (-8, 11)]

PERFIS = {
    0: (CASCO_ION, CHAPA_ION, COCKPIT_ION, LUZES_ION, -14),
    1: (CASCO_IGNIS, CHAPA_IGNIS, COCKPIT_IGNIS, LUZES_IGNIS, -18),
}

# Quantos passos de rastro cada nave deixa
RASTRO_PASSOS = 10


class Renderer:
    def __init__(self) -> None:
        self.glow = GlowCache()
        self.fundo = self._montar_fundo()
        self.casco_estacao = self._montar_estacao()
        self._t_visual = 0.0

        # Surfaces reaproveitadas. Alocar uma Surface do tamanho da tela por
        # frame é o tipo de coisa que passa despercebida no PC e derruba o
        # frame rate num Pi 4. Alocamos uma vez e só limpamos quando usamos.
        self._buf_feixe = pygame.Surface((cfg.LARGURA, cfg.ALTURA), pygame.SRCALPHA)
        self._buf_halo = pygame.Surface(
            (cfg.IRIS_RAIO * 4, cfg.IRIS_RAIO * 4), pygame.SRCALPHA
        )

        # Rastro: as últimas posições de cada nave, para o desenho da esteira.
        self._rastro: dict[int, list[tuple[float, float, float]]] = {0: [], 1: []}

        # Estrelas que cintilam e detritos que orbitam. Poucos, de propósito:
        # o grosso do campo estelar está na camada estática.
        rng = random.Random(4711)
        self._cintilantes = [
            (rng.randrange(cfg.LARGURA), rng.randrange(cfg.ALTURA),
             rng.uniform(0.6, 2.4), rng.uniform(0, math.tau))
            for _ in range(46)
        ]
        self._detritos = [
            (rng.random(), rng.choice([-1, 1]) * rng.uniform(38, 92),
             rng.uniform(0.006, 0.022), rng.randint(2, 4), rng.uniform(0, math.tau))
            for _ in range(44)
        ]

    # -- camadas estáticas --------------------------------------------------

    def _montar_fundo(self) -> pygame.Surface:
        """Campo estelar + luz da estação + detritos parados + leito da pista."""
        s = pygame.Surface((cfg.LARGURA, cfg.ALTURA))
        s.fill(cfg.COR_FUNDO)

        rng = random.Random(20260908)  # semente fixa: o céu não muda entre partidas

        # Campo estelar em três profundidades
        for _ in range(340):
            x, y = rng.randrange(cfg.LARGURA), rng.randrange(cfg.ALTURA)
            b = rng.randint(26, 120)
            s.set_at((x, y), (b, b, min(255, int(b * 1.2))))
        for _ in range(90):
            x, y = rng.randrange(cfg.LARGURA), rng.randrange(cfg.ALTURA)
            b = rng.randint(110, 190)
            s.set_at((x, y), (b, b, min(255, int(b * 1.1))))
        for _ in range(22):
            x, y = rng.randrange(cfg.LARGURA), rng.randrange(cfg.ALTURA)
            pygame.draw.circle(s, (190, 200, 225), (x, y), 1)

        # Nebulosa fraca: manchas largas e escuras, só para o fundo não ser liso
        neb = pygame.Surface((cfg.LARGURA, cfg.ALTURA), pygame.SRCALPHA)
        for _ in range(16):
            nx = rng.randrange(cfg.LARGURA)
            ny = rng.randrange(cfg.ALTURA)
            raio = rng.randint(90, 230)
            tom = rng.choice([(0x1A, 0x12, 0x3E), (0x10, 0x1C, 0x3A), (0x20, 0x14, 0x30)])
            for i in range(7):
                k = i / 6
                pygame.draw.circle(neb, (*tom, int(7 * (1 - k))),
                                   (nx, ny), int(raio * (1 - k * 0.75)))
        s.blit(neb, (0, 0))

        # Luz da ÍRIS-9 caindo no anel: a estação ilumina o próprio cenário.
        luz = pygame.Surface((cfg.LARGURA, cfg.ALTURA), pygame.SRCALPHA)
        for i in range(20):
            k = i / 19
            raio = int(cfg.IRIS_RAIO * (5.2 - 4.0 * k))
            pygame.draw.circle(luz, (*cfg.COR_ESTACAO, int(3 + 9 * k)),
                               (cfg.PISTA_CX, cfg.PISTA_CY), raio)
        s.blit(luz, (0, 0))

        # Anel de detritos parados
        for _ in range(300):
            t = rng.random()
            desvio = rng.uniform(-104, 104)
            if abs(desvio) < 34:
                continue
            px, py = track.ponto(t, desvio)
            r = rng.randint(1, 5)
            tom = rng.randint(20, 56)
            # Pedras mais perto da estação recebem um resto de luz roxa
            luz_k = max(0.0, 1.0 - abs(desvio) / 110)
            cor = mistura((tom, tom + 6, tom + 16), (0x3E, 0x2E, 0x6A), luz_k * 0.35)
            lados = rng.randint(5, 7)
            giro = rng.uniform(0, math.tau)
            pontos = [
                (px + math.cos(giro + i * math.tau / lados) * r * rng.uniform(0.7, 1.3),
                 py + math.sin(giro + i * math.tau / lados) * r * rng.uniform(0.7, 1.3))
                for i in range(lados)
            ]
            pygame.draw.polygon(s, cor, pontos)

        # Leito da pista. Montado como ANEL numa surface própria: se pintássemos
        # o miolo com a cor de fundo, apagaríamos as estrelas de dentro da órbita
        # e o centro da tela viraria um buraco chapado.
        borda_int = track.poligono_faixa(-cfg.FAIXA_OFFSET - 13)
        borda_ext = track.poligono_faixa(cfg.FAIXA_OFFSET + 13)
        anel = pygame.Surface((cfg.LARGURA, cfg.ALTURA), pygame.SRCALPHA)
        pygame.draw.polygon(anel, (*cfg.COR_PISTA, 255), borda_ext)

        # Sombreado do leito: mais claro do lado voltado para a estação.
        n = 180
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            px, py = track.ponto((t0 + t1) / 2, 0)
            # Ângulo entre a normal da pista e a direção da estação
            dx, dy = cfg.PISTA_CX - px, cfg.PISTA_CY - py
            d = math.hypot(dx, dy) or 1.0
            k = max(0.0, min(1.0, 1.0 - d / 460))
            cor = mistura(cfg.COR_PISTA, (0x28, 0x2E, 0x52), k)
            quad = (track.arco(t0, t1, cfg.FAIXA_OFFSET + 13, 2)
                    + list(reversed(track.arco(t0, t1, -cfg.FAIXA_OFFSET - 13, 2))))
            pygame.draw.polygon(anel, (*cor, 255), quad)

        # Zona de sombra: só faz sentido enquanto a varredura existir.
        if cfg.IRIS_EVENTO_ATIVO:
            sombra_ext = track.arco(cfg.SOMBRA_INICIO, cfg.SOMBRA_FIM, cfg.FAIXA_OFFSET + 13, 64)
            sombra_int = track.arco(cfg.SOMBRA_INICIO, cfg.SOMBRA_FIM, -cfg.FAIXA_OFFSET - 13, 64)
            pygame.draw.polygon(anel, (*cfg.COR_SOMBRA, 255),
                                sombra_ext + list(reversed(sombra_int)))
            for i in range(0, 65, 3):
                t = cfg.SOMBRA_INICIO + (cfg.SOMBRA_FIM - cfg.SOMBRA_INICIO) * (i / 64)
                pygame.draw.line(
                    anel, (0x27, 0x5A, 0x50, 255),
                    track.ponto(t, -cfg.FAIXA_OFFSET - 12),
                    track.ponto(t, cfg.FAIXA_OFFSET + 12), 1,
                )

        # Divisória entre as duas faixas, tracejada
        for i in range(0, 240, 2):
            t0, t1 = i / 240, (i + 0.85) / 240
            pygame.draw.line(anel, (0x30, 0x3D, 0x5C, 255),
                             track.ponto(t0, 0), track.ponto(t1, 0), 1)

        # Fura o miolo: cor RGBA numa surface SRCALPHA substitui o pixel, então
        # este polígono devolve alpha 0 e o campo estelar reaparece por dentro.
        pygame.draw.polygon(anel, (0, 0, 0, 0), borda_int)
        s.blit(anel, (0, 0))

        # Guias das bordas
        pygame.draw.aalines(s, cfg.COR_LINHA, True, borda_ext)
        pygame.draw.aalines(s, cfg.COR_LINHA, True, borda_int)

        # Marcas de checkpoint, com número
        for i, cp in enumerate(cfg.CHECKPOINTS):
            a = track.ponto(cp, -cfg.FAIXA_OFFSET - 13)
            b = track.ponto(cp, cfg.FAIXA_OFFSET + 13)
            pygame.draw.line(s, (0x46, 0x5C, 0x86), a, b, 2)
            # Pequenas marcas nas bordas, para a linha ser legível de longe
            for lado, ponto in ((-1, a), (1, b)):
                fora = track.ponto(cp, lado * (cfg.FAIXA_OFFSET + 21))
                pygame.draw.line(s, (0x5A, 0x74, 0xA8), ponto, fora, 2)

        # Linha de largada / chegada em t = 0: dois xadrezes atravessando o leito
        larg = cfg.FAIXA_OFFSET + 13
        colunas = 8
        for fila in range(2):
            t0 = -0.0042 + fila * 0.0042
            t1 = t0 + 0.0042
            for col in range(colunas):
                o0 = -larg + 2 * larg * col / colunas
                o1 = -larg + 2 * larg * (col + 1) / colunas
                cor = cfg.COR_TEXTO if (fila + col) % 2 == 0 else (0x2A, 0x33, 0x4A)
                pygame.draw.polygon(s, cor, [
                    track.ponto(t0, o0), track.ponto(t1, o0),
                    track.ponto(t1, o1), track.ponto(t0, o1),
                ])

        return s

    def _montar_estacao(self) -> pygame.Surface:
        """
        Casco da ÍRIS-9: esfera coberta de placas hexagonais.

        Sem trincheira equatorial e sem canhão. A abertura em diafragma é
        desenhada por cima, a cada frame, porque ela se mexe.
        """
        r = cfg.IRIS_RAIO
        lado = r * 2 + 8
        s = pygame.Surface((lado, lado), pygame.SRCALPHA)
        c = lado / 2

        pygame.draw.circle(s, (0x14, 0x10, 0x2E), (c, c), r)

        # Grade hexagonal. As placas passam da borda e depois são recortadas
        # pela máscara circular, senão sobra um anel escuro em volta.
        hx = 11.0
        passo_x = hx * 1.5
        passo_y = hx * math.sqrt(3)
        linhas = int(lado / passo_y) + 2
        colunas = int(lado / passo_x) + 2
        rng = random.Random(99)
        for col in range(-1, colunas):
            for lin in range(-1, linhas):
                px = col * passo_x
                py = lin * passo_y + (passo_y / 2 if col % 2 else 0)
                if math.hypot(px - c, py - c) > r + hx:
                    continue
                # Sombreamento esférico: luz vindo de cima à esquerda.
                # O piso de 0,18 impede que o lado escuro vire um buraco preto.
                nx = max(-1.0, min(1.0, (px - c) / r))
                ny = max(-1.0, min(1.0, (py - c) / r))
                nz = math.sqrt(max(0.0, 1.0 - nx * nx - ny * ny))
                luz = max(0.0, (-nx * 0.48 - ny * 0.56 + nz * 0.68))
                # Variação de placa para placa, para a superfície não ser lisa
                ruido = rng.uniform(-0.05, 0.05)
                cor = mistura((0x1A, 0x13, 0x36), cfg.COR_ESTACAO,
                              max(0.0, 0.18 + luz * 0.78 + ruido))
                pontos = [
                    (px + hx * 0.92 * math.cos(i * math.tau / 6),
                     py + hx * 0.92 * math.sin(i * math.tau / 6))
                    for i in range(6)
                ]
                pygame.draw.polygon(s, cor, pontos)
                # Junta entre placas
                pygame.draw.polygon(s, mistura(cor, (0, 0, 0), 0.45), pontos, 1)

        # Realce especular no ponto de luz
        espec = pygame.Surface((lado, lado), pygame.SRCALPHA)
        ex, ey = c - r * 0.38, c - r * 0.44
        for i in range(9):
            k = i / 8
            pygame.draw.circle(espec, (0xC8, 0xB8, 0xFF, int(4 + 14 * k)),
                               (int(ex), int(ey)), int(r * (0.52 - 0.42 * k)))
        s.blit(espec, (0, 0))

        # Recorte circular
        mascara = pygame.Surface((lado, lado), pygame.SRCALPHA)
        pygame.draw.circle(mascara, (255, 255, 255, 255), (c, c), r)
        s.blit(mascara, (0, 0), special_flags=pygame.BLEND_RGBA_MIN)

        # Vinheta na borda, para a esfera não parecer um disco chapado
        vinheta = pygame.Surface((lado, lado), pygame.SRCALPHA)
        for i in range(14):
            k = i / 14
            pygame.draw.circle(vinheta, (0x04, 0x02, 0x10, int(16 + 40 * k)),
                               (c, c), int(r - i * 1.6), 3)
        s.blit(vinheta, (0, 0))

        pygame.draw.circle(s, (0x5A, 0x46, 0xB8), (c, c), r, 2)
        return s

    # -- camadas dinâmicas --------------------------------------------------

    def desenhar_mundo(self, tela: pygame.Surface, race, dt: float, correndo: bool = True) -> None:
        self._t_visual += dt
        tela.blit(self.fundo, (0, 0))
        self._desenhar_cintilancia(tela)
        self._desenhar_detritos(tela, dt if correndo else 0.0)
        self._desenhar_estacao(tela, race.iris)
        self._desenhar_checkpoints_ativos(tela, race)
        for nave in race.naves:
            self._registrar_rastro(nave, correndo)
        for nave in race.naves:
            self._desenhar_rastro(tela, nave)
        for nave in race.naves:
            self._desenhar_nave(tela, nave)

    def _desenhar_cintilancia(self, tela) -> None:
        """Algumas estrelas piscam. O resto do céu está na camada estática."""
        for x, y, vel, fase in self._cintilantes:
            b = 0.5 + 0.5 * math.sin(self._t_visual * vel + fase)
            if b < 0.55:
                continue
            tom = int(70 + 150 * b)
            tela.set_at((x, y), (tom, tom, min(255, int(tom * 1.1))))
            if b > 0.9:
                self.glow.blit(tela, (tom // 3, tom // 3, tom // 2), 2, x, y)

    def _desenhar_detritos(self, tela, dt: float) -> None:
        """Uma minoria dos detritos orbita, para o anel não parecer congelado."""
        for i, (t0, desvio, vel, r, giro) in enumerate(self._detritos):
            t = (t0 + self._t_visual * vel) % 1.0
            px, py = track.ponto(t, desvio)
            luz_k = max(0.0, 1.0 - abs(desvio) / 110)
            tom = 34 + (i % 5) * 6
            cor = mistura((tom, tom + 6, tom + 16), (0x46, 0x34, 0x78), luz_k * 0.4)
            a = giro + self._t_visual * 0.5
            pontos = [
                (px + math.cos(a + j * math.tau / 5) * r,
                 py + math.sin(a + j * math.tau / 5) * r * 0.8)
                for j in range(5)
            ]
            pygame.draw.polygon(tela, cor, pontos)

    def _desenhar_estacao(self, tela: pygame.Surface, iris) -> None:
        cx, cy = cfg.PISTA_CX, cfg.PISTA_CY
        r = cfg.IRIS_RAIO

        # Halo, mais forte durante o aviso
        if iris.avisando or iris.varrendo:
            faixa = 1.0 if iris.varrendo else (iris.carga - cfg.IRIS_AVISO) / (1.0 - cfg.IRIS_AVISO)
            pulso = 0.5 + 0.5 * math.sin(self._t_visual * (4 + 8 * faixa))
            halo = self._buf_halo
            halo.fill((0, 0, 0, 0))
            base = 10 + 26 * faixa * pulso
            for i in range(7):
                k = i / 6
                raio = int(r * (1.62 - 0.52 * k))
                pygame.draw.circle(halo, (*cfg.COR_ESTACAO, int(base * (0.35 + 0.65 * k))),
                                   (r * 2, r * 2), raio)
            tela.blit(halo, (cx - r * 2, cy - r * 2))

        tela.blit(self.casco_estacao, (cx - r - 4, cy - r - 4))

        # Anel de luzes de sinalização girando em volta do equador aparente.
        # É o que faz a estação parecer uma instalação, e não uma bola.
        n_luzes = 14
        for i in range(n_luzes):
            a = self._t_visual * 0.22 + i * math.tau / n_luzes
            # Elipse achatada: sugere um anel em perspectiva
            lx = cx + math.cos(a) * (r * 0.98)
            ly = cy + math.sin(a) * (r * 0.30)
            atras = math.sin(a) < 0
            piscando = (i % 4 == 0) and math.sin(self._t_visual * 5 + i) > 0.2
            if piscando:
                cor = (0xFF, 0xC8, 0x88) if not atras else (0x6E, 0x54, 0x40)
                raio = 2 if atras else 3
            else:
                cor = (0x9E, 0x88, 0xE8) if not atras else (0x4A, 0x3C, 0x76)
                raio = 1 if atras else 2
            pygame.draw.circle(tela, cor, (int(lx), int(ly)), raio)
            if not atras and piscando:
                self.glow.blit(tela, (0x50, 0x38, 0x20), 3, lx, ly)

        # Abertura em diafragma
        abertura = iris.abertura()
        raio_boca = r * 0.46
        laminas = 8
        vazio = raio_boca * abertura

        pygame.draw.circle(tela, (0x0B, 0x07, 0x1E), (cx, cy), int(raio_boca) + 2)
        if vazio > 1:
            brilho = (0xFF, 0xE8, 0xB0) if iris.varrendo else mistura(
                (0x6B, 0x4A, 0xC8), (0xFF, 0xC4, 0x6A), abertura
            )
            pygame.draw.circle(tela, mistura(brilho, (0, 0, 0), 0.45),
                               (cx, cy), int(vazio) + 3)
            pygame.draw.circle(tela, brilho, (cx, cy), int(vazio))
            self.glow.blit(tela, mistura(brilho, (0, 0, 0), 0.55),
                           int(vazio) + 6, cx, cy)

        # Cada lâmina é uma pá que avança sobre o centro. Quanto mais fechado,
        # mais elas se sobrepõem; quanto mais aberto, mais recuam para a borda.
        giro = self._t_visual * 0.35 + abertura * 0.55
        passo = math.tau / laminas
        for i in range(laminas):
            a0 = giro + i * passo
            a1 = a0 + passo * 1.55       # >1 para as pás se sobreporem
            r_ext = raio_boca * 1.1
            pontos = [
                (cx + math.cos(a0) * r_ext, cy + math.sin(a0) * r_ext),
                (cx + math.cos(a1) * r_ext, cy + math.sin(a1) * r_ext),
                (cx + math.cos(a1) * vazio, cy + math.sin(a1) * vazio),
                (cx + math.cos(a0 + passo * 0.25) * vazio,
                 cy + math.sin(a0 + passo * 0.25) * vazio),
            ]
            claro = 0.45 + 0.42 * (0.5 + 0.5 * math.cos(a0 + 2.4))
            pygame.draw.polygon(tela, mistura((0x2A, 0x20, 0x54), (0x9E, 0x87, 0xF0), claro), pontos)
            pygame.draw.line(tela, (0xC3, 0xB4, 0xFF), pontos[1], pontos[2], 1)

        pygame.draw.circle(tela, (0x5A, 0x46, 0xB8), (cx, cy), int(raio_boca) + 2, 2)

        if iris.varrendo:
            self._desenhar_feixe(tela, iris)

    def _desenhar_feixe(self, tela: pygame.Surface, iris) -> None:
        cx, cy = cfg.PISTA_CX, cfg.PISTA_CY
        t = iris.varredura_t
        feixe = self._buf_feixe
        feixe.fill((0, 0, 0, 0))

        # Leque: o rastro do feixe desbotando atrás da frente
        for i in range(16):
            tt = t - i * 0.011
            if tt < 0:
                break
            alpha = int(130 * (1 - i / 16) ** 1.5)
            largura = 3 + i // 3
            pygame.draw.line(feixe, (0xFF, 0xD8, 0x88, alpha),
                             (cx, cy), track.ponto(tt, cfg.FAIXA_OFFSET + 40), largura)

        ponta = track.ponto(t, cfg.FAIXA_OFFSET + 40)
        pygame.draw.line(feixe, (0xFF, 0xF2, 0xCC, 220), (cx, cy), ponta, 3)
        tela.blit(feixe, (0, 0))
        # Cabeça do feixe, aditiva: é o ponto mais brilhante da tela
        self.glow.blit(tela, (0xFF, 0xE8, 0xB0), 10, ponta[0], ponta[1])

    def _desenhar_checkpoints_ativos(self, tela: pygame.Surface, race) -> None:
        """
        Acende a linha do checkpoint que abriu a janela de roleta.

        Só o checkpoint efetivamente cruzado acende, e só enquanto a janela
        estiver aberta: é o mesmo evento que a roleta na tela está mostrando.
        """
        for nave in race.naves:
            r = nave.roleta
            if r.estado != OPORTUNIDADE or r.checkpoint is None:
                continue
            cp = cfg.CHECKPOINTS[r.checkpoint]
            off = track.offset_da_faixa(nave.lane)
            a = track.ponto(cp, off - 13)
            b = track.ponto(cp, off + 13)
            pulso = 0.55 + 0.45 * math.sin(self._t_visual * 14)
            pygame.draw.line(tela, mistura(cfg.COR_PISTA, nave.cor, pulso), a, b, 4)
            self.glow.blit(tela, mistura((0, 0, 0), nave.cor, 0.35 * pulso), 7,
                           (a[0] + b[0]) / 2, (a[1] + b[1]) / 2)

    # -- naves ---------------------------------------------------------------

    def _registrar_rastro(self, nave, correndo: bool) -> None:
        hist = self._rastro[nave.lane]
        if not correndo:
            hist.clear()
            return
        x, y = track.ponto(nave.t, track.offset_da_faixa(nave.lane))
        hist.append((x, y, track.angulo(nave.t)))
        del hist[:-RASTRO_PASSOS]

    def _desenhar_rastro(self, tela, nave) -> None:
        """
        Esteira do motor: só aparece com PWM, e o tamanho segue o PWM.

        É o mesmo dado da telemetria contado de outro jeito — dá para ver quem
        está no Impulso e quem está cortado sem olhar o rodapé.
        """
        hist = self._rastro[nave.lane]
        if nave.pwm <= 0.01 or len(hist) < 3:
            return
        cor = nave.cor
        for i, (x, y, _a) in enumerate(hist[:-1]):
            f = (i + 1) / len(hist)
            raio = 1 + 4.5 * f * nave.pwm
            self.glow.blit(tela, mistura((0, 0, 0), cor, f * f * 0.55), int(raio), x, y)

    def _desenhar_nave(self, tela: pygame.Surface, nave) -> None:
        off = track.offset_da_faixa(nave.lane)
        x, y = track.ponto(nave.t, off)
        ang = track.angulo(nave.t)

        # Tremor de impacto: enquanto o flash dura, a nave chacoalha. É o que
        # faz "levou um tiro" ser sentido e não só lido.
        if nave.flash > 0.0:
            amp = 4.0 * nave.flash
            x += math.sin(self._t_visual * 90) * amp
            y += math.cos(self._t_visual * 76) * amp
        casco, chapa, cockpit, luzes, traseira = PERFIS[nave.lane]
        escala = 1.05

        # Sombra projetada no leito, deslocada para longe da estação
        dx, dy = x - cfg.PISTA_CX, y - cfg.PISTA_CY
        d = math.hypot(dx, dy) or 1.0
        sx, sy = x + dx / d * 5, y + dy / d * 5
        pygame.draw.polygon(tela, (0x0D, 0x13, 0x22),
                            rotacionar(casco, ang, sx, sy, escala * 0.94))

        # Chama do motor, proporcional ao PWM: o jogador vê o atuador.
        if nave.pwm > 0.01:
            comprimento = 10 + 26 * nave.pwm
            tremor = 1.0 + 0.18 * math.sin(self._t_visual * 40 + nave.lane)
            cor_chama = mistura(nave.cor, (0xFF, 0xFF, 0xFF), 0.35)
            # Pluma externa
            pygame.draw.polygon(tela, mistura(cor_chama, (0x30, 0x12, 0x08), 0.45), rotacionar(
                [(traseira, -6.0), (traseira - comprimento * 1.25 * tremor, 0), (traseira, 6.0)],
                ang, x, y, escala))
            # Núcleo
            pygame.draw.polygon(tela, cor_chama, rotacionar(
                [(traseira, -4.5), (traseira - comprimento * tremor, 0), (traseira, 4.5)],
                ang, x, y, escala))
            bx, by = rotacionar([(traseira - 3, 0)], ang, x, y, escala)[0]
            self.glow.blit(tela, mistura((0, 0, 0), cor_chama, 0.45 + 0.35 * nave.pwm),
                           int(6 + 7 * nave.pwm), bx, by)

        # Aura de estado, desenhada por baixo do casco
        if nave.superaquecimento > 0.0:
            self.glow.blit(tela, (0x60, 0x14, 0x1E), 16, x, y)
        elif nave.atordoado > 0.0:
            self.glow.blit(tela, (0x30, 0x38, 0x60), 14, x, y)
        elif nave.lento > 0.0:
            self.glow.blit(tela, (0x28, 0x2C, 0x34), 13, x, y)

        corpo = rotacionar(casco, ang, x, y, escala)
        cor_corpo = nave.cor
        if nave.flash > 0.0:
            cor_corpo = mistura(cor_corpo, (0xFF, 0xFF, 0xFF), min(1.0, nave.flash * 2.4))

        # Casco em três tons: barriga escura, chapa média, contorno claro.
        pygame.draw.polygon(tela, mistura(cor_corpo, cfg.COR_FUNDO, 0.66), corpo)
        pygame.draw.polygon(tela, mistura(cor_corpo, cfg.COR_FUNDO, 0.34),
                            rotacionar(chapa, ang, x, y, escala))
        pygame.draw.polygon(tela, cor_corpo, corpo, 2)

        # Cockpit aceso
        cabine = rotacionar(cockpit, ang, x, y, escala)
        pygame.draw.polygon(tela, mistura(cor_corpo, (0xFF, 0xFF, 0xFF), 0.72), cabine)
        self.glow.blit(tela, mistura((0, 0, 0), cor_corpo, 0.30), 4, x, y)

        # Luzes de navegação nas pontas, piscando fora de fase entre as naves
        pisca = math.sin(self._t_visual * 4.5 + nave.lane * 2.1) > 0.35
        if pisca:
            for lx, ly in rotacionar(luzes, ang, x, y, escala):
                pygame.draw.circle(tela, (0xFF, 0xFF, 0xFF), (int(lx), int(ly)), 2)
                self.glow.blit(tela, (0x38, 0x3E, 0x48), 3, lx, ly)

        # Escudo carregado: bolha hexagonal girando devagar em volta da nave.
        # Quando ele bloqueia um ataque, effects.py acende uma bolha bem mais
        # forte por cima desta.
        if nave.escudo:
            fase = self._t_visual * 1.1
            cor_escudo = cfg.ITEM_CORES["escudo"]
            pontos = [
                (x + math.cos(fase + i * math.tau / 6) * 25,
                 y + math.sin(fase + i * math.tau / 6) * 25) for i in range(6)
            ]
            brilho = 0.45 + 0.35 * math.sin(self._t_visual * 3)
            pygame.draw.polygon(tela, mistura(cor_escudo, (0xFF, 0xFF, 0xFF), brilho),
                                pontos, 2)
