// Os números que definem a voz de um motor.
//
// A máquina de síntese é uma só (VozMotor); o que muda de um motor para o
// outro são estes parâmetros. Separá-los serve para duas coisas: comparar
// vozes lado a lado sem recompilar nada, e poder inventar uma terceira sem
// mexer em uma linha de DSP.
//
// PROPULSOR é a voz de origem do Orbital Derby: grave, fluida, pensada para
// aguentar corrida longa sem cansar o ouvido.
//
// CAÇA é a voz de filme de nave: formantes fortes e móveis, uivo no lugar de
// respiração e um toque de inarmonicidade metálica. Ela imita a TÉCNICA do som
// analógico de caça espacial — ressonância vocal em cima de um tom que oscila
// —, não os sons registrados de nenhuma franquia.
//
// As duas foram REBAIXADAS depois de soarem agudas demais em teste de ouvido.
// O que fazia isso não era a afinação: era o centro de gravidade do espectro.
// Baixar a fundamental sozinho quase não resolve — quem manda são as
// frequências dos formantes, o corte do passa-baixa e o sopro de ar, que é
// energia de banda larga. Foram os três que desceram.
//
// A diferença central entre as duas é onde mora o caráter:
//
//   PROPULSOR  no corpo. Parciais graves densas, rosnado forte, sopro de ar.
//   CAÇA       na garganta. Formantes altos e estreitos, tom médio, uivo.

using Godot;

namespace OrbitalDerby.Audio;

public enum PerfilMotor { Propulsor, Caca }

/// <summary>
/// Lembra qual voz ficou escolhida. Arquivo próprio, separado do
/// controles.cfg: som e entrada não têm motivo para compartilhar destino, e
/// um arquivo corrompido não pode levar o outro junto.
/// </summary>
public static class ConfigMotor
{
    private const string Arquivo = "user://som.cfg";

    public static PerfilMotor Carregar()
    {
        var cf = new ConfigFile();
        if (cf.Load(Arquivo) != Error.Ok)
            return PerfilMotor.Propulsor;
        return System.Enum.TryParse(cf.GetValue("som", "motor", "Propulsor").AsString(), out PerfilMotor p)
            ? p
            : PerfilMotor.Propulsor;
    }

    public static void Salvar(PerfilMotor p)
    {
        var cf = new ConfigFile();
        cf.SetValue("som", "motor", p.ToString());
        cf.Save(Arquivo);
    }
}

public sealed class ReceitaMotor
{
    public string Nome = "";

    // Afinação: f = FreqBase + FreqGanho * esforço, em Hz.
    public double FreqBase, FreqGanho;

    // Expoente do decaimento das parciais: p = PBase - PGanho * esforço.
    // Menor = mais brilhante. Mexer nele, e não no volume, é o que deixa o
    // motor abrir sem gritar.
    public double PBase, PGanho;

    // Rosnado: peso das parciais ímpares (as que caem no meio da série).
    public double GrowlBase, GrowlGanho;

    // Formantes: três ressonâncias em frequências FIXAS, que não seguem a
    // afinação. São elas que dão caráter — quanto mais fortes e estreitas,
    // mais o motor soa como uma garganta e menos como um zumbido.
    public double[] FmtHz = { 290, 760, 1750 };
    public double[] FmtQ = { 2.2, 2.0, 1.8 };
    public double[] FmtGanho = { 0.50, 0.36, 0.13 };
    public double[] FmtAbre = { 0.18, 0.30, 0.35 };   // quanto sobem com o esforço
    public double FmtGanhoBurn = 0.13;                // reforço do agudo na pós-combustão

    // Balanço dos formantes: um LFO lento que move as três frequências ao
    // mesmo tempo. É o detalhe que transforma "ressonância" em "garganta" —
    // sem ele o formante é um filtro parado, com ele a máquina parece estar
    // modulando o próprio grito. Zero no propulsor.
    public double FmtBalancoHz, FmtBalancoProf;

    // Movimento da afinação. RESPIRAÇÃO é lenta (fluida); UIVO é rápido e
    // profundo (dramático). Um motor usa um ou outro, não os dois.
    public double RespiraHz = 0.27, RespiraProf = 0.0035;
    public double UivoHz, UivoProf;

    // Inarmonicidade por modulação: um segundo oscilador numa razão não
    // inteira some ao corpo. Em dose pequena dá liga metálica; em dose grande
    // vira sino desafinado, então é o parâmetro mais perigoso daqui.
    public double FmRazao = 2.41, FmIndice;

    // Casco: atraso curto realimentado, em milissegundos. Mais curto = corpo
    // menor e mais agudo.
    public double CascoMs = 3.1, CascoRealim = 0.40, CascoMix = 0.30;

    // Sopro de ar: volume e corte do ruído.
    public double ArBase = 0.09, ArGanho = 0.22;
    public double ArCorteBase = 1100, ArCorteGanho = 3200;
    public double ArBurn = 0.7;

    // Passa-baixa final (nunca ressonante) e saturação.
    public double CorteBase = 1800, CorteGanho = 4500, CorteBurn = 1100;
    public double DriveBase = 1.1, DriveGanho = 0.5, DriveBurn = 0.9;

    // Mistura entre o som seco e os formantes.
    public double Seco = 0.60, Corpo = 0.68;

    // Volume final da voz.
    public double Volume = 0.52;

    public static ReceitaMotor De(PerfilMotor p) => p == PerfilMotor.Caca ? Caca() : Propulsor();

    public static string NomeDe(PerfilMotor p) => p == PerfilMotor.Caca ? "caça" : "propulsor";

    public static PerfilMotor Proximo(PerfilMotor p) =>
        p == PerfilMotor.Propulsor ? PerfilMotor.Caca : PerfilMotor.Propulsor;

    /// <summary>
    /// A voz de origem: grave e fluida. Os números são resultado de afinar
    /// contra o ouvido e contra o espectro. No talo o centro de gravidade fica
    /// em 647 Hz e 10% da energia acima de 2 kHz — contra 1069 Hz e 21% de uma
    /// versão que ainda soava aguda, e 44% de uma que soava estridente.
    /// Não mexer sem medir de novo.
    /// </summary>
    public static ReceitaMotor Propulsor() => new()
    {
        Nome = "propulsor",
        FreqBase = 34.0, FreqGanho = 112.0,
        PBase = 1.70, PGanho = 0.52,
        GrowlBase = 0.30, GrowlGanho = 0.44,
        FmtHz = new[] { 260.0, 680.0, 1450.0 },
        FmtQ = new[] { 2.2, 2.0, 1.8 },
        FmtGanho = new[] { 0.56, 0.36, 0.11 },
        FmtAbre = new[] { 0.14, 0.22, 0.22 },
        FmtGanhoBurn = 0.13,
        RespiraHz = 0.27, RespiraProf = 0.0035,
        UivoHz = 0.0, UivoProf = 0.0,
        FmtBalancoHz = 0.0, FmtBalancoProf = 0.0,
        FmIndice = 0.0,
        CascoMs = 3.1, CascoRealim = 0.40, CascoMix = 0.30,
        ArBase = 0.08, ArGanho = 0.19,
        ArCorteBase = 920, ArCorteGanho = 2500, ArBurn = 0.6,
        CorteBase = 1500, CorteGanho = 3300, CorteBurn = 800,
        DriveBase = 1.1, DriveGanho = 0.5, DriveBurn = 0.9,
        Seco = 0.60, Corpo = 0.68,
        Volume = 0.52,
    };

    /// <summary>
    /// A voz de caça. Quatro mudanças, todas na mesma direção:
    ///
    ///   1. TOM MAIS ALTO, mas só um pouco. A distância entre as duas vozes
    ///      está mais no timbre do que na afinação.
    ///   2. FORMANTES FORTES, ALTOS E ESTREITOS. É o coração do timbre: os
    ///      harmônicos atravessam os picos enquanto a nave acelera e o som
    ///      soa quase VOCAL. Era isso que se obtinha, na era analógica,
    ///      processando o berro de um bicho.
    ///   3. UIVO E BALANÇO. Um vibrato de 5,2 Hz na afinação, mais um passeio
    ///      de 1,7 Hz nas frequências dos formantes. O segundo é o que mais
    ///      conta: formante parado é filtro, formante que anda é garganta.
    ///   4. INARMONICIDADE. Um modulador em razão 2,41 acrescenta parciais que
    ///      não pertencem à série: o brilho metálico e "errado" de uma
    ///      máquina que não é motor a combustão.
    ///
    /// Em compensação o rosnado grave cai — o caça é tenso, não encorpado. No
    /// talo fica em 1026 Hz de centroide e 17% acima de 2 kHz, contra 647 Hz e
    /// 10% do propulsor: mais brilhante de propósito, e ainda assim mais escuro
    /// do que o próprio propulsor já foi.
    /// </summary>
    public static ReceitaMotor Caca() => new()
    {
        Nome = "caça",
        FreqBase = 46.0, FreqGanho = 155.0,
        PBase = 1.52, PGanho = 0.50,
        GrowlBase = 0.20, GrowlGanho = 0.30,
        FmtHz = new[] { 335.0, 880.0, 1780.0 },
        FmtQ = new[] { 3.4, 3.0, 2.4 },
        FmtGanho = new[] { 0.68, 0.46, 0.15 },
        FmtAbre = new[] { 0.16, 0.24, 0.24 },
        FmtGanhoBurn = 0.10,
        RespiraHz = 0.31, RespiraProf = 0.0016,
        UivoHz = 5.2, UivoProf = 0.022,
        FmtBalancoHz = 1.7, FmtBalancoProf = 0.10,
        FmRazao = 2.41, FmIndice = 0.15,
        CascoMs = 2.6, CascoRealim = 0.50, CascoMix = 0.32,
        ArBase = 0.06, ArGanho = 0.16,
        ArCorteBase = 1120, ArCorteGanho = 2800, ArBurn = 0.7,
        CorteBase = 1560, CorteGanho = 3400, CorteBurn = 820,
        DriveBase = 1.2, DriveGanho = 0.6, DriveBurn = 0.9,
        Seco = 0.52, Corpo = 0.62,
        Volume = 0.46,
    };
}
