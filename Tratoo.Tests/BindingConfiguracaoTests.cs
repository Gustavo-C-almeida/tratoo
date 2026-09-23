using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Cobre o caminho que produção realmente usa: AddTratooForwardedHeaders lendo de
    /// IConfiguration. Sem isto, um erro no nome da seção ou no binding de array
    /// passaria despercebido — os testes de pipeline sozinhos não detectam.
    ///
    /// Vale lembrar que no Railway NÃO existe appsettings.json dentro da imagem
    /// (.dockerignore), então a fonte real é variável de ambiente no formato
    /// ForwardedHeaders__Chave — o que, no .NET, chega ao binder como ForwardedHeaders:Chave.
    /// </summary>
    public class BindingConfiguracaoTests
    {
        private static (ForwardedHeadersOptions Opcoes, RedeConfiavel Rede, ForwardedHeadersSettings Settings)
            Resolver(params (string Chave, string Valor)[] pares)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(pares.ToDictionary(p => p.Chave, p => (string?)p.Valor))
                .Build();

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddTratooForwardedHeaders(configuration);

            var provider = services.BuildServiceProvider();

            return (
                provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value,
                provider.GetRequiredService<RedeConfiavel>(),
                provider.GetRequiredService<ForwardedHeadersSettings>());
        }

        [Fact]
        public void SemConfiguracaoNenhuma_UsaOsPadroesDoProjeto()
        {
            var (opcoes, rede, settings) = Resolver();

            Assert.Equal(2, settings.ForwardLimit);
            Assert.Equal(2, opcoes.ForwardLimit);
            Assert.True(settings.Habilitado);

            // Só XForwardedFor e XForwardedProto — Host deliberadamente de fora.
            Assert.Equal(
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
                opcoes.ForwardedHeaders);

            // Gate ativo com as faixas padrão; listas do framework vazias de propósito.
            Assert.False(rede.Vazio);
            Assert.Contains("100.64.0.0/10", rede.FaixasConfiguradas);
            Assert.Empty(opcoes.KnownProxies);
            Assert.Empty(opcoes.KnownNetworks);
        }

        [Fact]
        public void ForwardLimitVindoDeConfiguracao_SobrescreveOPadrao()
        {
            var (opcoes, _, settings) = Resolver(("ForwardedHeaders:ForwardLimit", "3"));

            Assert.Equal(3, settings.ForwardLimit);
            Assert.Equal(3, opcoes.ForwardLimit);
        }

        [Fact]
        public void HabilitadoFalse_DesligaOProcessamento()
        {
            var (opcoes, _, _) = Resolver(("ForwardedHeaders:Habilitado", "false"));

            Assert.Equal(
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None,
                opcoes.ForwardedHeaders);
        }

        [Fact]
        public void PeersConfiaveisVindoDeConfiguracao_SubstituiOPadrao()
        {
            var (_, rede, _) = Resolver(
                ("ForwardedHeaders:PeersConfiaveis:0", "10.1.2.0/24"),
                ("ForwardedHeaders:PeersConfiaveis:1", "192.0.2.7"));

            Assert.Equal(new[] { "10.1.2.0/24", "192.0.2.7" }, rede.FaixasConfiguradas);
            Assert.True(rede.Contem(System.Net.IPAddress.Parse("10.1.2.99")));
            Assert.True(rede.Contem(System.Net.IPAddress.Parse("192.0.2.7")));   // IP solto = /32
            Assert.False(rede.Contem(System.Net.IPAddress.Parse("100.64.0.1"))); // padrão saiu
        }

        [Fact]
        public void KnownNetworksVindoDeConfiguracao_ChegaAoMiddleware()
        {
            var (opcoes, _, _) = Resolver(
                ("ForwardedHeaders:KnownNetworks:0", "100.64.0.0/10"),
                ("ForwardedHeaders:KnownProxies:0", "198.51.100.200"));

            Assert.Single(opcoes.KnownNetworks);
            Assert.Single(opcoes.KnownProxies);
            Assert.Equal("198.51.100.200", opcoes.KnownProxies[0].ToString());
        }

        /// <summary>CIDR inválido não pode derrubar a aplicação — é ignorado e reportado.</summary>
        [Fact]
        public void CidrInvalido_EIgnoradoEReportado()
        {
            var (_, rede, _) = Resolver(
                ("ForwardedHeaders:PeersConfiaveis:0", "100.64.0.0/10"),
                ("ForwardedHeaders:PeersConfiaveis:1", "nao-e-um-cidr"),
                ("ForwardedHeaders:PeersConfiaveis:2", "10.0.0.0/999"));

            Assert.Equal(new[] { "100.64.0.0/10" }, rede.FaixasConfiguradas);
            Assert.Equal(new[] { "nao-e-um-cidr", "10.0.0.0/999" }, rede.FaixasInvalidas);
            Assert.False(rede.Vazio);
        }

        /// <summary>Entrada em branco (jeito de desligar via env var) zera o gate.</summary>
        [Fact]
        public void PeersConfiaveisEmBranco_DeixaOGateVazio()
        {
            var (_, rede, _) = Resolver(("ForwardedHeaders:PeersConfiaveis:0", ""));

            Assert.True(rede.Vazio);
        }

        /// <summary>
        /// Formato que o Railway realmente injeta: variável de ambiente com duplo
        /// underscore. Confirma que a seção e os nomes das chaves batem de verdade.
        /// </summary>
        [Fact]
        public void VariavelDeAmbienteComDuploUnderscore_EhLidaCorretamente()
        {
            const string VarLimit = "ForwardedHeaders__ForwardLimit";
            const string VarPeer  = "ForwardedHeaders__PeersConfiaveis__0";

            try
            {
                Environment.SetEnvironmentVariable(VarLimit, "4");
                Environment.SetEnvironmentVariable(VarPeer, "203.0.113.0/24");

                var configuration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

                var services = new ServiceCollection();
                services.AddOptions();
                services.AddTratooForwardedHeaders(configuration);
                var provider = services.BuildServiceProvider();

                var opcoes = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
                var rede = provider.GetRequiredService<RedeConfiavel>();

                Assert.Equal(4, opcoes.ForwardLimit);
                Assert.Equal(new[] { "203.0.113.0/24" }, rede.FaixasConfiguradas);
            }
            finally
            {
                Environment.SetEnvironmentVariable(VarLimit, null);
                Environment.SetEnvironmentVariable(VarPeer, null);
            }
        }
    }
}
