# -*- coding: utf-8 -*-
"""
ORBITAL DERBY — ponto de entrada.

Roda 100% simulado: teclado na entrada, NullOutput na saída, pista integrada
por software. Nenhuma dependência de GPIO.

    python main.py

O mesmo arquivo é o ponto de entrada da versão web (pygbag/WebAssembly), por
isso o `asyncio.run` no fim: o navegador precisa que o loop devolva o controle
uma vez por frame. No desktop o comportamento é idêntico.

Para migrar ao hardware, troque as duas instâncias abaixo por GpioDriver e
PwmOutput (drivers/). A lógica de jogo não muda.
"""

import asyncio

# O import de pygame precisa estar AQUI, no ponto de entrada, mesmo que este
# arquivo não o use: o pygbag decide quais pacotes embarcar no WebAssembly
# analisando os imports do main.py. Importado só lá dentro de game/, o pygame
# chegava ao navegador como um stub vazio e o jogo morria no pygame.init().
import pygame  # noqa: F401  (necessário para o build web)

from game.app import App


async def main() -> None:
    from drivers.input_driver import KeyboardDriver
    from drivers.output_bus import NullOutput

    entrada = KeyboardDriver()
    saida = NullOutput(lanes=2)

    await App(input_driver=entrada, output_bus=saida).rodar()


asyncio.run(main())
