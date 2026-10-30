using System;

namespace GRepos.Services;

/// <summary>
/// O tamanho com que a janela principal abre: o que o usuário deixou da última vez,
/// ajustado ao monitor de agora. Lógica pura — a janela só aplica o resultado.
/// </summary>
public static class JanelaGuardada
{
    /// <summary>Folga em relação à área útil do monitor, para a moldura não sair da tela.</summary>
    private const double Folga = 40;

    /// <param name="largura">Guardada da última vez; zero ou menos é "nunca guardou".</param>
    /// <param name="areaLargura">Área útil do monitor (sem a barra de tarefas), em pontos da tela; zero se desconhecida.</param>
    public static (double Largura, double Altura) Tamanho(
        double largura, double altura,
        double padraoLargura, double padraoAltura,
        double minimaLargura, double minimaAltura,
        double areaLargura = 0, double areaAltura = 0)
    {
        var l = largura > 0 ? largura : padraoLargura;
        var a = altura > 0 ? altura : padraoAltura;

        // monitor menor que o da última vez (notebook fora da base): cabe no de agora
        if (areaLargura > 0) l = Math.Min(l, areaLargura - Folga);
        if (areaAltura > 0) a = Math.Min(a, areaAltura - Folga);

        return (Math.Max(l, minimaLargura), Math.Max(a, minimaAltura));
    }

    /// <summary>
    /// Largura de uma coluna fixa (a sidebar, a lista de commits) no espaço de agora: a escolhida
    /// pelo usuário, enquanto couber. Parte da escolha, não da largura atual: a coluna que
    /// encolheu só porque a janela estreitou volta ao que era quando a janela alarga.
    /// </summary>
    /// <param name="cabe">O que sobra depois do divisor e do mínimo da coluna vizinha.</param>
    public static double ColunaQueCede(double escolhida, double minima, double cabe) =>
        Math.Max(minima, Math.Min(escolhida, cabe));
}
