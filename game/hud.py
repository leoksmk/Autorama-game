# -*- coding: utf-8 -*-
"""
HUD e telas.

O rodapé de telemetria lê do OutputBus, não das naves. Isso é de propósito: é
exatamente o que o back-end vai consumir. Se um efeito não aparece ali, ele não
chegou ao PWM e portanto não existiria no carrinho físico.

Fonte: pygame.font.Font(None, ...), que é a fonte embutida da biblioteca.
Nenhum arquivo externo é carregado.
"""

from __future__ import annotations

import math

import pygame

from . import config as cfg
from . import items
from .gfx import mistura as _mistura

PAINEL_L = 236
PAINEL_A = 208
PAINEL_Y = 16
RODAPE_Y = 646
RODAPE_A = 60

# Centro da caixa de item de cada jogador. As posições foram medidas contra o
# traçado: a caixa tem de caber no vão entre a borda externa da pista e a borda
# da tela, senão cobre a corrida. O vão é assimétrico porque a órbita é
# modulada, daí os dois centros diferentes em vez de um espelhamento simples.
ROLETA_CX_P1 = 127
ROLETA_CX_P2 = 1181
ROLETA_CY = 356
ROLETA_LADO = 108


class Hud:
    def __init__(self) -> None:
        self.f_titulo = pygame.font.Font(None, 74)
        self.f_grande = pygame.font.Font(None, 46)
        self.f_medio = pygame.font.Font(None, 28)
        self.f_peq = pygame.font.Font(None, 22)
        self.f_mini = pygame.font.Font(None, 19)
        self._t = 0.0
        # Os painéis translúcidos têm tamanho e cor fixos. Rasterizar o mesmo
        # retângulo 60 vezes por segundo é desperdício; cacheamos por formato.
        self._cache_painel: dict[tuple, pygame.Surface] = {}

    def tick(self, dt: float) -> None:
        self._t += dt

    # -- utilidades ---------------------------------------------------------

    def _texto(self, tela, fonte, txt, x, y, cor=cfg.COR_TEXTO, centro=False, direita=False):
        img = fonte.render(txt, True, cor)
        r = img.get_rect()
        if centro:
            r.midtop = (x, y)
        elif direita:
            r.topright = (x, y)
        else:
            r.topleft = (x, y)
        tela.blit(img, r)
        return r

    def _fundo_translucido(self, w, h, cor, alpha) -> pygame.Surface:
        chave = (w, h, cor, alpha)
        s = self._cache_painel.get(chave)
        if s is None:
            s = pygame.Surface((w, h), pygame.SRCALPHA)
            s.fill((*cor, alpha))
            self._cache_painel[chave] = s
        return s

    def _painel(self, tela, x, y, w, h, cor_borda=None, alpha=232):
        tela.blit(self._fundo_translucido(w, h, cfg.COR_PAINEL, alpha), (x, y))
        pygame.draw.rect(tela, cor_borda or (0x1C, 0x28, 0x40), (x, y, w, h), 1)

    def _barra(self, tela, x, y, w, h, fracao, cor, fundo=(0x14, 0x1C, 0x2E)):
        pygame.draw.rect(tela, fundo, (x, y, w, h))
        preenchido = int(w * max(0.0, min(1.0, fracao)))
        if preenchido > 0:
            pygame.draw.rect(tela, cor, (x, y, preenchido, h))
        pygame.draw.rect(tela, (0x27, 0x33, 0x4C), (x, y, w, h), 1)

    # -- painel do jogador --------------------------------------------------

    def painel_jogador(self, tela, race, nave) -> None:
        x = 16 if nave.lane == 0 else cfg.LARGURA - PAINEL_L - 16
        y = PAINEL_Y
        borda = _mistura(nave.cor, cfg.COR_PAINEL, 0.55)
        self._painel(tela, x, y, PAINEL_L, PAINEL_A, borda)

        px = x + 14
        largura = PAINEL_L - 28

        # Nome à esquerda, voltas em destaque à direita
        self._texto(tela, self.f_medio, nave.nome, px, y + 14, nave.cor)
        voltas = min(nave.voltas, cfg.VOLTAS_PARA_VENCER)
        self._texto(tela, self.f_grande, f"{voltas}/{cfg.VOLTAS_PARA_VENCER}",
                    x + PAINEL_L - 14, y + 8, cfg.COR_TEXTO, direita=True)

        # Barra de ritmo: é o que o polegar está produzindo agora. Fica no topo
        # porque é a informação que o jogador consulta o tempo todo.
        pos = race.posicao(nave)
        self._texto(tela, self.f_peq, "ritmo", px, y + 44, cfg.COR_TEXTO_FRACO)
        self._texto(tela, self.f_peq, f"{pos}º", x + PAINEL_L - 14, y + 44,
                    cfg.COR_TEXTO if pos == 1 else cfg.COR_TEXTO_FRACO, direita=True)
        esforco = nave.esforco
        cor_ritmo = nave.cor if esforco > 0.05 else (0x30, 0x3C, 0x54)
        self._barra(tela, px, y + 64, largura, 10, esforco, cor_ritmo)

        # Barra de calor
        self._texto(tela, self.f_peq, "calor", px, y + 84, cfg.COR_TEXTO_FRACO)
        if nave.superaquecimento > 0.0:
            cor_calor = cfg.COR_ALERTA
        else:
            cor_calor = _mistura(cfg.COR_OK, cfg.COR_ALERTA, nave.calor)
        self._barra(tela, px, y + 104, largura, 14, nave.calor, cor_calor)
        # Marca do ritmo a partir do qual o motor esquenta
        lim = px + int(largura * cfg.CALOR_LIMIAR)
        pygame.draw.line(tela, (0x50, 0x5E, 0x78), (lim, y + 62), (lim, y + 76), 1)
        if nave.calor > 0.78 and nave.superaquecimento <= 0.0:
            if math.sin(self._t * 14) > 0:
                pygame.draw.rect(tela, cfg.COR_ALERTA, (px, y + 104, largura, 14), 2)

        # Slot de item
        self._texto(tela, self.f_peq, "slot", px, y + 126, cfg.COR_TEXTO_FRACO)
        slot_y = y + 146
        pygame.draw.rect(tela, (0x11, 0x18, 0x2A), (px, slot_y, largura, 30))
        if nave.item:
            cor_item = items.cor_do_item(nave.item)
            pygame.draw.rect(tela, cor_item, (px, slot_y, largura, 30), 2)
            self._texto(tela, self.f_medio, items.nome_legivel(nave.item),
                        px + largura // 2, slot_y + 5, cor_item, centro=True)
        elif nave.roleta.visivel:
            pygame.draw.rect(tela, nave.cor, (px, slot_y, largura, 30), 1)
            rotulo = {
                items.OPORTUNIDADE: "roleta aberta",
                items.GIRANDO: "girando",
                items.REVELANDO: "prêmio",
            }.get(nave.roleta.estado, "")
            self._texto(tela, self.f_peq, rotulo, px + largura // 2, slot_y + 8,
                        nave.cor, centro=True)
        else:
            pygame.draw.rect(tela, (0x1C, 0x25, 0x3A), (px, slot_y, largura, 30), 1)
            self._texto(tela, self.f_peq, "vazio", px + largura // 2, slot_y + 8,
                        cfg.COR_TEXTO_FRACO, centro=True)

        # Mensagem curta do jogador
        if nave.aviso:
            self._texto(tela, self.f_peq, nave.aviso, px, y + 182, nave.cor)

    # -- caixa de item -------------------------------------------------------

    def _icone(self, tela, item, cx, cy, esc, cor):
        """
        Símbolo vetorial de cada poder.

        Ícone em vez de texto porque a caixa troca de face muitas vezes por
        segundo durante o giro: ler palavras a essa velocidade é impossível,
        ler formas não é.
        """
        def p(x, y):
            return (cx + x * esc, cy + y * esc)

        if item == "tiro":
            # Dardo apontando para frente, com traço de velocidade
            pygame.draw.polygon(tela, cor, [p(7, 0), p(-4, -5.5), p(-1.5, 0), p(-4, 5.5)])
            pygame.draw.line(tela, cor, p(-7, -3), p(-10, -3), 2)
            pygame.draw.line(tela, cor, p(-7, 3), p(-10, 3), 2)
        elif item == "bomba":
            # Casco redondo com pavio
            pygame.draw.circle(tela, cor, p(0, 1.5), int(6 * esc))
            pygame.draw.line(tela, cor, p(2.5, -4), p(6, -8), 2)
            pygame.draw.circle(tela, (0xFF, 0xF0, 0xC0), p(6.5, -8.5), max(1, int(1.6 * esc)))
        elif item == "escudo":
            # Escudo: hexágono com barra central
            pygame.draw.polygon(tela, cor,
                                [p(7 * math.cos(i * math.tau / 6 - math.pi / 2),
                                   7 * math.sin(i * math.tau / 6 - math.pi / 2))
                                 for i in range(6)], 2)
            pygame.draw.line(tela, cor, p(0, -3.5), p(0, 3.5), 2)
        else:
            # Nada: círculo cortado
            pygame.draw.circle(tela, cor, p(0, 0), int(7 * esc), 2)
            pygame.draw.line(tela, cor, p(-5, -5), p(5, 5), 2)

    def roleta(self, tela, nave) -> None:
        """
        Caixa de item do jogador, no estilo das caixas de corrida: o ícone
        troca rápido e vai travando.

        Três leituras diferentes, porque são três momentos diferentes: a janela
        aberta mostra a barra de tempo escoando e o botão a apertar, o giro
        mostra a face piscando, e a revelação trava e estufa a caixa.
        """
        r = nave.roleta
        if not r.visivel:
            return

        cx = ROLETA_CX_P1 if nave.lane == 0 else ROLETA_CX_P2
        cy = ROLETA_CY
        lado = ROLETA_LADO

        girando = r.estado == items.GIRANDO
        revelando = r.estado == items.REVELANDO

        # Na revelação a caixa dá um "estufo" curto e volta.
        escala = 1.0
        if revelando:
            k = min(1.0, r.tempo / 0.22)
            escala = 1.0 + 0.22 * math.sin(k * math.pi)
        meio = int(lado * escala / 2)

        cor_item = items.cor_do_item(r.face)
        caixa = pygame.Rect(cx - meio, cy - meio, meio * 2, meio * 2)

        # Corpo da caixa
        pygame.draw.rect(tela, (0x0A, 0x10, 0x1C), caixa, border_radius=10)
        borda = cor_item if (girando or revelando) else nave.cor
        pygame.draw.rect(tela, borda, caixa, 3, border_radius=10)

        # Fundo pulsando na cor do item enquanto gira
        if girando or revelando:
            brilho = 0.16 if girando else 0.30 + 0.22 * math.sin(self._t * 18)
            interno = caixa.inflate(-10, -10)
            pygame.draw.rect(tela, _mistura((0x0A, 0x10, 0x1C), cor_item, brilho),
                             interno, border_radius=7)

        # Cantoneiras, para a caixa ler como caixa e não como botão
        for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            ax = cx + sx * (meio - 7)
            ay = cy + sy * (meio - 7)
            pygame.draw.line(tela, borda, (ax, ay), (ax - sx * 9, ay), 2)
            pygame.draw.line(tela, borda, (ax, ay), (ax, ay - sy * 9), 2)

        self._icone(tela, r.face, cx, cy, 2.6 * escala, cor_item)

        if r.estado == items.OPORTUNIDADE:
            # Barra de tempo escoando por baixo da caixa
            frac = r.fracao_restante
            urgente = frac < 0.35
            cor_barra = cfg.COR_ALERTA if urgente else nave.cor
            larg = lado
            bx = cx - larg // 2
            by = cy + meio + 12
            pygame.draw.rect(tela, (0x14, 0x1C, 0x2E), (bx, by, larg, 8))
            if frac > 0:
                pygame.draw.rect(tela, cor_barra, (bx, by, int(larg * frac), 8))
            pygame.draw.rect(tela, (0x27, 0x33, 0x4C), (bx, by, larg, 8), 1)

            # O botão que resolve isto, ensinado na hora em que importa.
            pisca = 0.5 + 0.5 * math.sin(self._t * (18 if urgente else 9))
            cor_pisca = _mistura(cfg.COR_TEXTO_FRACO, cor_barra, pisca)
            self._texto(tela, self.f_grande, cfg.ROTULO_ACAO.get(nave.lane, "?"),
                        cx, by + 16, cor_pisca, centro=True)
            self._texto(tela, self.f_peq, "aperte", cx, by + 52, cor_pisca, centro=True)

        elif revelando:
            self._texto(tela, self.f_medio, items.nome_legivel(r.face),
                        cx, cy + meio + 14, cor_item, centro=True)

    # -- barra da estação ---    # -- barra da estação ---------------------------------------------------

    def barra_iris(self, tela, iris) -> None:
        if not cfg.IRIS_EVENTO_ATIVO:
            return
        w, h = 340, 16
        x = (cfg.LARGURA - w) // 2
        y = 26

        rotulo = "ÍRIS-9"
        if iris.varrendo:
            rotulo = "ÍRIS-9 varrendo"
            cor = (0xFF, 0xE2, 0x9A)
        elif iris.avisando:
            rotulo = "ÍRIS-9 vai varrer"
            cor = cfg.COR_ALERTA if math.sin(self._t * 12) > 0 else (0xFF, 0xA8, 0xB8)
        else:
            cor = cfg.COR_ESTACAO

        self._texto(tela, self.f_peq, rotulo, cfg.LARGURA // 2, y - 20, cor, centro=True)
        fracao = 1.0 if iris.varrendo else iris.carga
        self._barra(tela, x, y, w, h, fracao, cor)

        # Marca do limiar de aviso
        mx = x + int(w * cfg.IRIS_AVISO)
        pygame.draw.line(tela, cfg.COR_TEXTO_FRACO, (mx, y - 3), (mx, y + h + 3), 1)

        if iris.avisando or iris.varrendo:
            self._texto(tela, self.f_mini, "a sombra fica no trecho hachurado da pista",
                        cfg.LARGURA // 2, y + h + 5, cfg.COR_TEXTO_FRACO, centro=True)

    # -- telemetria ---------------------------------------------------------

    def telemetria(self, tela, race, bus) -> None:
        """
        Rodapé. Lê o que foi escrito no OutputBus neste frame.

        É o contrato com o hardware, mostrado ao vivo: PWM alvo por pista e o
        efeito que está mandando naquele valor.
        """
        y = RODAPE_Y
        self._painel(tela, 0, y, cfg.LARGURA, cfg.ALTURA - y, (0x1A, 0x24, 0x3A), 240)

        self._texto(tela, self.f_mini, "telemetria · saída para as pistas",
                    12, y + 6, cfg.COR_TEXTO_FRACO)

        for nave in race.naves:
            estado = bus.get_lane(nave.lane)
            pwm = estado["pwm"]
            tag = estado["effect_tag"] or "livre"

            bx = 12 + nave.lane * 420
            by = y + 26

            self._texto(tela, self.f_peq, f"pista {nave.lane + 1}", bx, by - 1, nave.cor)
            self._barra(tela, bx + 74, by + 2, 120, 12, pwm, nave.cor)
            self._texto(tela, self.f_peq, f"pwm {pwm:0.2f}", bx + 202, by - 1, cfg.COR_TEXTO)
            cor_tag = (cfg.COR_ALERTA
                       if tag in ("superaquecido", "parado pela bomba", "teto reduzido")
                       else cfg.COR_TEXTO_FRACO)
            self._texto(tela, self.f_peq, tag, bx + 282, by - 1, cor_tag)

        # Últimos eventos, à direita
        ly = y + 6
        for texto, cor in race.log[-3:]:
            self._texto(tela, self.f_mini, texto, cfg.LARGURA - 12, ly, cor, direita=True)
            ly += 16

    # -- telas ---------------------------------------------------------------

    def tela_atracao(self, tela, status: str = "") -> None:
        tela.blit(self._fundo_translucido(cfg.LARGURA, cfg.ALTURA, cfg.COR_FUNDO, 200), (0, 0))

        cx = cfg.LARGURA // 2
        self._texto(tela, self.f_titulo, "ORBITAL DERBY", cx, 118, cfg.COR_TEXTO, centro=True)
        self._texto(tela, self.f_peq, "Dois cargueiros, um anel de detritos e a ÍRIS-9 vigiando.",
                    cx, 182, cfg.COR_TEXTO_FRACO, centro=True)

        # Cartão de controles. A altura acompanha o número de linhas: com as
        # regras atuais são sete, e um painel fixo cortava as últimas.
        w, h = 700, 300
        x, y = cx - w // 2, 214
        self._painel(tela, x, y, w, h)

        col = [x + 28, x + 368]
        for i, (nome, cor, acel, acao) in enumerate((
            (cfg.NOME_P1, cfg.COR_P1, "A", "S"),
            (cfg.NOME_P2, cfg.COR_P2, "L", "K"),
        )):
            self._texto(tela, self.f_medio, nome, col[i], y + 20, cor)
            self._texto(tela, self.f_peq, f"{acel} — acelerador (martele)", col[i], y + 54, cfg.COR_TEXTO)
            self._texto(tela, self.f_peq, f"{acao} — ação", col[i], y + 78, cfg.COR_TEXTO)

        linhas = [
            "O acelerador é de MARTELAR: aperte rápido para ir rápido.",
            "Segurar não faz nada. Ritmo alto demais esquenta e corta o motor.",
            f"Cruzar um checkpoint abre a caixa por {cfg.ROLETA_OPORTUNIDADE:.1f} s. Não apertou, perdeu.",
            "A caixa gira sem parar a nave — mas às vezes não vem nada.",
            "Com item no slot, a ação usa o item a qualquer momento.",
            "Tiro deixa lento, Bomba para por 2 s, Escudo bloqueia um ataque.",
            "Tiro e Bomba só pegam o adversário de perto. Longe, o item queima.",
            f"Vence quem completar {cfg.VOLTAS_PARA_VENCER} voltas.",
        ]
        ly = y + 112
        for linha in linhas:
            self._texto(tela, self.f_peq, linha, x + 28, ly, cfg.COR_TEXTO_FRACO)
            ly += 23

        pisca = 0.5 + 0.5 * math.sin(self._t * 4)
        self._texto(tela, self.f_medio, "Espaço para começar",
                    cx, y + h + 22, _mistura(cfg.COR_TEXTO_FRACO, cfg.COR_TEXTO, pisca),
                    centro=True)
        extras = "R reinicia" if cfg.NO_NAVEGADOR else "F11 tela cheia · R reinicia · Esc sai"
        self._texto(tela, self.f_mini, extras, cx, y + h + 58,
                    cfg.COR_TEXTO_FRACO, centro=True)

        # Estado dos controles de hardware. Vazio no teclado, e aí nem aparece.
        if status:
            self._texto(tela, self.f_mini, status, cx, y + h + 80,
                        cfg.COR_TEXTO_FRACO, centro=True)

    def tela_contagem(self, tela, restante: float) -> None:
        cx = cfg.LARGURA // 2
        n = int(math.ceil(restante))
        texto = str(n) if n > 0 else "Vai!"
        # Escala pulsando a cada segundo inteiro
        fase = restante - math.floor(restante)
        escala = 1.0 + 0.35 * fase if n > 0 else 1.25

        img = self.f_titulo.render(texto, True, cfg.COR_TEXTO)
        img = pygame.transform.rotozoom(img, 0, escala)
        r = img.get_rect(center=(cx, 300))
        tela.blit(img, r)
        self._texto(tela, self.f_peq, "segure o acelerador quando aparecer Vai",
                    cx, 366, cfg.COR_TEXTO_FRACO, centro=True)

    def tela_resultado(self, tela, race) -> None:
        tela.blit(self._fundo_translucido(cfg.LARGURA, cfg.ALTURA, cfg.COR_FUNDO, 215), (0, 0))

        cx = cfg.LARGURA // 2
        vencedor = race.vencedor
        self._texto(tela, self.f_titulo, f"{vencedor.nome} venceu", cx, 150, vencedor.cor, centro=True)

        w, h = 560, 176
        x, y = cx - w // 2, 250
        self._painel(tela, x, y, w, h)

        ly = y + 22
        for i, nave in enumerate(race.classificacao()):
            self._texto(tela, self.f_medio, f"{i + 1}º", x + 24, ly, cfg.COR_TEXTO_FRACO)
            self._texto(tela, self.f_medio, nave.nome, x + 70, ly, nave.cor)
            voltas = min(nave.voltas, cfg.VOLTAS_PARA_VENCER)
            self._texto(tela, self.f_peq, f"{voltas} voltas", x + 230, ly + 5, cfg.COR_TEXTO)
            if nave.terminou:
                self._texto(tela, self.f_peq, f"{nave.tempo_final:0.1f} s",
                            x + w - 24, ly + 5, cfg.COR_TEXTO, direita=True)
            else:
                self._texto(tela, self.f_peq, f"em {nave.t * 100:0.0f}% da volta",
                            x + w - 24, ly + 5, cfg.COR_TEXTO_FRACO, direita=True)
            ly += 46

        self._texto(tela, self.f_peq, f"tempo da corrida: {race.tempo:0.1f} s",
                    x + 24, y + 128, cfg.COR_TEXTO_FRACO)

        pisca = 0.5 + 0.5 * math.sin(self._t * 4)
        self._texto(tela, self.f_medio, "R para correr de novo",
                    cx, 470, _mistura(cfg.COR_TEXTO_FRACO, cfg.COR_TEXTO, pisca), centro=True)
