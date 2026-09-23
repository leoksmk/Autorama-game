// A lista de opções da tela de configurações.
//
// Cada opção sabe só três coisas: como se chama, como ler o valor atual e como
// mudá-lo para o lado. Quem liga isso ao jogo é o Main, e quem desenha é o Hud
// — assim acrescentar uma opção é acrescentar uma linha, e não mexer em três
// arquivos.
//
// Mouse e teclado chegam aqui pelo mesmo caminho (Mover / Mudar). Numa feira o
// jogo roda com dois controles ESP e ninguém quer procurar o mouse, então tudo
// que dá para clicar também dá para fazer com as setas.

using System;
using System.Collections.Generic;

namespace OrbitalDerby.Interface;

public sealed class Opcao
{
    public required string Rotulo { get; init; }
    public required Func<string> Valor { get; init; }

    /// <summary>Linha de apoio abaixo do valor. Vazia some.</summary>
    public Func<string> Detalhe { get; init; } = () => "";

    /// <summary>Anda um passo: +1 para a direita, -1 para a esquerda.</summary>
    public required Action<int> Mudar { get; init; }
}

public sealed class Configuracoes
{
    private readonly List<Opcao> _opcoes = new();

    public bool Aberta { get; private set; }
    public int Linha { get; private set; }

    /// <summary>
    /// O painel está mostrando as regras em vez da lista de opções. As regras
    /// vivem aqui, e não na tela inicial, porque a tela inicial é o que um
    /// desconhecido vê de longe: ali cabe o nome do jogo e como começar, mais
    /// nada. Quem quer saber a regra abre a engrenagem.
    /// </summary>
    public bool MostrandoRegras { get; private set; }
    public IReadOnlyList<Opcao> Opcoes => _opcoes;

    /// <summary>Chamado quando algo muda, para o Main salvar e aplicar.</summary>
    public Action? AoMudar;

    public void Acrescentar(Opcao o) => _opcoes.Add(o);

    public void Abrir()
    {
        Aberta = true;
        Linha = 0;
        MostrandoRegras = false;
    }

    public void Fechar()
    {
        Aberta = false;
        MostrandoRegras = false;
    }

    public void AlternarRegras() => MostrandoRegras = !MostrandoRegras;

    public void Alternar()
    {
        if (Aberta) Fechar();
        else Abrir();
    }

    public void Mover(int passo)
    {
        if (_opcoes.Count == 0) return;
        // Com as regras abertas, qualquer passo fecha elas e devolve a lista:
        // é a saída óbvia para quem só tem dois botões.
        if (MostrandoRegras)
        {
            MostrandoRegras = false;
            return;
        }
        Linha = (Linha + passo + _opcoes.Count) % _opcoes.Count;
    }

    public void Selecionar(int linha)
    {
        if (linha >= 0 && linha < _opcoes.Count)
            Linha = linha;
    }

    public void Mudar(int direcao)
    {
        if (_opcoes.Count == 0) return;
        _opcoes[Linha].Mudar(direcao);
        AoMudar?.Invoke();
    }
}
