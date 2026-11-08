using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tratoo.API.BackgroundServices;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Features.Pagamentos;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// A trava entre réplicas (<see cref="ExecucaoExclusiva"/>, pg_try_advisory_xact_lock)
    /// contra Postgres REAL: disputa, liberação no fim, na queda forçada da conexão e no
    /// cancelamento, fail-safe com o banco fora do ar, e a transição condicional que
    /// protege cada pagamento por baixo da trava.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class ExecucaoExclusivaTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
        private const long Chave = ExecucaoExclusiva.ChavePagamentoLiberacao;
        private readonly PostgresFixture _pg;

        public ExecucaoExclusivaTests(PostgresFixture pg) => _pg = pg;

        private sealed class LogMemoria : ILogger
        {
            public ConcurrentQueue<(LogLevel Nivel, string Texto)> Linhas { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formatar)
                => Linhas.Enqueue((nivel, formatar(estado, ex)));
        }

        private static ServiceProvider Servicos(string conexao)
        {
            var s = new ServiceCollection();
            s.AddLogging();
            s.AddDbContext<TratooContext>(o => o.UseNpgsql(conexao));
            s.AddScoped<IPagamentoRepository, PagamentoRepository>();
            return s.BuildServiceProvider();
        }

        /// <summary>Rodada que avisa quando entrou e fica parada até ser solta (ou cancelada).</summary>
        private sealed class RodadaPresa
        {
            public TaskCompletionSource Entrou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Soltar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task Executar(IServiceProvider _, CancellationToken ct)
            {
                Entrou.TrySetResult();
                await Soltar.Task.WaitAsync(ct);
            }
        }

        private static Task<bool> TentarAsync(IServiceScopeFactory escopos, Func<IServiceProvider, CancellationToken, Task> rodada,
            ILogger? logger = null, CancellationToken ct = default)
            => ExecucaoExclusiva.TentarExecutarAsync(escopos, Chave, rodada, logger ?? NullLogger.Instance, ct);

        private static Task<bool> TentarVazioAsync(IServiceScopeFactory escopos) => TentarAsync(escopos, (_, _) => Task.CompletedTask);

        /// <summary>PID do backend que segura a advisory lock <see cref="Chave"/> neste banco.</summary>
        private static async Task<int?> PidDonoDaTravaAsync(string conexao)
        {
            await using var c = new NpgsqlConnection(conexao);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND granted " +
                "AND database = (SELECT oid FROM pg_database WHERE datname = current_database()) " +
                "AND ((classid::bigint << 32) | objid::bigint) = @k", c);
            cmd.Parameters.AddWithValue("k", Chave);
            return await cmd.ExecuteScalarAsync() as int?;
        }

        [FactComDocker]
        public async Task Duas_replicas_disputando_so_uma_executa_e_a_outra_pula()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await using var servicos = Servicos(conexao);
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();
            var execucoes = 0;
            var barreira = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task Rodada(IServiceProvider _, CancellationToken __)
            {
                Interlocked.Increment(ref execucoes);
                await barreira.Task.WaitAsync(Limite); // segura a trava até as duas terem tentado
            }

            var a = TentarAsync(escopos, Rodada);
            var b = TentarAsync(escopos, Rodada);
            var primeira = await Task.WhenAny(a, b).WaitAsync(Limite); // a perdedora volta na hora
            Assert.False(await primeira);
            barreira.SetResult();

            var resultados = await Task.WhenAll(a, b).WaitAsync(Limite);
            Assert.Equal(1, resultados.Count(r => r));
            Assert.Equal(1, execucoes);
        }

        [FactComDocker]
        public async Task Trava_e_liberada_no_fim_da_rodada()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await using var servicos = Servicos(conexao);
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();

            Assert.True(await TentarVazioAsync(escopos));
            Assert.Null(await PidDonoDaTravaAsync(conexao));
            Assert.True(await TentarVazioAsync(escopos));
        }

        [FactComDocker]
        public async Task Conexao_da_trava_derrubada_a_forca_libera_a_trava_para_outra_replica()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await using var servicos = Servicos(conexao);
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();
            var presa = new RodadaPresa();
            var log = new LogMemoria();

            var replicaA = TentarAsync(escopos, presa.Executar, log);
            await presa.Entrou.Task.WaitAsync(Limite);
            Assert.False(await TentarVazioAsync(escopos)); // A segura a trava

            // Simula o container de A morrendo / a conexão caindo.
            var pid = await PidDonoDaTravaAsync(conexao);
            Assert.NotNull(pid);
            await using (var admin = new NpgsqlConnection(conexao))
            {
                await admin.OpenAsync();
                await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@p)", admin);
                kill.Parameters.AddWithValue("p", pid!.Value);
                Assert.True((bool)(await kill.ExecuteScalarAsync())!);
            }

            Assert.True(await TentarVazioAsync(escopos)); // o Postgres soltou a trava sozinho

            presa.Soltar.SetResult();
            Assert.True(await replicaA.WaitAsync(Limite)); // A termina sem estourar; só registra
            Assert.Contains(log.Linhas, l => l.Nivel == LogLevel.Warning);
        }

        [FactComDocker]
        public async Task Cancelamento_durante_a_rodada_libera_a_trava()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await using var servicos = Servicos(conexao);
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();
            var presa = new RodadaPresa();
            using var parada = new CancellationTokenSource();

            var replicaA = TentarAsync(escopos, presa.Executar, ct: parada.Token);
            await presa.Entrou.Task.WaitAsync(Limite);
            Assert.False(await TentarVazioAsync(escopos));

            parada.Cancel(); // stoppingToken: o host está desligando
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replicaA.WaitAsync(Limite));

            Assert.Null(await PidDonoDaTravaAsync(conexao));
            Assert.True(await TentarVazioAsync(escopos));
        }

        [FactComDocker]
        public async Task Registra_Information_ao_adquirir_e_Debug_ao_pular()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await using var servicos = Servicos(conexao);
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();
            var presa = new RodadaPresa();
            var logA = new LogMemoria();
            var logB = new LogMemoria();

            var replicaA = TentarAsync(escopos, presa.Executar, logA);
            await presa.Entrou.Task.WaitAsync(Limite);
            Assert.False(await TentarAsync(escopos, (_, _) => Task.CompletedTask, logB));
            presa.Soltar.SetResult();
            await replicaA.WaitAsync(Limite);

            Assert.Contains(logA.Linhas, l => l.Nivel == LogLevel.Information && l.Texto.Contains("adquirida"));
            Assert.Contains(logB.Linhas, l => l.Nivel == LogLevel.Debug && l.Texto.Contains("pulada"));
            Assert.DoesNotContain(logB.Linhas, l => l.Nivel >= LogLevel.Information);
        }

        [FactComDocker]
        public async Task Postgres_inacessivel_a_rodada_nao_executa_sem_trava()
        {
            // Porta sem ninguém escutando: conexão recusada na hora.
            const string inacessivel = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=3";
            await using var servicos = Servicos(inacessivel);
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();
            var executou = false;

            await Assert.ThrowsAnyAsync<Exception>(() =>
                TentarAsync(escopos, (_, _) => { executou = true; return Task.CompletedTask; }));
            Assert.False(executou);

            // No job real: a exceção sobe para o laço (que registra e espera o próximo
            // intervalo); nenhuma varredura nem chamada ao Asaas acontece.
            var job = new PagamentoLiberacaoService(escopos, NullLogger<PagamentoLiberacaoService>.Instance);
            await Assert.ThrowsAnyAsync<Exception>(() => job.ExecutarRodadaAsync(CancellationToken.None));
        }

        [Fact]
        public void Chaves_de_trava_sao_distintas()
        {
            var chaves = typeof(ExecucaoExclusiva)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(long))
                .Select(f => (long)f.GetRawConstantValue()!)
                .ToList();

            Assert.True(chaves.Count >= 3);
            Assert.Equal(chaves.Count, chaves.Distinct().Count());
        }

        [FactComDocker]
        public async Task Transicao_condicional_Retido_para_EmProgresso_tem_um_unico_vencedor()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<bool> ReivindicarAsync()
            {
                await using var db = SeedIntegracao.Contexto(conexao);
                await db.Database.OpenConnectionAsync(); // conexão pronta antes da largada
                await largada.Task;
                return await new PagamentoRepository(db).TryMarcarTransferenciaEmProgressoAsync(cenario.PagamentoId!.Value);
            }

            var disputas = Enumerable.Range(0, 8).Select(_ => Task.Run(ReivindicarAsync)).ToList();
            await Task.Delay(200);
            largada.SetResult();
            var resultados = await Task.WhenAll(disputas).WaitAsync(Limite);

            Assert.Equal(1, resultados.Count(r => r));
            await using var conferir = SeedIntegracao.Contexto(conexao);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso,
                (await conferir.Pagamentos.SingleAsync(p => p.Id == cenario.PagamentoId)).Status);
        }
    }
}
