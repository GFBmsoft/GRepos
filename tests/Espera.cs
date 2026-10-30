using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Espera algo assíncrono acontecer. Esgotado o prazo, o teste falha: passar calado deixava
/// o teste seguir sem o estado que ele devia conferir, e ele continuava verde.
/// </summary>
internal static class Espera
{
    public static async Task Ate(Func<bool> condicao, [CallerArgumentExpression(nameof(condicao))] string oQue = "")
    {
        for (var i = 0; i < 100 && !condicao(); i++) await Task.Delay(50);
        Assert.True(condicao(), $"esperou 5 s por: {oQue}");
    }
}
