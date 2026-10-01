using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Garante que, com N réplicas, só UMA execute a rodada de um job por vez — via
    /// <c>pg_try_advisory_xact_lock</c> no Postgres, que já é a fonte da verdade (nenhuma
    /// dependência nova; o Redis continua opcional).
    ///
    /// Por que a variante <b>xact</b> (de transação) e não <c>pg_advisory_lock</c> (de sessão):
    /// em produção a conexão passa pelo PgBouncer do Neon em modo <c>transaction</c>, que
    /// NÃO suporta advisory lock de sessão — a sessão pode trocar de conexão no servidor
    /// entre um comando e outro. Uma trava de transação vive exatamente enquanto a
    /// transação está aberta, e é nessa unidade que o pooler fixa a conexão.
    ///
    /// Como é usada: a trava fica numa transação aberta, numa conexão dedicada, enquanto
    /// a rodada roda em OUTRO escopo (outro DbContext, outra conexão). A transação da
    /// trava não escreve nada; só a encerra no fim, o que solta a trava. Se o processo
    /// morrer, o Postgres desfaz a transação e a trava cai sozinha.
    ///
    /// Não substitui as garantias por item (ex.: a reivindicação atômica de cada
    /// pagamento): se a conexão da trava cair no meio da rodada, a trava se solta e
    /// outra réplica pode começar — e a garantia por item continua valendo.
    /// </summary>
    public static class ExecucaoExclusiva
    {
        // Identificadores fixos (bigint) por job. "Tratoo" em ASCII + sequencial, para
        // não colidir com advisory locks de outros sistemas no mesmo banco.
        public const long ChavePagamentoLiberacao = 0x5472_6174_6F6F_0001;
        public const long ChaveReindexacao        = 0x5472_6174_6F6F_0002;

        /// <summary>
        /// Executa <paramref name="rodada"/> só se esta instância obtiver a trava
        /// <paramref name="chave"/>. Devolve false (sem executar) se outra instância já
        /// está rodando o mesmo job.
        /// </summary>
        public static async Task<bool> TentarExecutarAsync(
            IServiceScopeFactory scopeFactory,
            long chave,
            Func<IServiceProvider, CancellationToken, Task> rodada,
            ILogger logger,
            CancellationToken ct)
        {
            await using var escopoTrava = scopeFactory.CreateAsyncScope();
            var db = escopoTrava.ServiceProvider.GetRequiredService<TratooContext>();

            await using var transacao = await db.Database.BeginTransactionAsync(ct);

            var obtida = await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({chave}) AS \"Value\"")
                .SingleAsync(ct);

            if (!obtida)
            {
                await transacao.RollbackAsync(CancellationToken.None);
                return false;
            }

            try
            {
                await using var escopoRodada = scopeFactory.CreateAsyncScope();
                await rodada(escopoRodada.ServiceProvider, ct);
            }
            finally
            {
                try
                {
                    // Nada foi escrito nesta transação; encerrá-la libera a trava.
                    await transacao.RollbackAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // Ex.: a conexão caiu durante uma rodada longa. O Postgres já desfez a
                    // transação e soltou a trava — só registra.
                    logger.LogWarning(ex, "Falha ao encerrar a transação da trava {Chave}; o Postgres já a liberou.", chave);
                }
            }

            return true;
        }
    }
}
