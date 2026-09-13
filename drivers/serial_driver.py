# -*- coding: utf-8 -*-
"""
Controles ESP32 pela serial USB — a mesma coisa que a versão 3D já fazia em C#.

O firmware é `firmware/orbital_controle/orbital_controle.ino`: cada placa é um
teclado de dois botões pela USB, sem rádio e sem Wi-Fi, e diz quem é (1 = ÍON,
2 = ÍGNIS) pelo id gravado nela mesma.

Nada aqui roda na thread do jogo. Uma thread varre as portas COM, cada sondagem
tem a sua, e cada controle conectado tem uma thread só para ler linhas. É de
propósito: no Windows, abrir uma porta COM de Bluetooth pode travar por
segundos, e um cabo puxado no meio da corrida não pode congelar a tela.

POR QUE ISTO NÃO É "LEIA O ESTADO DO PINO POR FRAME"
----------------------------------------------------
O acelerador é de MARTELAR: o que vira velocidade é a borda de subida, e a
60 fps um aperto curto cabe inteiro entre dois frames. Lendo só o estado atual,
esse aperto desaparece. Então o driver conta os apertos que chegaram pela
serial e os entrega um por frame, garantindo um frame solto entre dois — que é
o que o `ButtonEdge` do jogo precisa para enxergar duas bordas distintas.

O contrato com o jogo não muda: `read()` devolve os mesmos quatro booleanos do
`KeyboardDriver`.
"""

from __future__ import annotations

import threading
import time

try:
    import serial
    from serial.tools import list_ports
except ImportError as e:  # pragma: no cover - depende do ambiente
    raise ImportError("pyserial não encontrado: pip install pyserial") from e

from .input_driver import ESTADO_VAZIO, InputDriver

# Protocolo (o mesmo de godot/scripts/core/Protocolo.cs)
BAUD = 115200
ASSINATURA = "HELLO ORBITAL"
PERGUNTA = b"?\n"

# Sem nenhuma linha por este tempo o controle é dado como desconectado. O ESP
# pulsa a cada 500 ms, então 1,6 s tolera três pulsações perdidas.
TIMEOUT_PULSACAO = 1.6

INTERVALO_VARREDURA = 1.5   # de quanto em quanto tempo reolhar as portas COM
ESPERA_RECUSADA = 8.0       # porta que não é controle só é tentada de novo depois disso
ESPERA_SONDAGEM = 2.5       # quanto esperar pelo HELLO antes de desistir da porta

# Teto de apertos guardados. Sem isso, um engasgo de dois segundos viraria uma
# rajada de cliques acumulados e a nave sairia acelerando sozinha.
PENDENTES_MAX = 2

NOMES = {1: "ÍON", 2: "ÍGNIS"}


class ControleEsp:
    """Um ESP32 vivo numa porta COM, com a sua thread de leitura."""

    def __init__(self, porta_serial: serial.Serial, ident: int, versao: str) -> None:
        self._s = porta_serial
        self.porta = porta_serial.port
        self.id = ident
        self.versao = versao

        self._trava = threading.Lock()
        self._segurado = {"T": False, "A": False}
        self._pendentes = {"T": 0, "A": 0}
        self._ultimo = {"T": False, "A": False}

        self._ultima_linha = time.monotonic()
        self._aberto = True
        threading.Thread(target=self._ler, name=f"serial {self.porta}", daemon=True).start()

    # -- estado -------------------------------------------------------------

    @property
    def vivo(self) -> bool:
        """Porta aberta e com sinal de vida recente."""
        return self._aberto and (time.monotonic() - self._ultima_linha) < TIMEOUT_PULSACAO

    def __str__(self) -> str:
        return f"{self.porta} · id {self.id} ({NOMES.get(self.id, '?')}) · fw {self.versao}"

    # -- leitura ------------------------------------------------------------

    def _ler(self) -> None:
        while self._aberto:
            try:
                bruto = self._s.readline()
            except Exception:
                self.fechar()          # cabo puxado, porta sumiu
                return
            if not bruto:
                continue               # só o timeout do readline; quem julga é `vivo`
            self._ultima_linha = time.monotonic()
            self._interpretar(bruto.decode("utf-8", "replace").strip())

    def _interpretar(self, linha: str) -> None:
        if not linha:
            return

        # O ESP cospe o log da ROM ao reiniciar (e abrir a porta reinicia a
        # placa em vários modelos). Procurar a assinatura em qualquer ponto da
        # linha tolera o lixo grudado antes dela.
        i = linha.find(ASSINATURA)
        if i >= 0:
            partes = linha[i + len(ASSINATURA):].split()
            if partes and partes[0] in ("1", "2"):
                with self._trava:
                    self.id = int(partes[0])
                    self.versao = partes[1] if len(partes) > 1 else "?"
            return

        if linha in ("T1", "T0", "A1", "A0"):
            botao, apertado = linha[0], linha[1] == "1"
            with self._trava:
                self._segurado[botao] = apertado
                if apertado:
                    self._pendentes[botao] = min(self._pendentes[botao] + 1, PENDENTES_MAX)
            return

        # "K xy": pulsação com o estado dos dois botões.
        if (len(linha) == 4 and linha[0] == "K" and linha[1] == " "
                and linha[2] in "01" and linha[3] in "01"):
            with self._trava:
                # A pulsação só CORRIGE o estado se um evento se perdeu. Ela
                # nunca cria clique: borda de subida vem de T1/A1, e só.
                self._segurado["T"] = linha[2] == "1"
                self._segurado["A"] = linha[3] == "1"

    # -- consumo pelo jogo --------------------------------------------------

    def amostrar(self) -> tuple[bool, bool]:
        """(acelerador, ação) deste frame, no formato de estado contínuo."""
        with self._trava:
            return self._amostrar("T"), self._amostrar("A")

    def _amostrar(self, botao: str) -> bool:
        # Chamado com a trava tomada.
        if self._pendentes[botao] > 0:
            if self._ultimo[botao]:
                # Já reportamos "apertado" no frame passado. Para o próximo
                # aperto virar uma borda nova, precisa haver um frame solto
                # entre os dois — este. O clique fica na fila, não se perde.
                valor = False
            else:
                valor = True
                self._pendentes[botao] -= 1
        else:
            valor = self._segurado[botao]
        self._ultimo[botao] = valor
        return valor

    # -- saída --------------------------------------------------------------

    def enviar(self, comando: str) -> None:
        """Manda uma linha ao ESP (`ID n`, e o `M <duty>` do dia em que a pista existir)."""
        try:
            self._s.write(f"{comando}\n".encode())
        except Exception:
            self.fechar()

    def fechar(self) -> None:
        self._aberto = False
        try:
            self._s.close()
        except Exception:
            pass


class ControleEspDriver(InputDriver):
    """
    Acha os controles sozinho e entrega os botões no formato do jogo.

    Quem é ÍON e quem é ÍGNIS vem do id que o próprio ESP informa, nunca do
    número da porta: o Windows troca esse número quando o cabo muda de entrada
    USB.
    """

    def __init__(self, iniciar: bool = True) -> None:
        self._trava = threading.Lock()
        self._controles: list[ControleEsp] = []
        self._recusadas: dict[str, float] = {}
        self._sondando: set[str] = set()
        self._rodando = False
        if iniciar:
            self.iniciar()

    def iniciar(self) -> None:
        if self._rodando:
            return
        self._rodando = True
        threading.Thread(target=self._varrer, name="varredura serial", daemon=True).start()

    # -- descoberta ---------------------------------------------------------

    def _varrer(self) -> None:
        while self._rodando:
            try:
                portas = [p.device for p in list_ports.comports()]
            except Exception:
                portas = []

            with self._trava:
                for c in [c for c in self._controles if not c.vivo]:
                    c.fechar()
                    self._controles.remove(c)
                conhecidas = {c.porta for c in self._controles} | self._sondando
                agora = time.monotonic()
                novas = [
                    p for p in dict.fromkeys(portas)
                    if p not in conhecidas
                    and agora - self._recusadas.get(p, -ESPERA_RECUSADA) >= ESPERA_RECUSADA
                ]
                self._sondando.update(novas)

            for p in novas:
                threading.Thread(target=self._sondar, args=(p,), daemon=True).start()

            time.sleep(INTERVALO_VARREDURA)

    def _sondar(self, nome: str) -> None:
        s: serial.Serial | None = None
        try:
            # DTR e RTS desligados: em muitas placas eles resetam o ESP.
            s = serial.Serial()
            s.port = nome
            s.baudrate = BAUD
            s.timeout = 0.25
            s.dtr = False
            s.rts = False
            s.open()
            s.reset_input_buffer()
            s.write(PERGUNTA)

            fim = time.monotonic() + ESPERA_SONDAGEM
            repetir = time.monotonic() + 0.5
            while time.monotonic() < fim and self._rodando:
                linha = s.readline().decode("utf-8", "replace").strip()
                i = linha.find(ASSINATURA)
                if i >= 0:
                    partes = linha[i + len(ASSINATURA):].split()
                    if partes and partes[0] in ("1", "2"):
                        controle = ControleEsp(
                            s, int(partes[0]), partes[1] if len(partes) > 1 else "?"
                        )
                        s = None                      # a porta agora é do controle
                        with self._trava:
                            self._controles.append(controle)
                        print(f"controle ESP32: {controle}")
                        return
                if time.monotonic() > repetir:
                    # O ESP pode ter acabado de reiniciar ao abrir a porta: insiste.
                    s.write(PERGUNTA)
                    repetir = time.monotonic() + 0.5
        except Exception:
            pass                                      # ocupada, sem permissão, sumiu
        finally:
            if s is not None:
                try:
                    s.close()
                except Exception:
                    pass
                with self._trava:
                    self._recusadas[nome] = time.monotonic()
            with self._trava:
                self._sondando.discard(nome)

    # -- atribuição ---------------------------------------------------------

    def atribuicao(self) -> tuple[ControleEsp | None, ControleEsp | None]:
        """
        Quem controla cada nave: primeiro o ESP que se apresentou com o id da
        nave; na falta dele, um controle sobrando. Assim dois ESP com o mesmo
        id ainda jogam — e o status na tela de abertura avisa para acertar.
        """
        with self._trava:
            vivos = sorted((c for c in self._controles if c.vivo), key=lambda c: c.porta)
        ion = next((c for c in vivos if c.id == 1), None)
        ignis = next((c for c in vivos if c.id == 2), None)
        for c in vivos:
            if c is ion or c is ignis:
                continue
            if ion is None:
                ion = c
            elif ignis is None:
                ignis = c
        return ion, ignis

    # -- contrato do InputDriver --------------------------------------------

    def read(self) -> dict:
        estado = dict(ESTADO_VAZIO)
        for prefixo, c in zip(("p1", "p2"), self.atribuicao()):
            if c is None:
                continue
            acel, acao = c.amostrar()
            estado[f"{prefixo}_throttle"] = acel
            estado[f"{prefixo}_action"] = acao
        return estado

    def status(self) -> str:
        partes = [
            f"{NOMES[i + 1]}: {c.porta}" if c else f"{NOMES[i + 1]}: aguardando"
            for i, c in enumerate(self.atribuicao())
        ]
        aviso = " · dois ESP com o mesmo id" if self.ids_repetidos else ""
        return "controle ESP32 — " + " · ".join(partes) + aviso

    def close(self) -> None:
        self._rodando = False
        with self._trava:
            for c in self._controles:
                c.fechar()
            self._controles.clear()

    # -- diagnóstico --------------------------------------------------------

    @property
    def conectados(self) -> int:
        with self._trava:
            return sum(1 for c in self._controles if c.vivo)

    @property
    def ids_repetidos(self) -> bool:
        with self._trava:
            ids = [c.id for c in self._controles if c.vivo]
        return len(ids) != len(set(ids))
