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
    public IReadOnlyList<Opcao> Opcoes => _opcoes;

    /// <summary>Chamado quando algo muda, para o Main salvar e aplicar.</summary>
    public Action? AoMudar;

    public void Acrescentar(Opcao o) => _opcoes.Add(o);

    public void Abrir()
    {
        Aberta = true;
        Linha = 0;
    }

    public void Fechar() => Aberta = false;

    public void Alternar()
    {
        if (Aberta) Fechar();
        else Abrir();
    }

    public void Mover(int passo)
    {
        if (_opcoes.Count == 0) return;
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
