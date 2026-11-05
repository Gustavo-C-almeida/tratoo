namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Nome curto da réplica que está em execução. Aparece no corpo de <c>/health/*</c> e em
    /// cada linha do log do console, para mostrar qual instância atendeu e qual rodou um job.
    ///
    /// Na Railway vem de <c>RAILWAY_REPLICA_ID</c>; no compose, do nome da máquina (o id do
    /// container). Só os primeiros caracteres são expostos: o suficiente para distinguir as
    /// réplicas, sem publicar o identificador inteiro numa rota anônima.
    /// </summary>
    public static class IdentificacaoInstancia
    {
        public const string VariavelDaPlataforma = "RAILWAY_REPLICA_ID";

        private const int Tamanho = 8;

        public static string Atual { get; } = Resolver(
            Environment.GetEnvironmentVariable(VariavelDaPlataforma), Environment.MachineName);

        public static string Resolver(string? idDaPlataforma, string nomeDaMaquina)
        {
            var origem = (string.IsNullOrWhiteSpace(idDaPlataforma) ? nomeDaMaquina : idDaPlataforma).Trim();
            return origem.Length <= Tamanho ? origem : origem[..Tamanho];
        }
    }
}
