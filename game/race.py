# -*- coding: utf-8 -*-
"""
Orquestração da corrida.

Junta naves, estação e itens, e é o único lugar que fala com o OutputBus.
Não lê teclado, não desenha nada: recebe um dicionário de botões e um de bordas,
e escreve PWM no barramento.

As animações entram por injeção (`fx`). Se nenhuma for passada, a corrida roda
igual com um objeto que engole as chamadas — é assim que os testes headless
funcionam sem tocar em pygame.
"""

from __future__ import annotations

from . import config as cfg
from . import items, track
from .items import GIRANDO, PARADA, REVELANDO
from .ship import Ship
from .station import Iris9


class _SemEfeitos:
    """Objeto nulo: aceita qualquer chamada de animação e não faz nada."""

    def __getattr__(self, _nome):
        return lambda *a, **k: None


class Race:
    def __init__(self, output_bus, fx=None) -> None:
        self.bus = output_bus
        self.fx = fx or _SemEfeitos()
        self.naves: list[Ship] = []
        self.iris = Iris9()
        self.tempo = 0.0
        self.vencedor: Ship | None = None
        self.log: list[tuple[str, tuple[int, int, int]]] = []
        # Ataques a caminho do alvo. O efeito só vale na chegada.
        self.em_voo: list[dict] = []
        self.reset()

    # -- ciclo de vida ------------------------------------------------------

    def reset(self) -> None:
        self.naves = [
            Ship(0, cfg.NOME_P1, cfg.COR_P1, 0.0),
            Ship(1, cfg.NOME_P2, cfg.COR_P2, 0.0),
        ]
        self.iris.reset()
        self.tempo = 0.0
        self.vencedor = None
        self.log = []
        self.em_voo = []
        self.fx.limpar()
        for nave in self.naves:
            self.bus.set_lane(nave.lane, 0.0, "parado")

    def adversario(self, nave: Ship) -> Ship:
        return self.naves[1 - nave.lane]

    def classificacao(self) -> list[Ship]:
        """
        Naves ordenadas do primeiro para o último.

        Quem já cruzou a linha vem antes, pelo tempo de chegada. Ordenar só por
        progresso colocaria em primeiro quem andou mais depois da bandeira.
        """
        return sorted(
            self.naves,
            key=lambda s: (0, s.tempo_final) if s.terminou else (1, -s.progresso),
        )

    def posicao(self, nave: Ship) -> int:
        return self.classificacao().index(nave) + 1

    def anotar(self, texto: str, cor: tuple[int, int, int] = cfg.COR_TEXTO) -> None:
        """Linha curta para o rodapé. Mantém só as últimas."""
        self.log.append((texto, cor))
        del self.log[:-4]

    # -- ponte para o hardware ---------------------------------------------

    def sync_position(self, lane: int, t: float) -> None:
        """
        Corrige a posição estimada de uma pista a partir de um sensor real.

        Não é chamado nesta etapa. Quando os sensores de checkpoint existirem,
        o driver de entrada chamará isto com o t configurado do sensor que
        disparou. A lógica de jogo não muda: ela já trata t como estimativa, e
        a passagem registrada aqui abre a janela da roleta pelo mesmo caminho
        que a integração abre hoje.
        """
        self.naves[lane].sync_position(t)

    # -- passo de simulação -------------------------------------------------

    def atualizar(self, dt: float, botoes: dict, bordas: dict, correndo: bool) -> None:
        # O cronômetro corre enquanto ainda houver alguém na pista: o segundo
        # colocado também merece um tempo registrado.
        if correndo and not all(n.terminou for n in self.naves):
            self.tempo += dt

        # A ÍRIS-9 só é cenário enquanto IRIS_EVENTO_ATIVO estiver desligado.
        # O maquinário continua aqui, pronto para voltar.
        if not cfg.IRIS_EVENTO_ATIVO:
            self.iris.animar(dt)          # só o diafragma respirando
        elif correndo:
            self.iris.atualizar(dt, self.naves)
            for lane, resultado in self.iris.eventos:
                nave = self.naves[lane]
                self.fx.varredura(nave, resultado)
                if resultado == "atingido":
                    self.anotar(f"{nave.nome} levou a varredura", cfg.COR_ALERTA)
                elif resultado == "bloqueado":
                    self.anotar(f"Escudo de {nave.nome} bloqueou", cfg.COR_OK)

        if correndo:
            self._resolver_voos(dt)

        prefixos = ("p1", "p2")
        for nave in self.naves:
            px = prefixos[nave.lane]

            # A roleta avança sua máquina de estados. A nave não para por
            # causa dela: o giro acontece com o carrinho em movimento.
            premio = nave.roleta.atualizar(dt)
            if premio is not None:
                self._entregar_premio(nave, premio)

            # Botão de ação, só na borda de subida.
            if correndo and bordas.get(f"{px}_action"):
                self._acao(nave)

            # O acelerador é de MARTELAR: o que conta é a borda de subida,
            # não o estado do botão. Segurar não faz nada.
            over_antes = nave.superaquecimento
            nave.atualizar(dt, bordas.get(f"{px}_throttle", False), correndo)
            if nave.superaquecimento > 0.0 and over_antes <= 0.0:
                self.fx.superaquecimento(nave)

            # Passou por um sensor. Com o slot vazio, isso abre a janela de
            # roleta — que é de tempo, e fecha sozinha se o jogador não apertar.
            if (
                correndo
                and nave.checkpoint_cruzado is not None
                and nave.item is None
                and nave.roleta.estado == PARADA
                and not nave.terminou
            ):
                nave.roleta.abrir(nave.checkpoint_cruzado)

        # Chegada. Marcar quem chegou é independente de quem venceu: durante a
        # espera do placar o segundo colocado ainda pode cruzar a linha.
        if correndo:
            for nave in self.naves:
                if nave.voltas >= cfg.VOLTAS_PARA_VENCER and not nave.terminou:
                    nave.terminou = True
                    nave.tempo_final = self.tempo
                    nave.roleta.cancelar()
                    if self.vencedor is None:
                        self.vencedor = nave
                        self.anotar(f"{nave.nome} completou {cfg.VOLTAS_PARA_VENCER} voltas", nave.cor)
                    else:
                        self.anotar(f"{nave.nome} chegou em seguida", nave.cor)

        # Único ponto de escrita no barramento. Toda alteração de velocidade
        # passou por aqui antes de virar movimento.
        for nave in self.naves:
            self.bus.set_lane(nave.lane, nave.pwm, nave.effect_tag())

    # -- ataques em voo -----------------------------------------------------

    def _resolver_voos(self, dt: float) -> None:
        """
        Um ataque só acerta quando chega.

        A mira é decidida no disparo (o Tiro precisa do alvo perto e à frente),
        mas o efeito no PWM só é aplicado aqui, no fim do voo. É o que permite
        levantar o Escudo com a bomba já no ar — e é o que faz o impacto na
        tela acontecer no mesmo instante em que o carrinho perderia força.
        """
        if not self.em_voo:
            return

        chegaram = []
        restantes = []
        for voo in self.em_voo:
            voo["t"] -= dt
            (chegaram if voo["t"] <= 0.0 else restantes).append(voo)
        self.em_voo = restantes

        for voo in chegaram:
            origem = self.naves[voo["origem"]]
            alvo = self.naves[voo["alvo"]]
            tipo = voo["tipo"]
            resultado = alvo.receber(tipo)
            self.fx.impacto(alvo, tipo, resultado)
            nome = items.nome_legivel(tipo)
            if resultado == "atingido":
                if tipo == "bomba":
                    self.anotar(f"Bomba de {origem.nome} parou {alvo.nome}", origem.cor)
                else:
                    self.anotar(f"Tiro de {origem.nome} acertou {alvo.nome}", origem.cor)
            else:
                self.anotar(f"{alvo.nome} bloqueou o {nome.lower()}", cfg.COR_OK)

    # -- roleta -------------------------------------------------------------

    def _entregar_premio(self, nave: Ship, premio: str) -> None:
        """A revelação acabou: o item vai para o slot — se houver item."""
        if premio == "nada":
            nave.item = None
            nave.mensagem("Não veio nada")
            self.anotar(f"{nave.nome} girou e não veio nada", cfg.COR_TEXTO_FRACO)
        else:
            nave.item = premio
            nave.mensagem(f"{items.nome_legivel(premio)} no slot")
            self.anotar(f"{nave.nome} pegou {items.nome_legivel(premio)}", nave.cor)
        self.fx.premio(nave, premio)

    # -- ação do jogador ----------------------------------------------------

    def _acao(self, nave: Ship) -> None:
        """
        Um botão, dois significados, decididos pelo contexto:

        - janela de roleta aberta e slot vazio -> gira (custa velocidade)
        - com item no slot                     -> usa o item, a qualquer momento

        A janela só abre ao cruzar um checkpoint com o slot vazio, e fecha
        sozinha em ROLETA_OPORTUNIDADE segundos. Girar com o slot ocupado não é
        permitido: o slot é único e sem estoque.
        """
        if nave.terminou or nave.roleta.estado in (GIRANDO, REVELANDO):
            return

        if nave.item is None:
            if nave.roleta.pode_girar:
                atrasado = nave.progresso < self.adversario(nave).progresso
                nave.roleta.girar(atrasado)
                nave.mensagem("Girando", cfg.ROLETA_GIRO)
            else:
                nave.mensagem("Nada no slot")
            return

        self._usar_item(nave)

    def _usar_item(self, nave: Ship) -> None:
        """
        Os três poderes. Os dois ofensivos disparam um projétil que percorre a
        pista: o resultado (acertou, bloqueado, errou) já é decidido aqui, e a
        animação leva esse veredito junto para estourar certo na chegada.
        """
        item = nave.item
        alvo = self.adversario(nave)
        nave.item = None

        if item in ("tiro", "bomba"):
            # A mira é conferida no disparo: os dois ataques só pegam alvo
            # PERTO, medido pelo caminho mais curto da pista em qualquer
            # sentido. Errar queima o item — é o que obriga a escolher a hora
            # em vez de apertar assim que a caixa entrega.
            alcance = cfg.TIRO_ALCANCE if item == "tiro" else cfg.BOMBA_ALCANCE
            perto = track.distancia_curta(nave.t, alvo.t) < alcance
            nome = items.nome_legivel(item)
            if perto:
                nave.mensagem(f"{nome} disparado" if item == "tiro" else "Bomba lançada")
                voo = cfg.TIRO_VOO if item == "tiro" else cfg.BOMBA_VOO
                self._lancar(nave, alvo, item, voo)
            else:
                nave.mensagem(f"{nome} errou")
                self.fx.ataque(nave, alvo, item, "errou")
                self.anotar(f"{nome} de {nave.nome} passou longe", cfg.COR_TEXTO_FRACO)

        elif item == "escudo":
            nave.escudo = True
            nave.mensagem("Escudo ativo")
            self.anotar(f"{nave.nome} levantou Escudo", nave.cor)
            self.fx.escudo(nave)

    def _lancar(self, origem: Ship, alvo: Ship, tipo: str, voo: float) -> None:
        """Põe o ataque no ar. Quem resolve o efeito é _resolver_voos()."""
        self.em_voo.append(
            {"origem": origem.lane, "alvo": alvo.lane, "tipo": tipo, "t": voo}
        )
        self.fx.ataque(origem, alvo, tipo, "voando")
