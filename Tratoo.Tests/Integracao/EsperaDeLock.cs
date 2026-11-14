using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tratoo.Domain.Data;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Sincronização determinística para testes de concorrência no Postgres: em vez de "esperar
    /// um pouco", o teste espera até o banco mostrar (pg_stat_activity) que a sessão concorrente
    /// está PARADA esperando um lock. A sessão é identificada pelo Application Name da conexão.
    /// </summary>
    internal static class EsperaDeLock
    {
        public static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Conexão sem pool. Cada teste usa um banco novo e várias sessões (réplicas, sessões
        /// nomeadas): com pool, as conexões ociosas de todos os testes ficariam abertas no mesmo
        /// servidor até esgotar max_connections ("53300: too many clients") no resto da suíte.
        /// </summary>
        public static string SemPool(string conexao)
        {
            // A migration do banco novo usou a string com pool: devolve essa conexão ociosa.
            using (var comPool = new NpgsqlConnection(conexao))
                NpgsqlConnection.ClearPool(comPool);
            return conexao + ";Pooling=false";
        }

        /// <summary>Contexto com conexão própria e um nome de sessão único (Application Name).</summary>
        public static (TratooContext Contexto, string Sessao) ContextoNomeado(string conexao)
        {
            var sessao = "teste_" + Guid.NewGuid().ToString("N")[..12];
            var opcoes = new DbContextOptionsBuilder<TratooContext>()
                .UseNpgsql(conexao + ";Application Name=" + sessao).Options;
            return (new TratooContext(opcoes), sessao);
        }

        /// <summary>Espera até a sessão estar bloqueada esperando lock. Falha se isso nunca acontecer.</summary>
        public static async Task AteFicarBloqueadaAsync(string conexao, string sessao)
        {
            var fim = DateTime.UtcNow + Limite;
            await using var cx = new NpgsqlConnection(conexao);
            await cx.OpenAsync();
            while (true)
            {
                await using var cmd = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_stat_activity WHERE application_name = @s AND wait_event_type = 'Lock'", cx);
                cmd.Parameters.AddWithValue("s", sessao);
                if ((long)(await cmd.ExecuteScalarAsync())! > 0)
                    return;
                if (DateTime.UtcNow > fim)
                    throw new TimeoutException($"A sessão {sessao} nunca ficou esperando lock: as operações não disputaram a mesma linha.");
                await Task.Delay(10); // sondagem de uma condição do banco, não uma suposição de tempo
            }
        }

        /// <summary>
        /// A executa <paramref name="primeiro"/> numa transação ainda aberta; B executa
        /// <paramref name="segundo"/> e fica bloqueada na mesma linha; só então A faz commit.
        /// Devolve os dois resultados — B sempre decide vendo o efeito já confirmado de A.
        /// </summary>
        public static async Task<(TA A, TB B)> SerializarAsync<TA, TB>(
            string conexao, Func<TratooContext, Task<TA>> primeiro, Func<TratooContext, Task<TB>> segundo)
        {
            var (ctxA, _) = ContextoNomeado(conexao);
            var (ctxB, sessaoB) = ContextoNomeado(conexao);
            await using (ctxA)
            await using (ctxB)
            {
                await using var txA = await ctxA.Database.BeginTransactionAsync();
                var a = await primeiro(ctxA);

                var b = Task.Run(() => segundo(ctxB));
                await AteFicarBloqueadaAsync(conexao, sessaoB);
                if (b.IsCompleted)
                    throw new InvalidOperationException("B terminou antes do commit de A: não houve disputa.");

                await txA.CommitAsync();
                return (a, await b.WaitAsync(Limite));
            }
        }
    }
}
