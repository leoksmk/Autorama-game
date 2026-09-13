// A trilha, em três camadas que tocam o tempo inteiro e só trocam de volume.
//
//     AMBIENTE  o casco: drone grave com batimento lento. Nunca sai.
//     PULSO     a corrida: batida grave a 112 bpm. Entra na largada.
//     TENSAO    a última volta: segunda menor por cima, tique agudo.
//
// Cruzar volumes em vez de parar e começar faixas é o que mantém tudo em fase:
// PULSO e TENSAO têm o mesmo compasso e foram gerados com o mesmo número de
// amostras, então a camada de tensão entra encaixada no tempo, sem emenda.
//
// Os volumes andam devagar de propósito (~1,5 s de travessia). Numa corrida de
// 5 voltas, camada entrando de supetão soa como erro; entrando por baixo, o
// jogador só percebe que ficou tenso.

using System;
using Godot;

namespace OrbitalDerby.Audio;

public partial class Trilha : Node
{
    private const float Silencio = -60f;

    private readonly AudioStreamPlayer[] _vozes = new AudioStreamPlayer[3];
    private readonly double[] _alvo = new double[3];
    private readonly double[] _agora = new double[3];

    /// <summary>Ganho de cada camada, de 0 a 1. O Som escreve, o _Process persegue.</summary>
    public double Ambiente { get => _alvo[0]; set => _alvo[0] = Math.Clamp(value, 0, 1); }
    public double Pulso { get => _alvo[1]; set => _alvo[1] = Math.Clamp(value, 0, 1); }
    public double Tensao { get => _alvo[2]; set => _alvo[2] = Math.Clamp(value, 0, 1); }

    public override void _Ready()
    {
        var streams = new[]
        {
            Banco.Ambiente().ParaWav(loop: true),
            Banco.PulsoCorrida().ParaWav(loop: true),
            Banco.TensaoFinal().ParaWav(loop: true),
        };

        for (int i = 0; i < _vozes.Length; i++)
        {
            var p = new AudioStreamPlayer
            {
                Stream = streams[i],
                Bus = Mixagem.Musica,
                VolumeDb = Silencio,
            };
            AddChild(p);
            p.Play();
            _vozes[i] = p;
        }
        Ambiente = 1.0;
    }

    public override void _Process(double delta)
    {
        double passo = Math.Min(1.0, delta / 1.5);
        for (int i = 0; i < _vozes.Length; i++)
        {
            _agora[i] += (_alvo[i] - _agora[i]) * passo;
            _vozes[i].VolumeDb = _agora[i] < 0.005
                ? Silencio
                : Mathf.LinearToDb((float)_agora[i]);
        }
    }
}
