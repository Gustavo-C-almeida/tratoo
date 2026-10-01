using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Features.Auth;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Um Redis real (mesma imagem e flags do compose: redis:7-alpine, senha, sem
    /// persistência). <see cref="NovaReplica"/> monta um container de DI pelo MESMO
    /// caminho da produção (<c>AddTratooEstadoEfemero</c>), cada um com seu próprio
    /// ConnectionMultiplexer — duas chamadas = duas réplicas da API.
    /// </summary>
    public sealed class RedisFixture : IAsyncLifetime
    {
        private const string Senha = "teste_redis_integracao";
        public static readonly string ChaveProtecao = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

        private ContainerDocker? _container;

        public string ConnectionString =>
            $"127.0.0.1:{_container!.Porta},password={Senha},abortConnect=false,connectTimeout=2000,asyncTimeout=2000";

        /// <summary>Conexão "crua", para inspecionar o que de fato ficou gravado no Redis.</summary>
        public ConnectionMultiplexer Bruto { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            if (!DockerCli.Disponivel)
                return;

            _container = ContainerDocker.Iniciar("redis:7-alpine", 6379,
                comando: new[] { "redis-server", "--requirepass", Senha, "--save", "", "--appendonly", "no" });

            var limite = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                try
                {
                    Bruto = await ConnectionMultiplexer.ConnectAsync(ConnectionString.Replace("abortConnect=false", "abortConnect=true"));
                    await Bruto.GetDatabase().PingAsync();
                    return;
                }
                catch when (DateTime.UtcNow < limite)
                {
                    await Task.Delay(300);
                }
            }
        }

        public ServiceProvider NovaReplica()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [EstadoEfemeroSetup.ChaveConexao] = ConnectionString,
                    [ProtecaoEstadoEfemero.ChaveConfiguracao] = ChaveProtecao
                })
                .Build();

            var s = new ServiceCollection();
            s.AddLogging();
            s.AddTratooEstadoEfemero(config);
            s.AddScoped<IVerificacaoMFAService, VerificacaoMFAService>();
            return s.BuildServiceProvider();
        }

        public async Task DisposeAsync()
        {
            if (Bruto is not null)
                await Bruto.DisposeAsync();
            if (_container is not null)
                await _container.DisposeAsync();
        }
    }

    [CollectionDefinition(Nome)]
    public sealed class ColecaoRedis : ICollectionFixture<RedisFixture>
    {
        public const string Nome = "Redis real (Docker)";
    }
}
