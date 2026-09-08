# -*- coding: utf-8 -*-
"""
Gera a versão web (WebAssembly) do Orbital Derby e a deixa pronta em docs/.

    python tools/build_web.py

Por que existe este script, em vez de chamar o pygbag direto:

O pygbag batiza o pacote com o NOME DA PASTA que contém o main.py. Como esta
pasta se chama "Autorama gameficação", o pacote sairia como
"autorama.gameficação.apk" — acento e cedilha em nome de arquivo servido por
URL, o que quebra no GitHub Pages. Então o build acontece numa cópia
temporária com nome ASCII ("orbital-derby") e o resultado é trazido de volta.

O que é publicado fica em docs/, que é a pasta que o GitHub Pages serve.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
DESTINO = RAIZ / "docs"

# O que o jogo precisa para rodar. Nada de build/, docs/, .git/ ou testes.
FONTES = ("main.py", "game", "drivers")

NOME_ASCII = "orbital-derby"


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="orbital-web-") as tmp:
        projeto = Path(tmp) / NOME_ASCII
        projeto.mkdir()

        for nome in FONTES:
            origem = RAIZ / nome
            if not origem.exists():
                print(f"faltando: {origem}")
                return 1
            if origem.is_dir():
                shutil.copytree(
                    origem, projeto / nome,
                    ignore=shutil.ignore_patterns("__pycache__", "*.pyc"),
                )
            else:
                shutil.copy2(origem, projeto / nome)

        print(f"compilando em {projeto} ...")
        # --ume_block 0 tira a tela de "clique para começar" do pygbag.
        cmd = [
            sys.executable, "-m", "pygbag",
            "--build",
            "--ume_block", "0",
            "--title", "Orbital Derby",
            str(projeto / "main.py"),
        ]
        r = subprocess.run(cmd, cwd=projeto)
        if r.returncode != 0:
            print("pygbag falhou")
            return r.returncode

        saida = projeto / "build" / "web"
        if not (saida / "index.html").exists():
            print(f"build nao gerou index.html em {saida}")
            return 1

        if DESTINO.exists():
            shutil.rmtree(DESTINO)
        shutil.copytree(saida, DESTINO)

        # .nojekyll: sem ele o GitHub Pages ignora arquivos iniciados por "_".
        (DESTINO / ".nojekyll").write_text("", encoding="utf-8")

    print(f"\npronto: {DESTINO}")
    for f in sorted(DESTINO.iterdir()):
        print(f"  {f.name}  ({f.stat().st_size / 1024:.0f} KB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
