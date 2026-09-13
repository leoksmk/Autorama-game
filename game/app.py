# -*- coding: utf-8 -*-
"""
Loop principal e máquina de telas.

    ATRAÇÃO -> CONTAGEM -> CORRIDA -> RESULTADO -> (R) CONTAGEM

Esta camada é a única que conhece pygame.event e a única que instancia os
drivers. A lógica de jogo recebe dicionários e escreve no barramento; ela não
sabe de onde vieram os botões nem para onde vai o PWM.

O loop é assíncrono porque a versão web (compilada para WebAssembly com pygbag)
exige devolver o controle ao navegador uma vez por frame. No desktop isso não
custa nada: `asyncio.run` roda o mesmo loop.
"""

from __future__ import annotations

import asyncio
import os

import pygame

from drivers.input_driver import ButtonEdge, InputDriver, KeyboardDriver
from drivers.output_bus import NullOutput, OutputBus

from . import config as cfg
from .effects import Effects
from .hud import Hud
from .race import Race
from .render import Renderer

NO_NAVEGADOR = cfg.NO_NAVEGADOR

ATRACAO, CONTAGEM, CORRIDA, RESULTADO = "atracao", "contagem", "corrida", "resultado"

# Quanto tempo a corrida continua na tela depois que alguém cruza a linha,
# para o jogador ver a chegada antes do placar.
ESPERA_RESULTADO = 1.6


class App:
    def __init__(self, input_driver: InputDriver | None = None,
                 output_bus: OutputBus | None = None) -> None:
        pygame.init()
        pygame.display.set_caption(cfg.TITULO)

        self.fullscreen = False
        self.tela = self._criar_tela()
        self.clock = pygame.time.Clock()

        # Troque estas duas linhas para rodar no hardware. Nada mais muda.
        self.entrada = input_driver or KeyboardDriver()
        self.bus = output_bus or NullOutput(lanes=2)

        self.bordas = ButtonEdge()
        self.fx = Effects()
        self.race = Race(self.bus, fx=self.fx)
        self.render = Renderer()
        self.hud = Hud()

        self.estado = ATRACAO
        self.contagem = cfg.CONTAGEM_DURACAO
        self.espera = 0.0
        self.rodando = True
        self.mostrar_fps = False

    # -- controle de tela ---------------------------------------------------

    def _criar_tela(self) -> pygame.Surface:
        """
        Cria a janela com degradação controlada.

        pygame.SCALED mantém a resolução lógica de 1280x720 e deixa o driver
        esticar, mas depende de um renderizador acelerado. Em máquina sem
        aceleração (algumas configurações de Pi, sessão sem GPU, X remoto) ele
        falha ou — pior — é aceito e cai num caminho de software lento, avisando
        só com "no fast renderer available" no terminal.

        Quando ele falha, caímos para janela comum sozinhos. Quando ele apenas
        fica lento, o pygame não nos avisa de forma detectável: por isso existe
        ORBITAL_NO_SCALED=1, para pular o SCALED de saída.
        """
        base = pygame.FULLSCREEN if self.fullscreen else 0
        if NO_NAVEGADOR:
            # O canvas já tem o tamanho certo; SCALED e vsync só atrapalham.
            tentativas = ((0, 0),)
        elif os.environ.get("ORBITAL_NO_SCALED") == "1":
            tentativas = ((base, 0),)
        else:
            tentativas = (
                (pygame.SCALED | base, 1),
                (pygame.SCALED | base, 0),
                (base, 0),
            )
        ultimo_erro = None
        for flags, vsync in tentativas:
            try:
                return pygame.display.set_mode((cfg.LARGURA, cfg.ALTURA), flags, vsync=vsync)
            except pygame.error as e:
                ultimo_erro = e
        raise RuntimeError(f"Não foi possível abrir a janela: {ultimo_erro}")

    def alternar_fullscreen(self) -> None:
        self.fullscreen = not self.fullscreen
        self.tela = self._criar_tela()

    def iniciar_corrida(self) -> None:
        self.race.reset()
        self.bordas.reset()
        self.contagem = cfg.CONTAGEM_DURACAO
        self.espera = 0.0
        self.estado = CONTAGEM

    # -- eventos ------------------------------------------------------------

    def _eventos(self) -> None:
        for evento in pygame.event.get():
            if evento.type == pygame.QUIT:
                self.rodando = False
            elif evento.type == pygame.KEYDOWN:
                if evento.key == pygame.K_ESCAPE:
                    self.rodando = False
                elif evento.key == pygame.K_F11 and not NO_NAVEGADOR:
                    self.alternar_fullscreen()
                elif evento.key == pygame.K_F3:
                    self.mostrar_fps = not self.mostrar_fps
                elif evento.key == pygame.K_SPACE and self.estado == ATRACAO:
                    self.iniciar_corrida()
                elif evento.key == pygame.K_r and self.estado in (RESULTADO, CORRIDA):
                    self.iniciar_corrida()

    # -- ciclo --------------------------------------------------------------

    def atualizar(self, dt: float) -> None:
        self.hud.tick(dt)

        estado_botoes = self.entrada.read()
        bordas = self.bordas.update(estado_botoes)

        correndo = self.estado == CORRIDA

        if self.estado == CONTAGEM:
            self.contagem -= dt
            if self.contagem <= 0.0:
                self.estado = CORRIDA
                self.race.anotar("Largada", cfg.COR_TEXTO)
                self.fx.largada()

        # A simulação roda também fora da corrida, com `correndo=False`: assim
        # os temporizadores drenam, o PWM vai a zero e a telemetria continua
        # dizendo a verdade sobre o que sairia para as pistas.
        self.race.atualizar(dt, estado_botoes, bordas, correndo)
        self.fx.atualizar(dt, self.race)

        if self.estado == CORRIDA and self.race.vencedor is not None:
            self.espera += dt
            if self.espera >= ESPERA_RESULTADO:
                self.estado = RESULTADO

    def desenhar(self, dt: float) -> None:
        self.render.desenhar_mundo(self.tela, self.race, dt,
                                   correndo=self.estado in (CORRIDA, RESULTADO))

        # Partículas e ondas ficam entre o mundo e o HUD: elas pertencem à
        # pista, não à interface.
        self.fx.desenhar(self.tela, self.race)

        if self.estado in (CONTAGEM, CORRIDA, RESULTADO):
            for nave in self.race.naves:
                self.hud.painel_jogador(self.tela, self.race, nave)
                self.hud.roleta(self.tela, nave)
            self.hud.barra_iris(self.tela, self.race.iris)

        # A telemetria fica sempre visível: é o contrato com o hardware.
        self.hud.telemetria(self.tela, self.race, self.bus)

        # Os cartões de anúncio vêm depois do HUD, para não ficarem por baixo
        # dos painéis.
        if self.estado in (CORRIDA, RESULTADO):
            self.fx.desenhar_anuncios(self.tela)
        self.fx.desenhar_flash(self.tela)

        if self.estado == ATRACAO:
            # O status do driver só existe para o hardware: com os controles
            # ESP32 o jogador precisa ver, antes de largar, se a placa dele foi
            # encontrada. No teclado a linha é vazia e nada aparece.
            self.hud.tela_atracao(self.tela, self.entrada.status())
        elif self.estado == CONTAGEM:
            self.hud.tela_contagem(self.tela, self.contagem)
        elif self.estado == RESULTADO:
            self.hud.tela_resultado(self.tela, self.race)

        if self.mostrar_fps:
            self.hud._texto(
                self.tela, self.hud.f_mini, f"{self.clock.get_fps():0.0f} fps",
                cfg.LARGURA - 12, 6, cfg.COR_TEXTO_FRACO, direita=True,
            )

        pygame.display.flip()

    async def rodar(self) -> None:
        """
        Loop principal.

        O `await` no fim de cada frame é o que devolve o controle ao navegador
        na versão web — sem ele a aba trava. No desktop é um no-op barato.
        """
        try:
            while self.rodando:
                # Clamp de dt: uma janela arrastada ou um hiccup do SO não pode
                # teletransportar as naves por meia volta.
                dt = min(self.clock.tick(cfg.FPS) / 1000.0, 0.05)
                self._eventos()
                self.atualizar(dt)
                self.desenhar(dt)
                await asyncio.sleep(0)
        finally:
            self.entrada.close()
            self.bus.close()
            if not NO_NAVEGADOR:
                pygame.quit()
