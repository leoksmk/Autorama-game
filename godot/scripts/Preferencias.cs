// Tudo que o jogador escolhe e o jogo lembra, num arquivo só.
//
// Antes havia dois arquivos (controles.cfg e som.cfg) escritos de lugares
// diferentes. Com a tela de configurações isso vira um problema de verdade:
// salvar uma opção não pode arriscar apagar outra. Aqui existe um único
// `Salvar()`, que grava o estado inteiro.
//
// Os dois arquivos antigos ainda são lidos UMA vez, se o novo não existir, para
// quem já tinha o jogo configurado não perder a escolha.

using Godot;
using OrbitalDerby.Audio;
using OrbitalDerby.Entrada;
using OrbitalDerby.Mundo;

namespace OrbitalDerby;

public sealed class Preferencias
{
    private const string Arquivo = "user://orbital.cfg";

    public TipoFonte[] Fontes = { TipoFonte.Teclado, TipoFonte.Teclado };
    public string Circuito = Circuitos.Icaro.Nome;
    public bool Mudo;
    public PerfilMotor Motor = PerfilMotor.Propulsor;
    public Ambiente.Qualidade Qualidade = Ambiente.Qualidade.Alta;
    public CameraRig.Modo Camera = CameraRig.Modo.Transmissao;

    public static Preferencias Carregar()
    {
        var p = new Preferencias();
        var cf = new ConfigFile();
        if (cf.Load(Arquivo) != Error.Ok)
        {
            // Primeira vez com o arquivo novo: aproveita o que os antigos tinham.
            p.Fontes = ConfigControles.Carregar();
            p.Motor = ConfigMotor.Carregar();
            return p;
        }

        for (int i = 0; i < 2; i++)
            if (System.Enum.TryParse(cf.GetValue("jogo", $"p{i + 1}", "Teclado").AsString(), out TipoFonte t))
                p.Fontes[i] = t;
        p.Circuito = cf.GetValue("jogo", "circuito", p.Circuito).AsString();
        p.Mudo = cf.GetValue("jogo", "mudo", false).AsBool();
        if (System.Enum.TryParse(cf.GetValue("jogo", "motor", "Propulsor").AsString(), out PerfilMotor m))
            p.Motor = m;
        if (System.Enum.TryParse(cf.GetValue("jogo", "qualidade", "Alta").AsString(), out Ambiente.Qualidade q))
            p.Qualidade = q;
        if (System.Enum.TryParse(cf.GetValue("jogo", "camera", "Transmissao").AsString(), out CameraRig.Modo c))
            p.Camera = c;
        return p;
    }

    public void Salvar()
    {
        var cf = new ConfigFile();
        for (int i = 0; i < Fontes.Length; i++)
            cf.SetValue("jogo", $"p{i + 1}", Fontes[i].ToString());
        cf.SetValue("jogo", "circuito", Circuito);
        cf.SetValue("jogo", "mudo", Mudo);
        cf.SetValue("jogo", "motor", Motor.ToString());
        cf.SetValue("jogo", "qualidade", Qualidade.ToString());
        cf.SetValue("jogo", "camera", Camera.ToString());
        cf.Save(Arquivo);
    }
}
