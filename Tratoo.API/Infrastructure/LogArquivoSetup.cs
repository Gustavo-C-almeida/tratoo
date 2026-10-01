namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Liga/desliga o sink de arquivo do Serilog (<c>logs/openai-.txt</c>, relativo ao
    /// WORKDIR). Apesar do nome, o arquivo recebe TODOS os logs, não só os da OpenAI.
    ///
    /// Padrão = ligado, para a produção (Railway) seguir exatamente como está. O
    /// docker-compose desliga (<c>LogArquivo__Habilitado=false</c>): em container o
    /// destino certo é o stdout — `docker compose logs` já agrega por réplica — e, com
    /// duas réplicas, um volume compartilhado faria dois processos gravarem no mesmo
    /// arquivo rolante. Ver Docs/TRILHA2-DECISOES.md, seção 1.5.
    /// </summary>
    public static class LogArquivoSetup
    {
        public const string Chave = "LogArquivo:Habilitado";

        public static bool Habilitado(IConfiguration configuration) =>
            configuration.GetValue(Chave, defaultValue: true);
    }
}
