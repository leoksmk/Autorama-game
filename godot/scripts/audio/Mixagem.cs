// Os canais da mesa de som, montados em código como todo o resto do projeto.
//
//     MOTOR ─┐
//     SFX   ─┼─> MASTER (limitador)
//     MUSICA ┘
//
// Três canais separados existem por um motivo prático: numa feira o motor
// precisa poder subir sozinho, e a música precisa poder sumir sem levar junto
// o aviso de superaquecimento. O limitador no master é obrigatório — dois
// motores saturados mais uma explosão estouram o teto com facilidade, e
// clipping digital numa caixa de som barata vira chiado.

using Godot;

namespace OrbitalDerby.Audio;

public static class Mixagem
{
    public const string Motor = "Motor";
    public const string Sfx = "Sfx";
    public const string Musica = "Musica";

    private static bool _montada;

    /// <summary>Volume de cada canal, em dB. Calibrado com os dois motores a todo vapor.</summary>
    private static readonly (string Nome, float Db)[] Canais =
    {
        (Motor, -4f),
        (Sfx, -3f),
        (Musica, -13f),
    };

    public static void Montar()
    {
        if (_montada)
            return;
        _montada = true;

        foreach (var (nome, db) in Canais)
        {
            if (AudioServer.GetBusIndex(nome) >= 0)
                continue;
            int i = AudioServer.BusCount;
            AudioServer.AddBus(i);
            AudioServer.SetBusName(i, nome);
            AudioServer.SetBusSend(i, "Master");
            AudioServer.SetBusVolumeDb(i, db);
        }

        // Teto do master. Sem isso, uma bomba caindo em cima dos dois motores
        // no talo passa de 0 dBFS e a saída distorce.
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.8f }, 0);
        AudioServer.SetBusVolumeDb(0, -1f);
    }

    public static void Volume(string canal, float db)
    {
        int i = AudioServer.GetBusIndex(canal);
        if (i >= 0)
            AudioServer.SetBusVolumeDb(i, db);
    }

    /// <summary>Silêncio geral (tecla M). Silencia o master e não o jogo: a corrida segue.</summary>
    public static void Surdina(bool ligada) => AudioServer.SetBusMute(0, ligada);

    public static bool EstaEmSurdina() => AudioServer.IsBusMute(0);
}
