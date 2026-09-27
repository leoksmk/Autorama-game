# -*- coding: utf-8 -*-
"""
Empacota a versão 3D (Godot/C#) como um .zip que abre com dois cliques.

    python tools/build_exe.py

O que sai: `dist/Orbital-Derby-Windows.zip`. Dentro dele, uma pasta
"Orbital Derby" com o executável, o .pck, as DLLs do .NET e o LEIA-ME.txt. Quem
baixa descompacta e clica — não precisa de Godot, .NET SDK nem Python.

O QUE PRECISA ESTAR INSTALADO (uma vez)

- Godot **4.7.2 .NET** (edição mono). O script procura no mesmo lugar que o
  `godot/jogar.bat`; se estiver em outro, defina a variável GODOT.
- .NET SDK 9 (o export compila o C#).
- Os **export templates** do Godot, que NÃO vêm com o editor: abra o Godot e vá
  em Editor > Gerenciar Modelos de Exportação > Baixar e Instalar. É ~1 GB, uma
  vez só. Sem eles o export falha dizendo "No export template found".

POR QUE UM SCRIPT, E NÃO `godot --export-release` direto

Quatro coisas que dão errado em silêncio:

1. O Godot só exporta um projeto já IMPORTADO — num clone novo não existe
   `godot/.godot/`, e o export sai com o .pck sem os recursos. Daí o `--import`.
2. O `.sln` do C# também não está versionado (é o editor que o escreve), então o
   `--build-solutions` vem antes do export.
3. Conferir se o executável existe NÃO prova que deu certo: ele é uma cópia do
   export template e aparece mesmo quando o C# não compila. O que prova é a
   pasta `data_*_windows_x86_64/` com o OrbitalDerby.dll dentro — é isso que
   este script exige antes de empacotar.
4. O export deixa os arquivos soltos. Se alguém mandar só o executável, ele não
   abre; o .zip com a pasta dentro força o conjunto a viajar junto, e leva o
   LEIA-ME.txt com as teclas e o contorno do "Windows protegeu seu PC".

Este mesmo script é o que roda no GitHub Actions
(.github/workflows/release-windows.yml). Lá o Godot é baixado na hora e o
caminho chega pela variável GODOT — é o único motivo de ela existir.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import zipfile
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
PROJETO = RAIZ / "godot"
DIST = RAIZ / "dist"

# Tem que casar com o `name=` do godot/export_presets.cfg. Uma palavra só, sem
# espaço, para não depender de como cada shell cita o argumento.
PRESET = "windows"
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


def rodar(godot: Path, *args: str) -> tuple[int, str]:
    """
    Chama o Godot em headless dentro de godot/ e DEVOLVE a saída dele.

    A saída é capturada e reimpressa, em vez de ir direto para o terminal, por um
    motivo prático: no GitHub Actions ela se perdia inteira, e aí um export que
    falhou pela metade (executável criado, C# não) ficava idêntico a um que deu
    certo. Quem lê o log precisa ver o que o Godot disse.
    """
    cmd = [str(godot), "--headless", "--path", str(PROJETO), *args]
    print("  $ " + " ".join(cmd), flush=True)
    r = subprocess.run(
        cmd, cwd=PROJETO, text=True, errors="replace",
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    )
    for linha in (r.stdout or "").splitlines():
        print("  | " + linha, flush=True)
    print(f"  (codigo {r.returncode})", flush=True)
    return r.returncode, r.stdout or ""


def pasta_do_dotnet(saida: Path) -> Path | None:
    """
    Acha a pasta `data_*_windows_x86_64/` que o export .NET cria ao lado do
    executável: é onde ficam o runtime do .NET e as DLLs do jogo.

    Ela é a prova de que o C# entrou no pacote. Sem ela o executável abre e morre
    reclamando de assembly.
    """
    for d in sorted(saida.glob("data_*")):
        if d.is_dir():
            return d
    return None


def zipar(pasta: Path, destino: Path) -> None:
    """
    Zipa `pasta` mantendo o nome dela como raiz dentro do .zip.

    Isso importa: um .zip que despeja dezenas de arquivos soltos na pasta de
    Downloads é a primeira forma de perder o jogador. Com a pasta dentro,
    descompactar dá uma coisa só para abrir.
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

    # 1) Importar. Sem .godot/ o export gera um .pck sem os recursos. O --import
    # faz o editor varrer tudo e sair. Em algumas versões ele devolve código != 0
    # mesmo dando certo, então o que se confere é o .godot/.
    print("\n[1/5] importando o projeto (gera .godot/) ...")
    rodar(godot, "--import")
    if not (PROJETO / ".godot").exists():
        print("o --import nao gerou godot/.godot/ — o export nao vai funcionar.")
        return 1

    # 2) Compilar o C#. Num clone novo não existe .sln, e sem ele o export sai
    # com executável e .pck mas sem o .NET.
    print("\n[2/5] compilando o C# (gera o .sln se faltar) ...")
    rodar(godot, "--build-solutions", "--quit")

    print(f"\n[3/5] exportando o preset '{PRESET}' ...")
    codigo, _ = rodar(godot, "--export-release", PRESET, str(saida / EXE))
    if not (saida / EXE).exists():
        print(f"\no export nao gerou {saida / EXE} (codigo {codigo}).")
        print("Se a mensagem acima fala em 'export template', faltam os")
        print("modelos: Godot > Editor > Gerenciar Modelos de Exportacao.")
        return 1

    dotnet_dir = pasta_do_dotnet(saida)
    if dotnet_dir is None:
        print("\nO export saiu SEM a pasta data_*_windows_x86_64/ do .NET.")
        print("O executavel existe, mas nao abre: falta o runtime e as DLLs.")
        print("Procure na saida do Godot acima o erro de build do C#.")
        return 1

    dlls = sorted(d.name for d in dotnet_dir.glob("*.dll"))
    print(f"  .NET: {dotnet_dir.name}/ com {len(dlls)} DLLs")
    if "OrbitalDerby.dll" not in dlls:
        print("\nA pasta do .NET existe mas nao tem o OrbitalDerby.dll: o")
        print("runtime foi junto e o codigo do jogo nao. Veja o log acima.")
        return 1

    # O leia-me vai junto porque é onde o jogador acha as teclas e o contorno do
    # aviso do SmartScreen — sem isso muita gente desiste na tela "O Windows
    # protegeu seu PC".
    print("\n[4/5] copiando o LEIA-ME.txt ...")
    shutil.copy2(LEIA_ME, saida / "LEIA-ME.txt")

    print("\n[5/5] zipando ...")
    DIST.mkdir(exist_ok=True)
    pacote = DIST / ZIP
    pacote.unlink(missing_ok=True)
    zipar(saida, pacote)

    print(f"\npronto: {pacote}  ({pacote.stat().st_size / 1_048_576:.0f} MB)")
    print(f"conteudo de {saida.name}/:")
    for f in sorted(saida.iterdir()):
        if f.is_dir():
            n = sum(1 for x in f.rglob("*") if x.is_file())
            print(f"  {f.name}/  ({n} arquivos)")
        else:
            print(f"  {f.name}  ({f.stat().st_size / 1_048_576:.1f} MB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
