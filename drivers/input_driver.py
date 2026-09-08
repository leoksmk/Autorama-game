# -*- coding: utf-8 -*-
"""
Leitura de entrada.

A lógica de jogo NUNCA lê teclado nem GPIO. Ela chama InputDriver.read() e
recebe sempre o mesmo dicionário de quatro booleanos. Trocar teclado por
hardware é trocar a instância passada para o jogo, nada mais.

A detecção de borda (o botão de ação só vale no instante em que é apertado)
mora aqui, no ButtonEdge, para que nenhum driver precise reimplementá-la.
"""

from __future__ import annotations

import pygame


# Formato do que todo driver devolve por frame.
ESTADO_VAZIO = {
    "p1_throttle": False,
    "p1_action": False,
    "p2_throttle": False,
    "p2_action": False,
}


class InputDriver:
    """Interface. Devolve o estado bruto dos botões neste frame."""

    def read(self) -> dict:
        raise NotImplementedError

    def close(self) -> None:
        """Libera recursos. No-op para teclado; solta pinos no GPIO."""
        return None


class KeyboardDriver(InputDriver):
    """
    Simulação no PC.

    ÍON   -> A (acelerador), S (ação)
    ÍGNIS -> L (acelerador), K (ação)
    """

    MAPA = {
        "p1_throttle": pygame.K_a,
        "p1_action": pygame.K_s,
        "p2_throttle": pygame.K_l,
        "p2_action": pygame.K_k,
    }

    def read(self) -> dict:
        teclas = pygame.key.get_pressed()
        return {nome: bool(teclas[tecla]) for nome, tecla in self.MAPA.items()}


class GpioDriver(InputDriver):
    """
    STUB. Não importa biblioteca de hardware e não é usado nesta etapa.

    Mapa de pinos pretendido (BCM), para preencher na montagem:

        p1_throttle -> GPIO 17   (botão para GND, pull-up interno)
        p1_action   -> GPIO 27   (botão para GND, pull-up interno)
        p2_throttle -> GPIO 22   (botão para GND, pull-up interno)
        p2_action   -> GPIO 23   (botão para GND, pull-up interno)

    Sensores de checkpoint (entram depois, junto com Race.sync_position):

        cp_pista_1  -> GPIO 5, 6, 13    (um por checkpoint)
        cp_pista_2  -> GPIO 19, 26, 21

    Implementação futura, resumida:

        from gpiozero import Button
        self._botoes = {nome: Button(pino, pull_up=True, bounce_time=0.01)
                        for nome, pino in PINOS.items()}
        def read(self):
            return {nome: b.is_pressed for nome, b in self._botoes.items()}

    Debounce por hardware (RC) ou por software (bounce_time) é obrigatório:
    um botão de acelerador chacoalhando vira microcortes de PWM no motor.
    """

    def __init__(self) -> None:
        raise NotImplementedError(
            "GpioDriver ainda não implementado. Use KeyboardDriver nesta etapa."
        )

    def read(self) -> dict:
        return dict(ESTADO_VAZIO)


class ButtonEdge:
    """
    Converte estado contínuo em borda de subida.

    O botão de ação precisa disparar uma vez por aperto. Se a lógica olhasse o
    estado contínuo, segurar o botão giraria a roleta ou queimaria o item
    dezenas de vezes por segundo.
    """

    def __init__(self) -> None:
        self._anterior: dict[str, bool] = {}

    def update(self, estado: dict) -> dict:
        """Devolve um dicionário com True apenas onde houve borda de subida."""
        bordas = {
            nome: bool(valor) and not self._anterior.get(nome, False)
            for nome, valor in estado.items()
        }
        self._anterior = dict(estado)
        return bordas

    def reset(self) -> None:
        """Esquece o estado anterior (usado na troca de tela)."""
        self._anterior = {}
