using Microsoft.Extensions.Caching.Memory;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Uso único e limite de tentativas dos códigos (MFA/cadastro/reset, OTP de assinatura,
    /// token de dados bancários) sob requisições SIMULTÂNEAS, no estado em memória — sem
    /// Docker. A mesma coisa com Redis real e duas réplicas está em
    /// Integracao/RedisReplicasTests.
    ///
    /// Antes, a validação era ler → comparar → apagar, e a tentativa só era contada depois
    /// do erro: duas requisições com o código certo eram aceitas, e N palpites simultâneos
    /// eram todos comparados (o limite de 5 não valia). Cada teste aqui falha nesse código.
    /// </summary>
    public class OtpConcorrenciaTests
    {
        private const string Email = "pessoa@exemplo.com";

        private static EstadoEfemeroEmMemoria NovaMemoria() => new(new MemoryCache(new MemoryCacheOptions()));

        // ── Primitiva: remover se igual ─────────────────────────────────────────

        [Fact]
        public async Task Memoria_remover_se_igual_so_vence_uma_vez()
        {
            var estado = NovaMemoria();
            await estado.DefinirAsync("mfa:login:x", "hash", TimeSpan.FromMinutes(5));

            var vencedores = await Task.WhenAll(Enumerable.Range(0, 50)
                .Select(_ => Task.Run(() => estado.RemoverSeIgualAsync("mfa:login:x", "hash"))));

            Assert.Equal(1, vencedores.Count(v => v));
            Assert.Null(await estado.ObterAsync<string>("mfa:login:x"));
        }

        [Fact]
        public async Task Memoria_remover_se_igual_nao_apaga_valor_regravado_nem_ausente()
        {
            var estado = NovaMemoria();
            await estado.DefinirAsync("mfa:login:x", "hash-antigo", TimeSpan.FromMinutes(5));
            await estado.DefinirAsync("mfa:login:x", "hash-novo", TimeSpan.FromMinutes(5));

            Assert.False(await estado.RemoverSeIgualAsync("mfa:login:x", "hash-antigo"));
            Assert.Equal("hash-novo", await estado.ObterAsync<string>("mfa:login:x"));
            Assert.False(await estado.RemoverSeIgualAsync("nao:existe", "hash"));
        }

        // ── MFA (cadastro, login, reset de senha) ───────────────────────────────

        [Fact]
        public async Task Codigo_MFA_validado_ao_mesmo_tempo_vale_uma_unica_vez()
        {
            var memoria = NovaMemoria();
            var codigo = await new VerificacaoMFAService(memoria).GerarECriarAsync(Email, "login");

            // Todas leem o código antes de qualquer uma consumi-lo.
            var simultaneas = VerificacaoMFAService.MaxTentativas;
            var encontro = new Encontro(simultaneas);
            var mfa = new VerificacaoMFAService(new EstadoObservado(memoria, "mfa:", encontro.ChegarAsync));

            var resultados = await Task.WhenAll(Enumerable.Range(0, simultaneas)
                .Select(_ => Otp.Resultado(() => mfa.ValidarAsync(Email, codigo, "login"))));

            Assert.Equal(1, resultados.Count(r => r == "OK"));
            Assert.All(resultados.Where(r => r != "OK"), r => Assert.Equal("Código expirado ou inválido", r));
        }

        [Fact]
        public async Task Codigo_MFA_sem_atraso_artificial_nunca_vale_duas_vezes()
        {
            // A corrida "natural", sem ganchos: no código antigo, ~4% das rodadas aceitavam
            // o mesmo código duas vezes (medido: 186 de 5000).
            for (var rodada = 0; rodada < 2000; rodada++)
            {
                var mfa = new VerificacaoMFAService(NovaMemoria());
                var codigo = await mfa.GerarECriarAsync(Email, "login");
                using var largada = new ManualResetEventSlim(false);

                var tarefas = Enumerable.Range(0, 2)
                    .Select(_ => Task.Run(() => { largada.Wait(); return Otp.Resultado(() => mfa.ValidarAsync(Email, codigo, "login")); }))
                    .ToArray();
                largada.Set();

                var resultados = await Task.WhenAll(tarefas);
                Assert.True(resultados.Count(r => r == "OK") == 1, $"rodada {rodada}: {string.Join(" | ", resultados)}");
            }
        }

        [Fact]
        public async Task Palpites_simultaneos_no_MFA_sao_comparados_no_maximo_MaxTentativas_vezes()
        {
            var memoria = NovaMemoria();
            var codigo = await new VerificacaoMFAService(memoria).GerarECriarAsync(Email, "reset_senha");
            var errado = codigo == "000000" ? "111111" : "000000";

            // O atraso depois de cada leitura deixa os 20 palpites "no ar" ao mesmo tempo.
            var observado = new EstadoObservado(memoria, "mfa:", () => Task.Delay(20));
            var mfa = new VerificacaoMFAService(observado);

            var resultados = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => Otp.Resultado(() => mfa.ValidarAsync(Email, errado, "reset_senha")))));

            // Mínimo 0, não 1: os palpites acima do limite invalidam o código, e sob carga isso
            // pode acontecer antes de qualquer um dos MaxTentativas legítimos ler o hash.
            // Nenhuma comparação é um desfecho seguro; o que importa é o teto.
            Assert.InRange(observado.LeiturasComValor, 0, VerificacaoMFAService.MaxTentativas);
            Assert.DoesNotContain("OK", resultados);
            Assert.Contains(resultados, r => r.StartsWith("Muitas tentativas", StringComparison.Ordinal));

            // O código morreu: nem o certo vale mais.
            var depois = await Otp.Resultado(() => mfa.ValidarAsync(Email, codigo, "reset_senha"));
            Assert.Equal("Código expirado ou inválido", depois);
        }

        [Fact]
        public async Task Codigo_certo_ainda_vale_na_ultima_tentativa_permitida()
        {
            var mfa = new VerificacaoMFAService(NovaMemoria());
            var codigo = await mfa.GerarECriarAsync(Email, "cadastro");
            var errado = codigo == "000000" ? "111111" : "000000";

            for (var i = 1; i < VerificacaoMFAService.MaxTentativas; i++)
                Assert.Equal("Código inválido", await Otp.Resultado(() => mfa.ValidarAsync(Email, errado, "cadastro")));

            Assert.Equal("OK", await Otp.Resultado(() => mfa.ValidarAsync(Email, codigo, "cadastro")));
        }

        [Fact]
        public async Task Codigo_regenerado_entre_ler_e_consumir_invalida_o_antigo_e_preserva_o_novo()
        {
            var memoria = NovaMemoria();
            var gerador = new VerificacaoMFAService(memoria);
            var antigo = await gerador.GerarECriarAsync(Email, "login");
            string? novo = null;

            // Logo depois de a validação ler o código antigo, o usuário pede outro.
            var mfa = new VerificacaoMFAService(new EstadoObservado(memoria, "mfa:", async () =>
            {
                if (novo is null)
                    novo = await gerador.GerarECriarAsync(Email, "login");
            }));

            Assert.Equal("Código expirado ou inválido", await Otp.Resultado(() => mfa.ValidarAsync(Email, antigo, "login")));
            Assert.Equal("OK", await Otp.Resultado(() => mfa.ValidarAsync(Email, novo!, "login")));
        }

        [Fact]
        public async Task Codigos_de_finalidades_diferentes_nao_se_misturam()
        {
            var mfa = new VerificacaoMFAService(NovaMemoria());
            var codigoLogin = await mfa.GerarECriarAsync(Email, "login");
            await mfa.GerarECriarAsync(Email, "reset_senha");

            await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync(Email, codigoLogin, "reset_senha"));
            await mfa.ValidarAsync(Email, codigoLogin, "login");
        }

        // ── OTP de assinatura de contrato ───────────────────────────────────────

        [Fact]
        public async Task OTP_de_assinatura_usado_ao_mesmo_tempo_assina_uma_unica_vez()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioContrato();
            await cenario.Servico(memoria).SolicitarOtpAssinaturaAsync(cenario.ContratoId, CenarioContrato.ContratanteId, "203.0.113.10", "testes");

            var simultaneas = 5;
            var encontro = new Encontro(simultaneas);
            var servico = cenario.Servico(new EstadoObservado(memoria, "otp-assinatura:", encontro.ChegarAsync));

            var resultados = await Task.WhenAll(Enumerable.Range(0, simultaneas)
                .Select(_ => Otp.Resultado(() => cenario.AssinarAsync(servico, cenario.UltimoOtp!))));

            Assert.Equal(1, resultados.Count(r => r == "OK"));
            Assert.Equal(1, cenario.Assinaturas);
        }

        [Fact]
        public async Task Palpites_simultaneos_no_OTP_de_assinatura_sao_limitados()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioContrato();
            await cenario.Servico(memoria).SolicitarOtpAssinaturaAsync(cenario.ContratoId, CenarioContrato.ContratanteId, "203.0.113.10", "testes");
            var errado = cenario.UltimoOtp == "000000" ? "111111" : "000000";

            var observado = new EstadoObservado(memoria, "otp-assinatura:", () => Task.Delay(20));
            var servico = cenario.Servico(observado);

            var resultados = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => Otp.Resultado(() => cenario.AssinarAsync(servico, errado)))));

            Assert.InRange(observado.LeiturasComValor, 0, 5); // mínimo 0: ver o teste do MFA
            Assert.DoesNotContain("OK", resultados);
            Assert.Equal("Código expirado ou inválido. Solicite um novo código.",
                await Otp.Resultado(() => cenario.AssinarAsync(servico, cenario.UltimoOtp!)));
            Assert.Equal(0, cenario.Assinaturas);
        }

        [Fact]
        public async Task OTP_de_assinatura_errado_informa_as_tentativas_restantes()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioContrato();
            var servico = cenario.Servico(memoria);
            await servico.SolicitarOtpAssinaturaAsync(cenario.ContratoId, CenarioContrato.ContratanteId, "203.0.113.10", "testes");
            var errado = cenario.UltimoOtp == "000000" ? "111111" : "000000";

            Assert.Equal("Código inválido. 4 tentativa(s) restante(s).", await Otp.Resultado(() => cenario.AssinarAsync(servico, errado)));
            Assert.Equal("OK", await Otp.Resultado(() => cenario.AssinarAsync(servico, cenario.UltimoOtp!)));
        }

        // ── Token e janela de edição dos dados bancários ────────────────────────

        [Fact]
        public async Task Token_bancario_confirmado_ao_mesmo_tempo_vale_uma_unica_vez()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioBancario();
            await cenario.Servico(memoria).SolicitarAlteracaoAsync(CenarioBancario.PrestadorId, "203.0.113.10");

            var simultaneas = 5;
            var encontro = new Encontro(simultaneas);
            var servico = cenario.Servico(new EstadoObservado(memoria, "bancarios:token:", encontro.ChegarAsync));

            var resultados = await Task.WhenAll(Enumerable.Range(0, simultaneas)
                .Select(_ => Otp.Resultado(() => servico.ConfirmarTokenAsync(CenarioBancario.PrestadorId, cenario.UltimoToken!, "203.0.113.10"))));

            Assert.Equal(1, resultados.Count(r => r == "OK"));
        }

        [Fact]
        public async Task Palpites_simultaneos_no_token_bancario_sao_limitados()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioBancario();
            await cenario.Servico(memoria).SolicitarAlteracaoAsync(CenarioBancario.PrestadorId, "203.0.113.10");
            var errado = cenario.UltimoToken == "000000" ? "111111" : "000000";

            var observado = new EstadoObservado(memoria, "bancarios:token:", () => Task.Delay(20));
            var servico = cenario.Servico(observado);

            var resultados = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => Otp.Resultado(() => servico.ConfirmarTokenAsync(CenarioBancario.PrestadorId, errado, "203.0.113.10")))));

            Assert.InRange(observado.LeiturasComValor, 0, 5); // mínimo 0: ver o teste do MFA
            Assert.DoesNotContain("OK", resultados);
            await Assert.ThrowsAsync<NegocioException>(() =>
                servico.ConfirmarTokenAsync(CenarioBancario.PrestadorId, cenario.UltimoToken!, "203.0.113.10"));
        }

        [Fact]
        public async Task Janela_de_edicao_bancaria_permite_uma_unica_gravacao_sob_concorrencia()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioBancario();
            var servico = cenario.Servico(memoria);
            await servico.SolicitarAlteracaoAsync(CenarioBancario.PrestadorId, "203.0.113.10");
            await servico.ConfirmarTokenAsync(CenarioBancario.PrestadorId, cenario.UltimoToken!, "203.0.113.10");

            var resultados = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => Otp.Resultado(() => servico.AtualizarAsync(CenarioBancario.PrestadorId, CenarioBancario.DadosValidos(), "203.0.113.10")))));

            Assert.Equal(1, resultados.Count(r => r == "OK"));
            Assert.Equal(1, cenario.Gravacoes);
        }

        [Fact]
        public async Task Dados_bancarios_invalidos_nao_gastam_a_confirmacao()
        {
            var memoria = NovaMemoria();
            var cenario = new CenarioBancario();
            var servico = cenario.Servico(memoria);
            await servico.SolicitarAlteracaoAsync(CenarioBancario.PrestadorId, "203.0.113.10");
            await servico.ConfirmarTokenAsync(CenarioBancario.PrestadorId, cenario.UltimoToken!, "203.0.113.10");

            var invalido = CenarioBancario.DadosValidos();
            invalido.Banco = "";
            Assert.Equal("Informe o banco.", await Otp.Resultado(() => servico.AtualizarAsync(CenarioBancario.PrestadorId, invalido, "203.0.113.10")));

            Assert.Equal("OK", await Otp.Resultado(() => servico.AtualizarAsync(CenarioBancario.PrestadorId, CenarioBancario.DadosValidos(), "203.0.113.10")));
            Assert.StartsWith("Confirme o código", await Otp.Resultado(() => servico.AtualizarAsync(CenarioBancario.PrestadorId, CenarioBancario.DadosValidos(), "203.0.113.10")));
            Assert.Equal(1, cenario.Gravacoes);
        }
    }
}
