# -*- coding: utf-8 -*-
"""
Saída para o hardware.

Este é o contrato que o back-end vai consumir. Toda mecânica de jogo termina
aqui, em um valor de PWM por pista. Se um poder não consegue ser escrito como
"aumenta o teto", "reduz o teto" ou "zera", ele não existe.

O HUD lê deste barramento para montar a telemetria, de propósito: se um efeito
não aparece no rodapé é porque ele não chegou ao PWM, e portanto não seria
sentido pelo carrinho de verdade.
"""

from __future__ import annotations


class OutputBus:
    """Interface. Recebe o estado de cada pista uma vez por frame."""

    def set_lane(self, lane: int, pwm: float, effect_tag: str = "") -> None:
        raise NotImplementedError

    def get_lane(self, lane: int) -> dict:
        raise NotImplementedError

    def close(self) -> None:
        return None


class NullOutput(OutputBus):
    """
    Implementação de simulação: só guarda o último estado escrito.

    É o que roda hoje. O driver real de hardware substitui esta classe e passa
    a escrever no controlador de PWM, mantendo exatamente a mesma assinatura.
    """

    def __init__(self, lanes: int = 2) -> None:
        self._estado = [
            {"pwm": 0.0, "effect_tag": ""} for _ in range(lanes)
        ]

    def set_lane(self, lane: int, pwm: float, effect_tag: str = "") -> None:
        # Clamp defensivo: nenhum valor fora de [0,1] pode vazar para o motor.
        pwm = 0.0 if pwm < 0.0 else (1.0 if pwm > 1.0 else pwm)
        self._estado[lane]["pwm"] = pwm
        self._estado[lane]["effect_tag"] = effect_tag

    def get_lane(self, lane: int) -> dict:
        return self._estado[lane]


class PwmOutput(OutputBus):
    """
    STUB. Não importa biblioteca de hardware e não é usado nesta etapa.

    Mapa de saída pretendido:

        pista 0 -> PWM canal 0 (ponte H, ~1 kHz)
        pista 1 -> PWM canal 1

    Implementação futura, resumida:

        from gpiozero import PWMOutputDevice
        self._canais = [PWMOutputDevice(18, frequency=1000),
                        PWMOutputDevice(19, frequency=1000)]
        def set_lane(self, lane, pwm, effect_tag=""):
            self._canais[lane].value = max(0.0, min(1.0, pwm))

    Observação para a montagem: motores de autorama costumam ter uma zona morta
    embaixo (abaixo de ~0,25 de duty o carrinho não sai do lugar). Se for esse o
    caso, o remapeamento de duty morto pertence a ESTA classe, não à lógica de
    jogo. O jogo continua raciocinando em 0..1 lineares.
    """

    def __init__(self) -> None:
        raise NotImplementedError(
            "PwmOutput ainda não implementado. Use NullOutput nesta etapa."
        )
