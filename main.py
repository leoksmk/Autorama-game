# -*- coding: utf-8 -*-
"""
ORBITAL DERBY — ponto de entrada.

    python main.py                 teclado + controles ESP32, se houver
    python main.py --teclado       só teclado (não abre porta COM nenhuma)
    python main.py --esp           só os controles ESP32

Por padrão as duas entradas valem ao mesmo tempo: quem apertar, vale. Assim dá
para jogar com um controle e um teclado enquanto o segundo ESP não fica pronto,
e o teclado continua funcionando se um cabo cair no meio da corrida.

O mesmo arquivo é o ponto de entrada da versão web (pygbag/WebAssembly), por
isso o `asyncio.run` no fim: o navegador precisa que o loop devolva o controle
uma vez por frame. No navegador não existe porta COM, então lá é sempre
teclado — e o import do pyserial nem chega a acontecer.

A saída continua nula: `NullOutput` só alimenta a telemetria. Para migrar ao
motor, troque por `PwmOutput` (drivers/output_bus.py). A lógica não muda.
"""

import asyncio
import sys

# O import de pygame precisa estar AQUI, no ponto de entrada, mesmo que este
# arquivo não o use: o pygbag decide quais pacotes embarcar no WebAssembly
# analisando os imports do main.py. Importado só lá dentro de game/, o pygame
# chegava ao navegador como um stub vazio e o jogo morria no pygame.init().
import pygame  # noqa: F401  (necessário para o build web)

from game.app import App

NO_NAVEGADOR = sys.platform == "emscripten"


def montar_entrada():
    """
    Escolhe o driver de entrada a partir da linha de comando.

    O `import` do serial_driver é adiado de propósito: ele exige pyserial, que
    não existe no WebAssembly. Fora do caminho estático de imports do main.py,
    o pygbag nem tenta embarcá-lo.
    """
    from drivers.input_driver import CombinedDriver, KeyboardDriver

    teclado = KeyboardDriver()
    so_teclado = "--teclado" in sys.argv or "--sem-esp" in sys.argv
    so_esp = "--esp" in sys.argv or "--controles" in sys.argv

    if so_teclado or NO_NAVEGADOR:
        return teclado

    try:
        from drivers.serial_driver import ControleEspDriver
    except ImportError as e:
        if so_esp:
            raise SystemExit(f"--esp pedido, mas {e}")
        print(f"{e} — seguindo só com o teclado (--teclado silencia este aviso).")
        return teclado

    esp = ControleEspDriver()
    print("controles ESP32: varrendo as portas COM. --teclado desliga a varredura.")
    return esp if so_esp else CombinedDriver(teclado, esp)


async def main() -> None:
    from drivers.output_bus import NullOutput

    entrada = montar_entrada()
    saida = NullOutput(lanes=2)

    await App(input_driver=entrada, output_bus=saida).rodar()


asyncio.run(main())
