using Avalonia.Controls;

namespace GRepos.Views;

/// <summary>
/// Item de menu com o glifo na cor da ação, como as pílulas (trocar verde, excluir
/// vermelho). <paramref name="cor"/> é uma das classes de cor do tema: azul, verde, roxo,
/// laranja, amarelo, vermelho ou cinza.
/// </summary>
public static class ItemDeMenu
{
    public static MenuItem Novo(string texto, string glifo, string cor) => new()
    {
        Header = texto,
        Icon = new TextBlock { Text = glifo, Classes = { "glifo", cor } },
    };
}
