# -*- coding: utf-8 -*-
"""
Animações de efeito.

Camada puramente visual: nada aqui altera o estado do jogo. A Race decide o que
aconteceu e chama um emissor daqui; este módulo só desenha a consequência.
Se você apagar este arquivo, a corrida continua funcionando igual — o que é o
teste de que a separação está certa.

Desempenho: as partículas são desenhadas como blits aditivos de sprites de
brilho pré-renderizados e cacheados. Nada de criar Surface por partícula nem
por frame, e nada de compor uma camada de tela cheia — no Pi 4 isso é a
diferença entre 60 e 35 FPS.
"""

from __future__ import annotations

import math
import random

import pygame

from . import config as cfg
from . import track
from .gfx import GlowCache, mistura as _mistura
from .items import cor_do_item, nome_legivel

# Teto de partículas vivas. Estourou, as mais velhas morrem: um efeito bonito
# nunca pode custar o frame rate.
MAX_PARTICULAS = 320


class _Particula:
    __slots__ = ("x", "y", "vx", "vy", "vida", "vida_max", "cor", "raio", "arrasto", "grav")

    def __init__(self, x, y, vx, vy, vida, cor, raio, arrasto=1.6, grav=0.0):
        self.x, self.y = x, y
        self.vx, self.vy = vx, vy
        self.vida = self.vida_max = vida
        self.cor = cor
        self.raio = raio
        self.arrasto = arrasto
        self.grav = grav


class _Onda:
    """Anel que se expande. Se tiver `lane`, acompanha a nave."""

    __slots__ = ("lane", "x", "y", "r0", "r1", "vida", "vida_max", "cor", "largura")

    def __init__(self, lane, x, y, r0, r1, vida, cor, largura=3):
        self.lane = lane
        self.x, self.y = x, y
        self.r0, self.r1 = r0, r1
        self.vida = self.vida_max = vida
        self.cor = cor
        self.largura = largura


class _Projetil:
    """
    Viaja pela pista de uma posição a outra, seguindo o traçado.

    Só voa e some. Quem decide o que acontece na chegada é a Race, que chama
    impacto() no mesmo instante em que aplica o efeito no PWM — o tempo de voo
    é regra de jogo (cfg.TIRO_VOO / cfg.BOMBA_VOO), não enfeite.
    """

    __slots__ = ("t0", "t1", "off", "off1", "vida", "vida_max", "cor",
                 "tipo", "rastro")

    def __init__(self, t0, t1, off, off1, vida, cor, tipo):
        self.t0, self.t1 = t0, t1
        self.off, self.off1 = off, off1
        self.vida = self.vida_max = vida
        self.cor = cor
        self.tipo = tipo                # "tiro" ou "bomba"
        self.rastro: list[tuple[float, float]] = []


class _Marcador:
    """
    Texto curto que sobe da nave e desvanece, EM CIMA DA PISTA.

    É o feedback que o jogador lê sem tirar os olhos da corrida: quem levou,
    quem bloqueou. O cartão lateral continua existindo para o prêmio da roleta,
    que é informação do painel e não da pista.
    """

    __slots__ = ("texto", "cor", "lane", "x", "y", "vida", "vida_max")

    def __init__(self, texto, cor, lane, x, y, vida):
        self.texto = texto
        self.cor = cor
        self.lane = lane
        self.x, self.y = x, y
        self.vida = self.vida_max = vida


class _Escudo:
    """Bolha hexagonal acendendo sobre a nave — o escudo entrando em ação."""

    __slots__ = ("lane", "vida", "vida_max")

    def __init__(self, lane, vida):
        self.lane = lane
        self.vida = self.vida_max = vida


class _Anuncio:
    """Cartão que sobe e desvanece do lado do jogador."""

    __slots__ = ("texto", "sub", "cor", "lane", "vida", "vida_max")

    def __init__(self, texto, sub, cor, lane, vida):
        self.texto = texto
        self.sub = sub
        self.cor = cor
        self.lane = lane
        self.vida = self.vida_max = vida


class Effects:
    def __init__(self) -> None:
        self.particulas: list[_Particula] = []
        self.ondas: list[_Onda] = []
        self.projeteis: list[_Projetil] = []
        self.marcadores: list[_Marcador] = []
        self.escudos: list[_Escudo] = []
        self.anuncios: list[_Anuncio] = []
        self.flash = 0.0
        self.flash_cor = (255, 255, 255)

        self.glow = GlowCache()
        self._cache_texto: dict[tuple, pygame.Surface] = {}
        self._f_titulo = pygame.font.Font(None, 40)
        self._f_sub = pygame.font.Font(None, 21)
        self._f_marca = pygame.font.Font(None, 24)
        self._rng = random.Random()

    def limpar(self) -> None:
        self.particulas.clear()
        self.ondas.clear()
        self.projeteis.clear()
        self.marcadores.clear()
        self.escudos.clear()
        self.anuncios.clear()
        self.flash = 0.0

    # -- sprites cacheados ---------------------------------------------------

    def _texto(self, fonte, txt, cor) -> pygame.Surface:
        chave = (id(fonte), txt, cor)
        s = self._cache_texto.get(chave)
        if s is None:
            s = fonte.render(txt, True, cor)
            if len(self._cache_texto) > 300:
                self._cache_texto.clear()
            self._cache_texto[chave] = s
        return s

    # -- emissão de partículas ----------------------------------------------

    def _pos(self, nave) -> tuple[float, float]:
        return track.ponto(nave.t, track.offset_da_faixa(nave.lane))

    def _jorro(self, x, y, n, cor, vel, vida, raio=3, espalha=math.tau, dir=0.0, arrasto=1.6):
        rng = self._rng
        for _ in range(n):
            a = dir + rng.uniform(-espalha / 2, espalha / 2)
            v = vel * rng.uniform(0.35, 1.0)
            self.particulas.append(_Particula(
                x, y, math.cos(a) * v, math.sin(a) * v,
                vida * rng.uniform(0.6, 1.0),
                cor, raio * rng.uniform(0.6, 1.3), arrasto,
            ))
        # Poda pelas mais velhas se estourou o teto.
        excesso = len(self.particulas) - MAX_PARTICULAS
        if excesso > 0:
            del self.particulas[:excesso]

    # -- emissores de alto nível --------------------------------------------

    def anunciar(self, texto: str, sub: str, cor, lane: int, vida: float = 1.7) -> None:
        # Um anúncio por jogador de cada vez: o mais novo substitui o anterior.
        self.anuncios = [a for a in self.anuncios if a.lane != lane]
        self.anuncios.append(_Anuncio(texto, sub, cor, lane, vida))

    def marcar(self, nave, texto: str, cor, vida: float = 1.0) -> None:
        """Marcador flutuante ancorado na posição atual da nave."""
        x, y = self._pos(nave)
        self.marcadores = [m for m in self.marcadores if m.lane != nave.lane]
        self.marcadores.append(_Marcador(texto, cor, nave.lane, x, y, vida))

    def premio(self, nave, item: str) -> None:
        """Caixa parou. Um prêmio jorra; a face vazia solta só fumaça."""
        x, y = self._pos(nave)
        cor = cor_do_item(item)
        if item == "nada":
            self._jorro(x, y, 12, (0x5A, 0x62, 0x72), 60, 1.0, raio=4, arrasto=0.8)
            self.marcar(nave, "nada", cfg.COR_TEXTO_FRACO, 1.0)
        else:
            self._jorro(x, y, 26, cor, 190, 0.8, raio=3)
            self.ondas.append(_Onda(nave.lane, x, y, 10, 46, 0.5, cor, 3))
        self.anunciar(nome_legivel(item), cfg.ITEM_DESCRICOES.get(item, ""), cor, nave.lane)

    def ataque(self, origem, alvo, tipo: str, resultado: str) -> None:
        """
        Tiro ou bomba saindo da nave e viajando pela pista até o alvo.

        `resultado` é "errou" (a mira já falhou no disparo) ou "voando". No
        segundo caso a Race chama impacto() quando o voo termina — e o tempo
        de voo aqui usa a MESMA constante que a Race usa, para o estouro na
        tela cair no mesmo frame em que o PWM do alvo muda.
        """
        cor = cor_do_item(tipo)
        off0 = track.offset_da_faixa(origem.lane)
        off1 = track.offset_da_faixa(alvo.lane)
        ox, oy = self._pos(origem)

        # Coice do disparo, na própria nave que atirou
        ang = track.angulo(origem.t)
        self._jorro(ox, oy, 10, cor, 120, 0.3, raio=2.4, espalha=1.0, dir=ang)
        self.ondas.append(_Onda(origem.lane, ox, oy, 6, 26, 0.28, cor, 2))

        if resultado == "errou":
            self.projeteis.append(_Projetil(
                origem.t, origem.t + 0.16, off0, off0, 0.4, cor, tipo))
            self.marcar(origem, "errou", cfg.COR_TEXTO_FRACO, 0.9)
            self.anunciar(nome_legivel(tipo), "passou longe", cfg.COR_TEXTO_FRACO,
                          origem.lane, 1.2)
            return

        # Bomba voa mais devagar que o tiro: dá para ver ela chegando — e dá
        # para o adversário levantar o Escudo antes de ela cair.
        voo = cfg.BOMBA_VOO if tipo == "bomba" else cfg.TIRO_VOO
        self.projeteis.append(_Projetil(
            origem.t, alvo.t, off0, off1, voo, cor, tipo))
        self.anunciar(nome_legivel(tipo), f"a caminho de {alvo.nome}", cor,
                      origem.lane, 1.4)

    def impacto(self, alvo, tipo: str, resultado: str) -> None:
        """
        Chegada do projétil no alvo: explosão ou deflexão pelo escudo.

        Tudo acontece na posição da nave, sobre a pista — é ali que o jogador
        está olhando.
        """
        x, y = self._pos(alvo)
        cor = cor_do_item(tipo)
        # `flash` e um campo declaradamente visual da Ship; o render o usa para
        # sacudir a nave. Reacende-lo aqui garante que o tremor caia no frame
        # da chegada, e nao no do disparo.
        alvo.flash = max(alvo.flash, 0.30 if resultado == "bloqueado" else 0.40)

        if resultado == "bloqueado":
            # Bolha de escudo acendendo e estilhaços ricocheteando
            escudo_cor = cor_do_item("escudo")
            self.ondas.append(_Onda(alvo.lane, x, y, 34, 20, 0.35, (0xFF, 0xFF, 0xFF), 4))
            self.ondas.append(_Onda(alvo.lane, x, y, 22, 46, 0.5, escudo_cor, 3))
            self.escudos.append(_Escudo(alvo.lane, 0.55))
            self._jorro(x, y, 16, escudo_cor, 190, 0.45, raio=2.6)
            self.marcar(alvo, "bloqueado", escudo_cor, 1.1)
            return

        if tipo == "bomba":
            # Explosão: clarão, dois anéis e muitos fragmentos
            self.ondas.append(_Onda(alvo.lane, x, y, 6, 74, 0.42, (0xFF, 0xF4, 0xD0), 5))
            self.ondas.append(_Onda(alvo.lane, x, y, 10, 52, 0.55, cor, 3))
            self._jorro(x, y, 34, (0xFF, 0xC4, 0x60), 250, 0.6, raio=3.6)
            self._jorro(x, y, 14, (0x6A, 0x5E, 0x58), 70, 1.2, raio=5, arrasto=0.7)
            self.marcar(alvo, "parado!", cfg.COR_ALERTA, 1.3)
        else:
            self.ondas.append(_Onda(alvo.lane, x, y, 6, 48, 0.36, (0xFF, 0xD8, 0xC8), 4))
            self._jorro(x, y, 22, cor, 180, 0.5, raio=3)
            self.marcar(alvo, "atingido", cfg.COR_ALERTA, 1.1)

    def escudo(self, nave) -> None:
        x, y = self._pos(nave)
        cor = cor_do_item("escudo")
        self.ondas.append(_Onda(nave.lane, x, y, 60, 24, 0.55, cor, 3))
        self.ondas.append(_Onda(nave.lane, x, y, 42, 24, 0.7, (0xE0, 0xFF, 0xFA), 1))
        self.escudos.append(_Escudo(nave.lane, 0.5))
        self.marcar(nave, "escudo", cor, 0.9)
        self.anunciar("Escudo", "bloqueia o próximo ataque", cor, nave.lane)

    def superaquecimento(self, nave) -> None:
        x, y = self._pos(nave)
        # Faíscas quentes + fumaça que sobe devagar
        self._jorro(x, y, 22, (0xFF, 0x9A, 0x50), 150, 0.5, raio=2.8)
        self._jorro(x, y, 12, (0x5A, 0x50, 0x50), 40, 1.4, raio=5, arrasto=0.7)
        self.ondas.append(_Onda(nave.lane, x, y, 10, 40, 0.4, cfg.COR_ALERTA, 3))
        self.anunciar("Superaqueceu", "motor cortado", cfg.COR_ALERTA, nave.lane, 1.4)
        self.marcar(nave, "superaqueceu", cfg.COR_ALERTA, 1.2)

    def varredura(self, nave, resultado: str) -> None:
        """Só usado se IRIS_EVENTO_ATIVO voltar a ser True."""
        x, y = self._pos(nave)
        if resultado == "atingido":
            self._jorro(x, y, 20, (0xFF, 0xD0, 0x80), 140, 0.6, raio=3)
            self.ondas.append(_Onda(nave.lane, x, y, 8, 56, 0.45, (0xFF, 0xE8, 0xB0), 3))
            self.marcar(nave, "varredura", cfg.COR_ALERTA, 1.2)
        elif resultado == "bloqueado":
            self.escudos.append(_Escudo(nave.lane, 0.5))
            self.marcar(nave, "bloqueado", cor_do_item("escudo"), 1.1)

    def largada(self) -> None:
        self.flash = 0.35
        self.flash_cor = (0x9A, 0xC8, 0xFF)

    # -- ciclo ---------------------------------------------------------------

    def atualizar(self, dt: float, race) -> None:
        self.flash = max(0.0, self.flash - dt * 2.2)

        vivas = []
        for p in self.particulas:
            p.vida -= dt
            if p.vida <= 0.0:
                continue
            k = math.exp(-p.arrasto * dt)
            p.vx *= k
            p.vy = p.vy * k + p.grav * dt
            p.x += p.vx * dt
            p.y += p.vy * dt
            vivas.append(p)
        self.particulas = vivas

        for o in self.ondas:
            o.vida -= dt
        self.ondas = [o for o in self.ondas if o.vida > 0.0]

        for a in self.anuncios:
            a.vida -= dt
        self.anuncios = [a for a in self.anuncios if a.vida > 0.0]

        for e in self.escudos:
            e.vida -= dt
        self.escudos = [e for e in self.escudos if e.vida > 0.0]

        # Marcadores sobem devagar a partir de onde a nave estava
        for m in self.marcadores:
            m.vida -= dt
            m.y -= 26 * dt
        self.marcadores = [m for m in self.marcadores if m.vida > 0.0]

        for pr in self.projeteis:
            pr.vida -= dt
        self.projeteis = [pr for pr in self.projeteis if pr.vida > 0.0]

    def desenhar(self, tela: pygame.Surface, race) -> None:
        # Ondas
        for o in self.ondas:
            k = 1.0 - o.vida / o.vida_max
            raio = o.r0 + (o.r1 - o.r0) * k
            if raio < 1:
                continue
            if o.lane is not None:
                o.x, o.y = self._pos(race.naves[o.lane])
            cor = _mistura((0, 0, 0), o.cor, (1.0 - k) ** 1.5)
            largura = max(1, int(o.largura * (1.0 - k * 0.6)))
            pygame.draw.circle(tela, cor, (int(o.x), int(o.y)), int(raio), largura)

        # Projéteis, seguindo o traçado da pista e migrando para a faixa do
        # alvo ao longo do voo — é o que faz o tiro parecer mirado.
        for pr in self.projeteis:
            k = 1.0 - pr.vida / pr.vida_max
            t = pr.t0 + ((pr.t1 - pr.t0) % 1.0) * k
            off = pr.off + (pr.off1 - pr.off) * k
            x, y = track.ponto(t, off)
            pr.rastro.append((x, y))
            del pr.rastro[:-9]
            for i, (rx, ry) in enumerate(pr.rastro):
                f = (i + 1) / len(pr.rastro)
                self.glow.blit(tela, _mistura((0, 0, 0), pr.cor, f * 0.8),
                               max(2, int(2 + 4 * f)), rx, ry)

            ang = track.angulo(t)
            if pr.tipo == "bomba":
                # Casco redondo girando, com pavio aceso
                raio = 6
                pygame.draw.circle(tela, (0x2A, 0x22, 0x14), (int(x), int(y)), raio)
                pygame.draw.circle(tela, pr.cor, (int(x), int(y)), raio, 2)
                giro = k * 14
                px = x + math.cos(giro) * raio
                py = y + math.sin(giro) * raio
                pygame.draw.circle(tela, (0xFF, 0xF0, 0xC0), (int(px), int(py)), 2)
            else:
                # Dardo apontado na direção do voo
                ca, sa = math.cos(ang), math.sin(ang)
                pontos = [
                    (x + 9 * ca, y + 9 * sa),
                    (x - 4 * ca - 3.5 * sa, y - 4 * sa + 3.5 * ca),
                    (x - 4 * ca + 3.5 * sa, y - 4 * sa - 3.5 * ca),
                ]
                pygame.draw.polygon(tela, (0xFF, 0xF0, 0xE0), pontos)
                pygame.draw.polygon(tela, pr.cor, pontos, 1)

        # Bolha de escudo acendendo sobre a nave
        for e in self.escudos:
            f = e.vida / e.vida_max
            nave = race.naves[e.lane]
            x, y = self._pos(nave)
            cor = _mistura((0, 0, 0), cor_do_item("escudo"), f)
            raio = 24 + 8 * (1.0 - f)
            fase = (1.0 - f) * 2.0
            pontos = [
                (x + math.cos(fase + i * math.tau / 6) * raio,
                 y + math.sin(fase + i * math.tau / 6) * raio) for i in range(6)
            ]
            pygame.draw.polygon(tela, cor, pontos, max(1, int(1 + 3 * f)))
            # Faces internas, sugerindo a casca hexagonal
            for i in range(6):
                a = fase + i * math.tau / 6
                pygame.draw.line(tela, _mistura((0, 0, 0), cor, 0.35), (x, y),
                                 (x + math.cos(a) * raio, y + math.sin(a) * raio), 1)

        # Partículas
        for p in self.particulas:
            f = p.vida / p.vida_max
            self.glow.blit(tela, _mistura((0, 0, 0), p.cor, f * f),
                           max(1, int(p.raio)), p.x, p.y)

        # Marcadores: o texto fica EM CIMA DA PISTA, junto da nave
        for m in self.marcadores:
            alpha = min(1.0, m.vida / 0.4)
            img = self._texto(self._f_marca, m.texto, _mistura((0, 0, 0), m.cor, alpha))
            r = img.get_rect(center=(int(m.x), int(m.y) - 30))
            r.left = max(10, min(r.left, cfg.LARGURA - 10 - r.width))
            r.top = max(10, r.top)
            # Tarja escura por trás, para o texto sobreviver ao fundo estrelado
            fundo = pygame.Rect(r.left - 5, r.top - 2, r.width + 10, r.height + 4)
            pygame.draw.rect(tela, _mistura((0, 0, 0), (0x06, 0x09, 0x12), alpha),
                             fundo, border_radius=3)
            pygame.draw.rect(tela, _mistura((0, 0, 0), m.cor, alpha * 0.7),
                             fundo, 1, border_radius=3)
            tela.blit(img, r)

    def desenhar_anuncios(self, tela: pygame.Surface) -> None:
        """
        Desenhado depois do HUD, para o cartão não ficar atrás dos painéis.
        Sobe alguns pixels e desvanece.
        """
        for a in self.anuncios:
            k = 1.0 - a.vida / a.vida_max
            subida = 26 * (1.0 - (1.0 - k) ** 2)
            # Entra rápido, sai lento
            alpha = min(1.0, a.vida / 0.35) * min(1.0, (a.vida_max - a.vida) / 0.12)

            # Alinhado com o disco de roleta do mesmo jogador (ver hud.py).
            base_x = 127 if a.lane == 0 else 1181
            y = 252 - subida

            cor = _mistura((0, 0, 0), a.cor, alpha)
            img = self._texto(self._f_titulo, a.texto, cor)
            r = img.get_rect(center=(base_x, int(y)))
            # Palavra comprida no painel da direita sairia da tela: prende o
            # cartao dentro da margem em vez de deixar o texto ser cortado.
            r.left = max(8, min(r.left, cfg.LARGURA - 8 - r.width))
            tela.blit(img, r)

            if a.sub:
                sub_cor = _mistura((0, 0, 0), cfg.COR_TEXTO_FRACO, alpha)
                img2 = self._texto(self._f_sub, a.sub, sub_cor)
                r2 = img2.get_rect(midtop=(r.centerx, r.bottom + 3))
                r2.left = max(8, min(r2.left, cfg.LARGURA - 8 - r2.width))
                tela.blit(img2, r2)

    def desenhar_flash(self, tela: pygame.Surface) -> None:
        if self.flash <= 0.0:
            return
        veu = pygame.Surface((cfg.LARGURA, cfg.ALTURA))
        veu.fill(_mistura((0, 0, 0), self.flash_cor, min(1.0, self.flash)))
        tela.blit(veu, (0, 0), special_flags=pygame.BLEND_ADD)
