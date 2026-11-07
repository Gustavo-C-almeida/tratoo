using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tratoo.Domain.Features.Auth;
using Xunit;

namespace Tratoo.Tests.Sessao
{
    /// <summary>
    /// Troca da chave do JWT: a atual assina (com kid), atual e anterior validam. Os tokens
    /// são conferidos com os TokenValidationParameters REAIS da API (JwtBearer configurado
    /// por AddTratooAutenticacao).
    /// </summary>
    public class ChavesJwtTests
    {
        private const string K1 = "chave-jwt-numero-um-dos-testes-0123456789";
        private const string K2 = "chave-jwt-numero-dois-dos-testes-0123456789";
        private const string K3 = "chave-jwt-numero-tres-dos-testes-0123456789";

        // ── Validação da configuração ────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("DEFINA_UMA_CHAVE_SECRETA_FORTE_COM_NO_MINIMO_32_CARACTERES")]
        [InlineData("curta-demais-0123456789-abcdef")]   // 30 bytes
        public void Chave_atual_ausente_placeholder_ou_curta_e_recusada(string? atual)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ChavesJwt.Preparar(atual, null));
            Assert.Contains(ChavesJwt.VariavelDeAmbiente, ex.Message);
            if (!string.IsNullOrEmpty(atual)) Assert.DoesNotContain(atual, ex.Message);
        }

        [Fact]
        public void Anterior_curta_e_recusada() =>
            Assert.Contains(ChavesJwt.VariavelDeAmbienteAnterior,
                Assert.Throws<InvalidOperationException>(() => ChavesJwt.Preparar(K1, "curta")).Message);

        [Fact]
        public void Anterior_igual_a_atual_e_recusada() =>
            Assert.Contains(ChavesJwt.VariavelDeAmbienteAnterior,
                Assert.Throws<InvalidOperationException>(() => ChavesJwt.Preparar(K1, K1)).Message);

        [Fact]
        public void Sem_anterior_so_a_atual_valida()
        {
            var c = ChavesJwt.Preparar(K1, null);
            Assert.Null(c.Anterior);
            Assert.Single(c.Validacao);
        }

        [Fact]
        public void Kid_e_estavel_distinto_por_chave_e_nao_revela_a_chave()
        {
            var a = ChavesJwt.Preparar(K1, null).Atual.KeyId;
            Assert.Equal(a, ChavesJwt.Preparar(K1, null).Atual.KeyId);
            Assert.NotEqual(a, ChavesJwt.Preparar(K2, null).Atual.KeyId);
            Assert.Matches("^[0-9a-f]{16}$", a);
            Assert.DoesNotContain("chave", a);
        }

        // ── Assinatura e validação com a configuração real ───────────────────

        private static IConfiguration Config(string atual, string? anterior = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ChavesJwt.ChaveConfiguracao] = atual,
                [ChavesJwt.ChaveAnteriorConfiguracao] = anterior,
                ["Jwt:Issuer"] = "Tratoo",
                ["Jwt:Audience"] = "TratooUsers"
            }).Build();

        private static string Emitir(string chave) =>
            new JwtService(Config(chave)).Gerar(7, "a@b.com", "A", "Contratante", true, false, "sid-1", TimeSpan.FromMinutes(15));

        /// <summary>Token como os emitidos ANTES desta versão: mesma chave, sem kid.</summary>
        private static string EmitirSemKid(string chave)
        {
            var credenciais = new SigningCredentials(
                new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256);
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                "Tratoo", "TratooUsers", new[] { new System.Security.Claims.Claim("sub", "7") },
                expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: credenciais));
        }

        private static async Task<bool> ValidaAsync(string token, string atual, string? anterior = null)
        {
            var servicos = new ServiceCollection();
            servicos.AddLogging();
            servicos.AddTratooAutenticacaoParaTestes(Config(atual, anterior));
            await using var sp = servicos.BuildServiceProvider();
            var parametros = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
            var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, parametros);
            return resultado.IsValid;
        }

        [Fact]
        public void Token_emitido_leva_o_kid_da_chave_atual()
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(Emitir(K1));
            Assert.Equal(ChavesJwt.Preparar(K1, null).Atual.KeyId, token.Header.Kid);
        }

        [Fact]
        public async Task Sem_troca_valida_so_a_chave_configurada() 
        {
            Assert.True(await ValidaAsync(Emitir(K1), K1));
            Assert.False(await ValidaAsync(Emitir(K2), K1));
        }

        [Fact]
        public async Task Durante_a_troca_tokens_da_nova_e_da_anterior_valem_e_de_outra_chave_nao()
        {
            Assert.True(await ValidaAsync(Emitir(K2), atual: K2, anterior: K1));   // novo
            Assert.True(await ValidaAsync(Emitir(K1), atual: K2, anterior: K1));   // emitido antes da troca
            Assert.False(await ValidaAsync(Emitir(K3), atual: K2, anterior: K1));  // desconhecida
        }

        [Fact]
        public async Task Depois_de_remover_a_anterior_os_tokens_dela_deixam_de_valer() =>
            Assert.False(await ValidaAsync(Emitir(K1), atual: K2));

        [Fact]
        public async Task Tokens_sem_kid_emitidos_antes_desta_versao_continuam_validos()
        {
            Assert.True(await ValidaAsync(EmitirSemKid(K1), K1));
            Assert.True(await ValidaAsync(EmitirSemKid(K1), atual: K2, anterior: K1));
            Assert.False(await ValidaAsync(EmitirSemKid(K3), atual: K2, anterior: K1));
        }

        [Fact]
        public async Task Kid_forjado_nao_faz_chave_desconhecida_passar()
        {
            // Assina com K3 mas declara o kid da chave atual: a assinatura não confere.
            var chave = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(K3))
            { KeyId = ChavesJwt.Preparar(K2, null).Atual.KeyId };
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                "Tratoo", "TratooUsers", new[] { new System.Security.Claims.Claim("sub", "7") },
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256)));

            Assert.False(await ValidaAsync(token, atual: K2, anterior: K1));
        }
    }

    internal static class AutenticacaoParaTestes
    {
        /// <summary>Só o que o JwtBearer precisa para montar as opções (sem Redis, sem sessões).</summary>
        public static IServiceCollection AddTratooAutenticacaoParaTestes(this IServiceCollection s, IConfiguration c) =>
            Tratoo.API.Infrastructure.SessaoAutenticacaoSetup.AddTratooAutenticacao(s, c);
    }
}
