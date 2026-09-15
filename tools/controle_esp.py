# -*- coding: utf-8 -*-
"""
Utilitário de bancada para os controles ESP32 do Orbital Derby.

Serve para testar a fiação e configurar os controles SEM abrir o jogo:

    python tools/controle_esp.py                      lista os controles plugados
    python tools/controle_esp.py --monitor COM5       mostra os apertos ao vivo
    python tools/controle_esp.py --achar-pino COM5    em que GPIO está o switch?
    python tools/controle_esp.py --diagnostico COM5   nível cru dos pinos
    python tools/controle_esp.py --definir-id COM5 2  transforma o controle em ÍGNIS

O botão não responde? Comece pelo --achar-pino: ele separa "o #define aponta
para o pino errado" de "o switch não está chegando a GPIO nenhum", que são
problemas diferentes e não dá para distinguir olhando o código.

Precisa de pyserial (pip install pyserial).

O id (1 = ÍON, 2 = ÍGNIS) fica gravado no próprio ESP. O número da porta COM
não serve para identificar o controle: o Windows troca esse número quando o
cabo muda de entrada USB.
"""

from __future__ import annotations

import argparse
import sys
import time

try:
    import serial
    from serial.tools import list_ports
except ImportError:
    sys.exit("pyserial não encontrado: pip install pyserial")

BAUD = 115200
NOMES = {1: "ÍON", 2: "ÍGNIS"}


def abrir(porta: str) -> serial.Serial:
    # DTR/RTS desligados: em muitas placas eles são ligados ao reset do ESP.
    s = serial.Serial()
    s.port = porta
    s.baudrate = BAUD
    s.timeout = 0.1
    s.dtr = False
    s.rts = False
    s.open()
    return s


def perguntar_hello(s: serial.Serial, espera: float = 2.0) -> str | None:
    """Manda "?" e espera a apresentação. Tolera o lixo de boot do ESP."""
    fim = time.time() + espera
    s.reset_input_buffer()
    s.write(b"?\n")
    ultimo_envio = time.time()
    while time.time() < fim:
        linha = s.readline().decode("utf-8", "replace").strip()
        i = linha.find("HELLO ORBITAL")
        if i >= 0:
            return linha[i:]
        if time.time() - ultimo_envio > 0.5:
            # O ESP pode ter acabado de reiniciar ao abrir a porta.
            s.write(b"?\n")
            ultimo_envio = time.time()
    return None


def descrever(hello: str) -> str:
    partes = hello.split()
    try:
        ident = int(partes[2])
        versao = partes[3] if len(partes) > 3 else "?"
        return f"id {ident} ({NOMES.get(ident, '?')}), firmware {versao}"
    except (IndexError, ValueError):
        return hello


def listar() -> int:
    portas = list(list_ports.comports())
    if not portas:
        print("Nenhuma porta COM encontrada. O ESP está plugado com um cabo USB de DADOS?")
        print("(Cabo só de carga não cria porta.) A SuperMini não precisa de driver;")
        print("placas com conversor separado, como o ESP32 clássico, precisam do CP210x ou CH340.")
        return 1
    achou = 0
    for p in portas:
        try:
            with abrir(p.device) as s:
                hello = perguntar_hello(s)
        except (serial.SerialException, OSError) as e:
            print(f"  {p.device:8} {p.description[:40]:40} ocupada ou inacessível ({e.__class__.__name__})")
            continue
        if hello:
            achou += 1
            print(f"  {p.device:8} CONTROLE ORBITAL — {descrever(hello)}")
        else:
            print(f"  {p.device:8} {p.description[:40]:40} não respondeu (não é um controle Orbital)")
    return 0 if achou else 1


def monitor(porta: str) -> int:
    with abrir(porta) as s:
        hello = perguntar_hello(s)
        print(f"{porta}: {descrever(hello) if hello else 'sem apresentação'}")
        print("Aperte os switches (Ctrl+C para sair). T = acelerador, A = ação.\n")
        ultimo_t = None
        try:
            while True:
                linha = s.readline().decode("utf-8", "replace").strip()
                if not linha:
                    continue
                if linha == "T1":
                    agora = time.time()
                    ritmo = f"  ({1 / (agora - ultimo_t):.1f} cliques/s)" if ultimo_t else ""
                    ultimo_t = agora
                    print(f"acelerador ▼{ritmo}")
                elif linha == "T0":
                    print("acelerador ▲")
                elif linha == "A1":
                    print("ação ▼")
                elif linha == "A0":
                    print("ação ▲")
                elif linha.startswith("K "):
                    pass  # pulsação: silenciosa
                else:
                    print(f"  {linha}")
        except KeyboardInterrupt:
            return 0


def achar_pino(porta: str) -> int:
    """
    Pergunta à placa em que GPIO o switch está ligado.

    Existe porque "o botão não funciona" tem duas causas muito diferentes e
    olhar o código não separa uma da outra: ou o #define aponta para o pino
    errado, ou o switch não está chegando eletricamente a lugar nenhum. A placa
    liga o pull-up em todos os pinos livres e diz qual vai a zero.
    """
    with abrir(porta) as s:
        hello = perguntar_hello(s)
        if not hello:
            print(f"{porta} não respondeu como controle Orbital.")
            return 1
        print(f"{porta}: {descrever(hello)}\n")

        s.write(b"VARRER\n")
        s.timeout = 0.5
        fim = time.time() + 15
        achou_pino = False
        while time.time() < fim:
            linha = s.readline().decode("utf-8", "replace").strip()
            if not linha:
                continue
            if linha.startswith("[scan]"):
                print(linha[7:])
                if "O SWITCH ESTA AQUI" in linha:
                    achou_pino = True
                if linha.endswith("fim."):
                    break
    if not achou_pino:
        print("\nNenhum pino respondeu ao aperto. Isso é elétrico, não é software.")
        return 1
    print("\nSe o GPIO acima não é o do #define, corrija PINO_ACELERADOR /")
    print("PINO_ACAO no firmware e grave de novo.")
    return 0


def diagnostico(porta: str) -> int:
    """Mostra o nível CRU dos dois pinos, ao vivo. Ctrl+C para sair."""
    with abrir(porta) as s:
        hello = perguntar_hello(s)
        print(f"{porta}: {descrever(hello) if hello else 'sem apresentação'}")
        s.write(b"D 1\n")
        print("cru=1 solto, cru=0 apertado. Se o cru não muda ao apertar, o")
        print("aperto não está chegando no pino. Ctrl+C para sair.\n")
        try:
            while True:
                linha = s.readline().decode("utf-8", "replace").strip()
                if linha.startswith("[ctrl]"):
                    print(linha[7:])
        except KeyboardInterrupt:
            try:
                s.write(b"D 0\n")
            except serial.SerialException:
                pass
            return 0


def definir_id(porta: str, ident: int) -> int:
    if ident not in (1, 2):
        print("O id tem de ser 1 (ÍON) ou 2 (ÍGNIS).")
        return 2
    with abrir(porta) as s:
        if not perguntar_hello(s):
            print(f"{porta} não respondeu como controle Orbital.")
            return 1
        s.write(f"ID {ident}\n".encode())
        fim = time.time() + 2.0
        while time.time() < fim:
            linha = s.readline().decode("utf-8", "replace").strip()
            if "HELLO ORBITAL" in linha:
                print(f"{porta}: {descrever(linha[linha.find('HELLO ORBITAL'):])}")
                return 0
    print("Sem confirmação do ESP.")
    return 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--monitor", metavar="PORTA", help="mostra os apertos de um controle ao vivo")
    ap.add_argument("--achar-pino", metavar="PORTA", help="descobre em que GPIO o switch está ligado")
    ap.add_argument("--diagnostico", metavar="PORTA", help="nível cru dos pinos, ao vivo")
    ap.add_argument("--definir-id", nargs=2, metavar=("PORTA", "ID"), help="grava o id (1 ou 2) no controle")
    a = ap.parse_args()

    if a.monitor:
        return monitor(a.monitor)
    if a.achar_pino:
        return achar_pino(a.achar_pino)
    if a.diagnostico:
        return diagnostico(a.diagnostico)
    if a.definir_id:
        return definir_id(a.definir_id[0], int(a.definir_id[1]))
    return listar()


if __name__ == "__main__":
    sys.exit(main())
