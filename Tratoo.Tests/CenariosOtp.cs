using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Contratos;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Features.Pagamentos;
using Tratoo.Domain.Features.Perfis;
using Tratoo.Domain.Features.Projetos;
using Tratoo.Domain.Features.Propostas;
using Tratoo.Domain.Features.Storage;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Prestador;
using Tratoo.Tests.Integracao;

namespace Tratoo.Tests
{
    /// <summary>
    /// Peças compartilhadas pelos testes de uso único/concorrência dos códigos — em memória
    /// (OtpConcorrenciaTests) e com Redis real entre réplicas (RedisReplicasTests).
    /// </summary>
    internal static class Otp
    {
        /// <summary>Roda a validação e devolve "OK" ou a mensagem da NegocioException.</summary>
        public static async Task<string> Resultado(Func<Task> validacao)
        {
            try { await validacao(); return "OK"; }
            catch (NegocioException e) { return e.Message; }
        }
    }

    /// <summary>
    /// Ponto de encontro assíncrono: cada chamada espera até as N chegarem. Usado para
    /// segurar N validações logo depois de lerem o código, abrindo de propósito a janela
    /// entre "ler" e "consumir" — sem ele, a corrida dependeria da sorte do escalonador.
    /// </summary>
    internal sealed class Encontro
    {
        private readonly int _participantes;
        private int _chegadas;
        private readonly TaskCompletionSource _todos = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Encontro(int participantes) => _participantes = participantes;

        public Task ChegarAsync()
        {
            if (Interlocked.Increment(ref _chegadas) == _participantes)
                _todos.TrySetResult();
            return _todos.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// Decorador de <see cref="IEstadoEfemero"/>: conta as leituras que acharam o código
    /// (chaves que começam com <c>prefixo</c>) — cada uma vira uma comparação — e roda um
    /// gancho logo depois de cada uma (encontro, atraso, regerar o código...).
    /// </summary>
    internal sealed class EstadoObservado : IEstadoEfemero
    {
        private readonly IEstadoEfemero _interno;
        private readonly string _prefixo;
        private readonly Func<Task>? _depoisDeLer;
        private int _leituras;

        public EstadoObservado(IEstadoEfemero interno, string prefixo, Func<Task>? depoisDeLer = null)
        {
            _interno = interno;
            _prefixo = prefixo;
            _depoisDeLer = depoisDeLer;
        }

        /// <summary>Quantas vezes o código foi lido com valor (= comparado).</summary>
        public int LeiturasComValor => Volatile.Read(ref _leituras);

        public async Task<T?> ObterAsync<T>(string chave)
        {
            var valor = await _interno.ObterAsync<T>(chave);
            if (valor is not null && chave.StartsWith(_prefixo, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _leituras);
                if (_depoisDeLer is not null)
                    await _depoisDeLer();
            }
            return valor;
        }

        public Task DefinirAsync<T>(string chave, T valor, TimeSpan ttl) => _interno.DefinirAsync(chave, valor, ttl);
        public Task<bool> DefinirSeAusenteAsync<T>(string chave, T valor, TimeSpan ttl) => _interno.DefinirSeAusenteAsync(chave, valor, ttl);
        public Task RemoverAsync(params string[] chaves) => _interno.RemoverAsync(chaves);
        public Task<bool> RemoverSeIgualAsync<T>(string chave, T esperado) => _interno.RemoverSeIgualAsync(chave, esperado);
        public Task<long> IncrementarAsync(string chave, TimeSpan ttl) => _interno.IncrementarAsync(chave, ttl);
    }

    /// <summary>
    /// Um contrato "Gerado" e as dependências do ContratoServicoService. O mesmo cenário
    /// serve várias réplicas: cada <see cref="Servico"/> recebe o estado da sua réplica.
    /// </summary>
    internal sealed class CenarioContrato
    {
        public const int ContratanteId = 10;
        public const int PrestadorId = 20;

        public Guid ContratoId { get; } = Guid.NewGuid();
        public string? UltimoOtp { get; private set; }
        public ConcurrentQueue<AcaoHistoricoAssinatura> Historico { get; } = new();

        public ContratoServicoService Servico(IEstadoEfemero estado) => new(
            Falso<IContratoServicoRepository>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                // Instância nova por chamada, como uma consulta ao banco por requisição.
                ["GetByIdAsync"] = _ => Task.FromResult<ContratoServico?>(new ContratoServico
                {
                    Id = ContratoId,
                    ContratanteId = ContratanteId,
                    PrestadorId = PrestadorId,
                    Status = ContratoServicoStatus.Gerado,
                    ExpiraEm = DateTime.UtcNow.AddDays(1)
                }),
                ["AddHistoricoAsync"] = a =>
                {
                    Historico.Enqueue(((HistoricoAssinatura)a[0]!).Acao);
                    return Task.CompletedTask;
                }
            }),
            Falso<IIdentidadeRepository>.Criar(),
            Falso<IContratanteRepository>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                ["GetCompletoAsync"] = _ => Task.FromResult<Contratante?>(
                    new Contratante { Id = ContratanteId, Nome = "Contratante", Email = "contratante@exemplo.com" })
            }),
            Falso<IPrestadorRepository>.Criar(),
            Falso<IEmailService>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                ["EnviarOtpAssinaturaAsync"] = a =>
                {
                    UltimoOtp = (string)a[3]!;
                    return Task.CompletedTask;
                }
            }),
            Falso<IContratosPdfService>.Criar(),
            Falso<IR2PrivateStorageService>.Criar(),
            Falso<IProjetoRepository>.Criar(),
            Falso<IPropostaProjetoRepository>.Criar(),
            Falso<IPagamentoService>.Criar(),
            Falso<IPagamentoRepository>.Criar(),
            estado,
            NullLogger<ContratoServicoService>.Instance);

        public Task AssinarAsync(ContratoServicoService servico, string otp) =>
            servico.AssinarAsync(ContratoId, ContratanteId, "203.0.113.10", "testes", otp);

        public int Assinaturas => Historico.Count(a => a == AcaoHistoricoAssinatura.Assinado);
    }

    /// <summary>Um prestador e as dependências do DadosBancariosService.</summary>
    internal sealed class CenarioBancario
    {
        public const int PrestadorId = 30;

        private int _gravacoes;

        public string? UltimoToken { get; private set; }

        /// <summary>Quantas vezes os dados bancários chegaram a ser gravados.</summary>
        public int Gravacoes => Volatile.Read(ref _gravacoes);

        public DadosBancariosService Servico(IEstadoEfemero estado) => new(
            Falso<IContaBancariaRepository>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                // Um "banco" com latência: abre a janela entre autorizar e consumir a autorização.
                ["SaveChangesAsync"] = _ => GravarAsync()
            }),
            Falso<IPrestadorRepository>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                ["GetByIdAsync"] = _ => Task.FromResult<Prestador?>(
                    new Prestador { Id = PrestadorId, Nome = "Prestadora", Email = "prestadora@exemplo.com" })
            }),
            Falso<IEmailService>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                ["EnviarTokenDadosBancariosAsync"] = a =>
                {
                    UltimoToken = (string)a[2]!;
                    return Task.CompletedTask;
                }
            }),
            Falso<IAuditLogRepository>.Criar(),
            estado,
            NullLogger<DadosBancariosService>.Instance);

        public static AtualizarDadosBancariosDTO DadosValidos() => new()
        {
            Banco = "Banco de Testes",
            Agencia = "0001",
            Conta = "123456",
            TipoPix = TipoPix.Email,
            ChavePix = "prestadora@exemplo.com"
        };

        private async Task GravarAsync()
        {
            Interlocked.Increment(ref _gravacoes);
            await Task.Delay(50);
        }
    }
}
