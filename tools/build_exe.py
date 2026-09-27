# -*- coding: utf-8 -*-
"""
Empacota a versão 3D (Godot/C#) como um .zip que abre com dois cliques.

    python tools/build_exe.py

O que sai: `dist/Orbital-Derby-Windows.zip`. Dentro dele, uma pasta
"Orbital Derby" com o .exe, o .pck, as DLLs do .NET e o LEIA-ME.txt. Quem
baixa descompacta e clica — não precisa de Godot, .NET SDK nem Python.

O QUE PRECISA ESTAR INSTALADO (uma vez)

- Godot **4.7.2 .NET** (edição mono). O script procura no mesmo lugar que o
  `godot/jogar.bat`; se estiver em outro, defina a variável GODOT.
- .NET SDK 9 (o export compila o C#).
- Os **export templates** do Godot, que NÃO vêm com o editor: abra o Godot e
  vá em Editor > Gerenciar Modelos de Exportação > Baixar e Instalar. É ~1 GB,
  uma vez só. Sem eles o export falha dizendo "No export template found".

POR QUE UM SCRIPT, E NÃO `godot --export-release` direto

Três motivos, todos coisas que dão errado silenciosamente:

1. O Godot só exporta um projeto já IMPORTADO — num clone novo não existe
   `godot/.godot/`, e o export sai com o .pck vazio ou quebrado. Por isso o
   passo `--import` antes.
2. O export deixa os arquivos soltos em `dist/Orbital Derby/`. Se alguém
   mandar só o .exe, ele não abre. O .zip com a pasta dentro força o trio a
   viajar junto.
3. O LEIA-ME.txt precisa entrar no pacote — é onde o jogador descobre as
   teclas e o aviso do "Windows protegeu seu PC".

Este mesmo script é o que roda no GitHub Actions
(.github/workflows/release-windows.yml). Lá o Godot é baixado na hora e o
caminho chega pela variável GODOT — é o único motivo de ela existir.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
PROJETO = RAIZ / "godot"
DIST = RAIZ / "dist"

# Tem que casar com o `name=` e o `export_path=` do godot/export_presets.cfg.
PRESET = "Windows Desktop"
PASTA = "Orbital Derby"
EXE = "Orbital Derby.exe"
ZIP = "Orbital-Derby-Windows.zip"

LEIA_ME = RAIZ / "tools" / "leia-me-windows.txt"

GODOT_PADRAO = (
    Path(os.environ.get("LOCALAPPDATA", ""))
    / "Programs" / "Godot"
    / "Godot_v4.7.2-stable_mono_win64"
    / "Godot_v4.7.2-stable_mono_win64.exe"
)


def achar_godot() -> Path | None:
    caminho = Path(os.environ["GODOT"]) if os.environ.get("GODOT") else GODOT_PADRAO
    return caminho if caminho.exists() else None


def rodar(godot: Path, *args: str) -> int:
    """Chama o Godot em headless dentro de godot/, mostrando a saída."""
    cmd = [str(godot), "--headless", "--path", str(PROJETO), *args]
    print("  $ " + " ".join(cmd))
    return subprocess.run(cmd, cwd=PROJETO).returncode


def zipar(pasta: Path, destino: Path) -> None:
    """
    Zipa `pasta` mantendo o nome dela como raiz dentro do .zip.

    Isso importa: um .zip que despeja 40 arquivos soltos na pasta de Downloads
    é a primeira forma de perder o jogador. Com a pasta dentro, descompactar
    dá uma coisa só para abrir.
    """
    with zipfile.ZipFile(destino, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for arquivo in sorted(pasta.rglob("*")):
            if arquivo.is_file():
                z.write(arquivo, Path(pasta.name) / arquivo.relative_to(pasta))


def main() -> int:
    godot = achar_godot()
    if godot is None:
        print("Nao achei o Godot 4.7.2 .NET em:")
        print(f"  {os.environ.get('GODOT') or GODOT_PADRAO}")
        print("Instale-o ou defina a variavel GODOT com o caminho do .exe.")
        return 1

    if shutil.which("dotnet") is None:
        print("dotnet nao esta no PATH. O export do C# precisa do .NET SDK 9.")
        return 1

    if not (PROJETO / "export_presets.cfg").exists():
        print(f"faltando: {PROJETO / 'export_presets.cfg'}")
        return 1

    saida = DIST / PASTA
    if saida.exists():
        shutil.rmtree(saida)
    saida.mkdir(parents=True)

    # 1) Importar. Um projeto Godot recém-clonado não tem .godot/, e sem ele o
    # export gera um .pck sem os recursos. O --import faz o editor varrer tudo
    # e sair. Ele "falha" com código != 0 em algumas versões mesmo dando certo,
    # então o resultado é conferido pela existência de .godot/, não pelo código.
    print("\n[1/4] importando o projeto (gera .godot/) ...")
    rodar(godot, "--import")
    if not (PROJETO / ".godot").exists():
        print("o --import nao gerou godot/.godot/ — o export nao vai funcionar.")
        return 1

    # 2) Exportar. O `--export-release` compila o C# e junta exe + pck + DLLs.
    print(f"\n[2/4] exportando o preset '{PRESET}' ...")
    r = rodar(godot, "--export-release", PRESET, str(saida / EXE))
    if not (saida / EXE).exists():
        print(f"\no export nao gerou {saida / EXE} (codigo {r}).")
        print("Se a mensagem acima fala em 'export template', faltam os")
        print("modelos: Godot > Editor > Gerenciar Modelos de Exportacao.")
        return 1

    # 3) O leia-me. Vai junto porque é onde o jogador acha as teclas e o
    # contorno do aviso do SmartScreen — sem isso muita gente desiste na
    # tela "O Windows protegeu seu PC".
    print("\n[3/4] copiando o LEIA-ME.txt ...")
    shutil.copy2(LEIA_ME, saida / "LEIA-ME.txt")

    print("\n[4/4] zipando ...")
    DIST.mkdir(exist_ok=True)
    pacote = DIST / ZIP
    pacote.unlink(missing_ok=True)
    zipar(saida, pacote)

    print(f"\npronto: {pacote}  ({pacote.stat().st_size / 1_048_576:.0f} MB)")
    print(f"conteudo de {saida.name}/:")
    for f in sorted(saida.iterdir()):
        if f.is_dir():
            n = sum(1 for _ in f.rglob("*") if _.is_file())
            print(f"  {f.name}/  ({n} arquivos)")
        else:
            print(f"  {f.name}  ({f.stat().st_size / 1_048_576:.1f} MB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
