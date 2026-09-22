// Despeja o banco inteiro em arquivos .wav, para ouvir e ajustar sem jogar.
//
//     jogar.bat --som-wav=C:\temp\som
//
// Existe por um motivo prático: acertar timbre exige ouvir o som isolado, em
// sequência, muitas vezes. Abrir o jogo e provocar um superaquecimento de
// verdade para escutar um assobio é lento demais para iterar.
//
// Os arquivos são descartáveis — o jogo nunca os lê. Quem toca é a síntese em
// memória, sempre.

using System;
using System.Collections.Generic;
using System.IO;
using OrbitalDerby.Core;

namespace OrbitalDerby.Audio;

public static class Exportar
{
    public static int Tudo(string pasta)
    {
        Directory.CreateDirectory(pasta);
        var fila = new List<(string Nome, Onda O, double Pan)>();

        foreach (int lane in new[] { 0, 1 })
        {
            string p = lane == 0 ? "ion" : "ignis";
            double tom = Banco.Tom(lane);
            double pan = Banco.Pan(lane);
            fila.Add(($"{p}_01_disparo", Banco.Disparo(tom), pan));
            fila.Add(($"{p}_02_bomba_lancada", Banco.BombaLancada(tom), pan));
            fila.Add(($"{p}_03_explosao", Banco.Explosao(), pan * 0.7));
            fila.Add(($"{p}_04_tiro_impacto", Banco.TiroImpacto(tom), pan));
            fila.Add(($"{p}_05_escudo_ligado", Banco.EscudoLigado(tom), pan));
            fila.Add(($"{p}_06_escudo_bloqueou", Banco.EscudoBloqueou(tom), pan));
            fila.Add(($"{p}_06b_escudo_caiu", Banco.EscudoCaiu(tom), pan));
            fila.Add(($"{p}_07_errou", Banco.AtaqueErrou(tom), pan));
            fila.Add(($"{p}_08_caixa_abriu", Banco.CaixaAbriu(tom), pan));
            fila.Add(($"{p}_09_roleta_tique", Banco.RoletaTique(tom), pan));
            fila.Add(($"{p}_10_checkpoint", Banco.Checkpoint(tom), pan));
            fila.Add(($"{p}_11_volta", Banco.Volta(tom), pan));
            fila.Add(($"{p}_12_superaquecimento", Banco.Superaquecimento(tom), pan));
            fila.Add(($"{p}_13_alarme_calor", Banco.AlarmeCalor(tom), pan));
            fila.Add(($"{p}_14_vitoria", Banco.Vitoria(), pan * 0.8));
            fila.Add(($"{p}_15_derrota", Banco.Derrota(), pan * 0.8));
            foreach (Item item in Roleta.Ordem)
                fila.Add(($"{p}_16_revelacao_{Cfg.NomeItem(item).ToLowerInvariant()}",
                    Banco.Revelacao(item, tom), pan));
        }

        fila.Add(("geral_01_contagem_bipe", Banco.ContagemBipe(), 0));
        fila.Add(("geral_02_contagem_verde", Banco.ContagemVerde(), 0));
        fila.Add(("geral_03_largada", Banco.Largada(), 0));
        fila.Add(("geral_04_ultima_volta", Banco.UltimaVolta(), 0));
        fila.Add(("geral_05_ultrapassagem", Banco.Ultrapassagem(), 0));
        fila.Add(("geral_06_clique", Banco.Clique(), 0));
        fila.Add(("loop_01_ambiente", Banco.Ambiente(), 0));
        fila.Add(("loop_02_pulso_corrida", Banco.PulsoCorrida(), 0));
        fila.Add(("loop_03_tensao_final", Banco.TensaoFinal(), 0));

        // O motor não sai do Banco: ele é sintetizado ao vivo. Para poder
        // ouvi-lo aqui, esta varredura desenha o que ele faria com o esforço
        // subindo de 0 a 1 e voltando — é o gesto do jogador martelando.
        // Uma varredura por voz e por pista: é assim que as duas vozes ficam
        // lado a lado na pasta, para comparar ouvindo em sequência.
        foreach (PerfilMotor perfil in new[] { PerfilMotor.Propulsor, PerfilMotor.Caca })
        {
            string v = perfil == PerfilMotor.Caca ? "caca" : "propulsor";
            fila.Add(($"motor_{v}_ion", MotorSom.Varredura(0, perfil), Banco.Pan(0)));
            fila.Add(($"motor_{v}_ignis", MotorSom.Varredura(1, perfil), Banco.Pan(1)));
        }

        foreach (var (nome, onda, pan) in fila)
            Salvar(Path.Combine(pasta, nome + ".wav"), onda, pan);
        return fila.Count;
    }

    /// <summary>Cabeçalho RIFF de 44 bytes e as amostras em PCM de 16 bits.</summary>
    private static void Salvar(string caminho, Onda o, double pan)
    {
        bool estereo = Math.Abs(pan) > 1e-3;
        int canais = estereo ? 2 : 1;
        int bytesDados = o.N * 2 * canais;
        double ge = estereo ? Math.Sqrt((1.0 - pan) * 0.5) : 1.0;
        double gd = estereo ? Math.Sqrt((1.0 + pan) * 0.5) : 1.0;

        using var f = new BinaryWriter(File.Create(caminho));
        f.Write(new[] { 'R', 'I', 'F', 'F' });
        f.Write(36 + bytesDados);
        f.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        f.Write(16);
        f.Write((short)1);                                  // PCM
        f.Write((short)canais);
        f.Write(Onda.Taxa);
        f.Write(Onda.Taxa * canais * 2);                    // bytes por segundo
        f.Write((short)(canais * 2));                       // alinhamento do bloco
        f.Write((short)16);
        f.Write(new[] { 'd', 'a', 't', 'a' });
        f.Write(bytesDados);

        for (int i = 0; i < o.N; i++)
        {
            if (estereo)
            {
                f.Write(Pcm(o.X[i] * (float)ge));
                f.Write(Pcm(o.X[i] * (float)gd));
            }
            else
            {
                f.Write(Pcm(o.X[i]));
            }
        }
    }

    private static short Pcm(float v) => (short)Math.Clamp(v * 32000f, -32767f, 32767f);
}
