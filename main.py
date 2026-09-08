# -*- coding: utf-8 -*-
"""
ORBITAL DERBY — ponto de entrada.

Roda 100% simulado: teclado na entrada, NullOutput na saída, pista integrada
por software. Nenhuma dependência de GPIO.

    python main.py

Para migrar ao hardware, troque as duas instâncias abaixo por GpioDriver e
PwmOutput (drivers/). A lógica de jogo não muda.
"""

from game.app import App


def main() -> None:
    from drivers.input_driver import KeyboardDriver
    from drivers.output_bus import NullOutput

    entrada = KeyboardDriver()
    saida = NullOutput(lanes=2)

    App(input_driver=entrada, output_bus=saida).rodar()


if __name__ == "__main__":
    main()
