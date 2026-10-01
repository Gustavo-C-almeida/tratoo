using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tratoo.Domain.Data;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Um Postgres (mesma imagem do compose: pgvector/pgvector:pg16) por execução da
    /// coleção. Cada teste pede um banco NOVO e já migrado (<see cref="CriarBancoMigradoAsync"/>),
    /// então os testes não dependem de ordem nem de dados uns dos outros.
    /// </summary>
    public sealed class PostgresFixture : IAsyncLifetime
    {
        private const string Senha = "teste_integracao";
        private ContainerDocker? _container;

        private string ConexaoServidor(string banco) =>
            $"Host=127.0.0.1;Port={_container!.Porta};Username=postgres;Password={Senha};Database={banco};Timeout=10";

        public async Task InitializeAsync()
        {
            if (!DockerCli.Disponivel)
                return; // os testes desta coleção são [FactComDocker] e serão ignorados

            _container = ContainerDocker.Iniciar("pgvector/pgvector:pg16", 5432,
                ambiente: new[] { $"POSTGRES_PASSWORD={Senha}" });

            // Conexão TCP só é aceita depois que a inicialização da imagem termina (o
            // servidor temporário do initdb escuta apenas no socket Unix).
            var limite = DateTime.UtcNow.AddSeconds(90);
            while (true)
            {
                try
                {
                    await using var c = new NpgsqlConnection(ConexaoServidor("postgres"));
                    await c.OpenAsync();
                    await using var cmd = new NpgsqlCommand("SELECT 1", c);
                    await cmd.ExecuteScalarAsync();
                    return;
                }
                catch when (DateTime.UtcNow < limite)
                {
                    await Task.Delay(500);
                }
            }
        }

        /// <summary>Cria um banco vazio, aplica as migrations reais e devolve a connection string.</summary>
        public async Task<string> CriarBancoMigradoAsync()
        {
            var nome = "t_" + Guid.NewGuid().ToString("N")[..16];

            await using (var c = new NpgsqlConnection(ConexaoServidor("postgres")))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {nome}", c);
                await cmd.ExecuteNonQueryAsync();
            }

            var conexao = ConexaoServidor(nome);
            await using var db = new TratooContext(new DbContextOptionsBuilder<TratooContext>().UseNpgsql(conexao).Options);
            await db.Database.MigrateAsync();
            return conexao;
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
                await _container.DisposeAsync();
        }
    }

    [CollectionDefinition(Nome)]
    public sealed class ColecaoPostgres : ICollectionFixture<PostgresFixture>
    {
        public const string Nome = "Postgres real (Docker)";
    }
}
