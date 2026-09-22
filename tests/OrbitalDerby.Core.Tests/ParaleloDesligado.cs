using Xunit;

// O circuito em uso é estado GLOBAL da regra (Cfg.Checkpoints, Cfg.VoltasParaVencer,
// Cfg.RitmoDoCircuito) — tem de ser, porque só existe uma pista por vez rodando.
// Com as classes de teste em paralelo, um teste que troca de circuito envenenava
// outro que estava medindo a física, e o de paridade com o Python quebrava sem
// que nada de errado tivesse acontecido no jogo.
//
// Desligar o paralelismo é a resposta certa aqui, e não esconder o global atrás
// de uma injeção: a suíte inteira roda em ~70 ms.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
