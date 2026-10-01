namespace Tratoo.Domain.Features.Infrastructure
{
    /// <summary>
    /// Estado efêmero de fluxo — códigos OTP (como hash), contadores de tentativa, cadastro
    /// pendente, janelas de autorização, marcas de "já enviado". Tudo com TTL; perder um
    /// valor obriga o usuário a repetir um passo, mas nunca corrompe dado de negócio.
    ///
    /// NÃO é lugar de fonte da verdade: dinheiro, contrato, ledger, auditoria e
    /// idempotência de webhook ficam no Postgres. Ver Docs/TRILHA2-DECISOES.md, seção 1.
    ///
    /// Duas implementações, escolhidas por configuração (EstadoEfemeroSetup na API):
    ///  • em memória — padrão, 1 réplica (produção hoje). Mesmo comportamento do
    ///    IMemoryCache que os serviços usavam diretamente;
    ///  • Redis — quando <c>Redis:ConnectionString</c> existe; compartilhado entre réplicas.
    ///
    /// Se o armazenamento estiver fora do ar, as operações lançam
    /// <see cref="Exceptions.ServicoIndisponivelException"/> (fail-closed): validar OTP
    /// "sem o armazenamento" seria aceitar qualquer código.
    /// </summary>
    public interface IEstadoEfemero
    {
        Task DefinirAsync<T>(string chave, T valor, TimeSpan ttl);

        /// <summary>Define só se a chave não existir. true = definiu agora (atômico).</summary>
        Task<bool> DefinirSeAusenteAsync<T>(string chave, T valor, TimeSpan ttl);

        /// <summary>Valor da chave, ou <c>default</c> se ausente/expirada.</summary>
        Task<T?> ObterAsync<T>(string chave);

        Task RemoverAsync(params string[] chaves);

        /// <summary>
        /// Soma 1 atomicamente (criando com 0 se não existir), renova o TTL e devolve o novo
        /// valor. Atômico entre réplicas — dois erros simultâneos contam dois.
        /// </summary>
        Task<long> IncrementarAsync(string chave, TimeSpan ttl);
    }
}
