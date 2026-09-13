# -*- coding: utf-8 -*-
"""
Reconstrói um sketch .ino perdido a partir do .ino.cpp que o Arduino deixa
para trás.

    python tools/recuperar_ino.py <arquivo.ino.cpp> [saida.ino]

Sem o segundo argumento, escreve na saída padrão.

Onde procurar os .ino.cpp, do mais volátil para o menos:

    %LOCALAPPDATA%\\Temp\\arduino-language-server*\\{build,fullbuild}\\sketch\\
    %LOCALAPPDATA%\\arduino\\sketches\\<hash>\\sketch\\
    %LOCALAPPDATA%\\Temp\\arduino\\sketches\\<hash>\\sketch\\

Os do language-server somem quando a Arduino IDE fecha. Foi de lá que saiu o
`firmware/recuperado/` — veja o LEIAME de lá.

POR QUE ISTO FUNCIONA
---------------------
O pré-processador do Arduino não reescreve o seu código: ele só insere
protótipos das funções e marca cada trecho com `#line N "arquivo"`, para o
compilador reportar erros na linha certa do .ino. Essas marcas são exatamente
o mapa de volta — cada linha de conteúdo pertence ao número que a última marca
anunciou.

Os protótipos inseridos também recebem `#line`, apontando para a linha da
definição. Eles caem em cima da linha certa e são sobrescritos quando o corpo
da função aparece mais adiante no arquivo. Por isso a reconstrução sai limpa,
sem os protótipos duplicados.

O que NÃO volta: nada. A reconstrução é exata para o .ino. Os `.h` do sketch
ficam no mesmo diretório do .ino.cpp, já em texto puro, mas com uma linha
`#line` a mais no topo — tire a primeira linha e são os originais.
"""

from __future__ import annotations

import re
import sys

MARCA = re.compile(r'^#line (\d+) "(.*)"\s*$')


def reconstruir(texto: str) -> tuple[str, str]:
    """Devolve (fonte reconstruído, caminho original que estava nas marcas)."""
    linhas: dict[int, str] = {}
    origem = ""
    atual: int | None = None

    for bruto in texto.splitlines():
        m = MARCA.match(bruto)
        if m:
            atual = int(m.group(1))
            origem = origem or m.group(2)
            continue
        if atual is None:
            continue          # preâmbulo do pré-processador, antes da 1ª marca
        linhas[atual] = bruto
        atual += 1

    if not linhas:
        return "", origem
    fim = max(linhas)
    return "\n".join(linhas.get(i, "") for i in range(1, fim + 1)) + "\n", origem


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__.strip())
        return 2

    with open(sys.argv[1], encoding="utf-8") as f:
        texto, origem = reconstruir(f.read())

    if not texto:
        print("nenhuma marca #line no arquivo: isto não é um .ino.cpp do Arduino.")
        return 1

    if len(sys.argv) > 2:
        with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
            f.write(texto)
        print(f"origem : {origem}")
        print(f"escrito: {sys.argv[2]} ({len(texto.splitlines())} linhas)")
    else:
        sys.stdout.write(texto)
    return 0


if __name__ == "__main__":
    sys.exit(main())
