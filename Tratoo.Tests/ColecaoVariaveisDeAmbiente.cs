using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Testes que alteram variáveis de ambiente do processo. Variável de ambiente é
    /// estado global: todo WebApplication.CreateBuilder a lê. Com
    /// <c>DisableParallelization</c>, o xUnit roda esta coleção sozinha, depois das
    /// coleções paralelas — nenhum outro teste enxerga os valores temporários.
    /// </summary>
    [CollectionDefinition(Nome, DisableParallelization = true)]
    public sealed class ColecaoVariaveisDeAmbiente
    {
        public const string Nome = "Variáveis de ambiente do processo";
    }
}
