# -*- coding: utf-8 -*-
"""
Gera tests/OrbitalDerby.Core.Tests/referencia_python.json a partir da versão
Python (game/), que é a implementação já validada.

Os testes C# rodam exatamente o mesmo roteiro de cliques no porte e comparam
número a número. É assim que se prova que a mudança para o Godot não alterou a
física: sem esta referência, "portei fielmente" seria só uma afirmação.

    python tests/gerar_referencia.py

Nenhum caso usa a caixa de item: o sorteio do Python e o do C# usam geradores
aleatórios diferentes, então só a parte determinística entra na comparação.
"""

import json
import os
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, RAIZ)

from game.ship import Ship            # noqa: E402
from game.race import Race            # noqa: E402
from drivers.output_bus import NullOutput  # noqa: E402

DT = 1.0 / 60.0
DESTINO = os.path.join(RAIZ, "tests", "OrbitalDerby.Core.Tests", "referencia_python.json")


def trajetoria(hz, segundos, soltar_em=None, amostra=15):
    """Uma nave sozinha, martelando a `hz`; opcionalmente solta em `soltar_em` s."""
    nave = Ship(0, "ÍON", (0, 0, 0), 0.0)
    prox = 0.0
    pontos = []
    for i in range(int(segundos * 60)):
        clique = False
        ativo = soltar_em is None or i < soltar_em * 60
        if hz > 0 and ativo and i >= prox:
            clique = True
            prox += 60.0 / hz
        nave.atualizar(DT, clique, True)
        if i % amostra == 0:
            pontos.append({
                "frame": i,
                "t": nave.t,
                "voltas": nave.voltas,
                "speed": nave.speed,
                "pwm": nave.pwm,
                "calor": nave.calor,
                "superaquecimento": nave.superaquecimento,
            })
    return pontos


def corrida(hz1, hz2, limite_s=300):
    """Corrida inteira, dois jogadores martelando em ritmos fixos, sem usar itens."""
    r = Race(NullOutput(2))
    hzs = (hz1, hz2)
    prox = [0.0, 0.0]
    frames = 0
    for i in range(int(limite_s * 60)):
        botoes = {k: False for k in ("p1_throttle", "p1_action", "p2_throttle", "p2_action")}
        bordas = dict(botoes)
        for lane in (0, 1):
            if i >= prox[lane]:
                bordas[f"p{lane + 1}_throttle"] = True
                prox[lane] += 60.0 / hzs[lane]
        r.atualizar(DT, botoes, bordas, True)
        frames = i + 1
        if all(n.terminou for n in r.naves):
            break
    return {
        "hz": [hz1, hz2],
        "frames": frames,
        "vencedor": r.vencedor.lane if r.vencedor else -1,
        "tempos": [n.tempo_final for n in r.naves],
        "voltas": [n.voltas for n in r.naves],
    }


def main():
    ref = {
        "gerado_por": "tests/gerar_referencia.py",
        "dt": DT,
        "trajetorias": [
            {"nome": "8 Hz por 9 s (passa pelo superaquecimento)", "hz": 8.0, "segundos": 9.0,
             "soltar_em": None, "pontos": trajetoria(8.0, 9.0)},
            {"nome": "4 Hz por 8 s (ritmo sustentavel)", "hz": 4.0, "segundos": 8.0,
             "soltar_em": None, "pontos": trajetoria(4.0, 8.0)},
            {"nome": "10 Hz por 3 s e solta", "hz": 10.0, "segundos": 5.0,
             "soltar_em": 3.0, "pontos": trajetoria(10.0, 5.0, soltar_em=3.0)},
            {"nome": "14 Hz por 14 s (cortes em ciclo)", "hz": 14.0, "segundos": 14.0,
             "soltar_em": None, "pontos": trajetoria(14.0, 14.0)},
        ],
        "corridas": [
            corrida(4.5, 6.0),
            corrida(7.0, 5.0),
        ],
    }
    with open(DESTINO, "w", encoding="utf-8") as f:
        json.dump(ref, f, ensure_ascii=False, indent=1)
    n_pontos = sum(len(t["pontos"]) for t in ref["trajetorias"])
    print(f"referencia gravada em {DESTINO}")
    print(f"  {len(ref['trajetorias'])} trajetorias, {n_pontos} amostras, {len(ref['corridas'])} corridas")
    for c in ref["corridas"]:
        print(f"  corrida {c['hz']}: vencedor pista {c['vencedor']}, tempos {c['tempos']}")


if __name__ == "__main__":
    main()
