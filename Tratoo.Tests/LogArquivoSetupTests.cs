using Microsoft.Extensions.Configuration;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// O contrato que importa: sem configuração nenhuma (produção na Railway), o sink de
    /// arquivo continua ligado. Só o compose, explicitamente, desliga.
    /// </summary>
    public class LogArquivoSetupTests
    {
        private static IConfiguration Config(string? valor) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [LogArquivoSetup.Chave] = valor })
                .Build();

        [Fact]
        public void Sem_configuracao_o_arquivo_fica_ligado_como_em_producao()
        {
            var vazia = new ConfigurationBuilder().Build();

            Assert.True(LogArquivoSetup.Habilitado(vazia));
        }

        [Theory]
        [InlineData("false", false)]
        [InlineData("False", false)]
        [InlineData("true", true)]
        public void Valor_explicito_e_respeitado(string valor, bool esperado)
        {
            Assert.Equal(esperado, LogArquivoSetup.Habilitado(Config(valor)));
        }
    }
}
