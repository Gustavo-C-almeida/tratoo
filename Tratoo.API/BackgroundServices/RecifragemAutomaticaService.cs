using Tratoo.API.Infrastructure;
using Tratoo.Domain.Data;
using Tratoo.Domain.Features.Shared;

namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// Recifragem automática: a cada intervalo, regrava em v2, na chave atual, o que ainda
    /// estiver na chave anterior ou no formato antigo (AES-CBC). É o mesmo
    /// <see cref="ModoRecifragem.RecifrarAsync"/> do comando <c>--recifrar-dados --aplicar</c>,
    /// só que rodando dentro da API, como os outros jobs.
    ///
    /// DESLIGADO por padrão: só roda com <c>DataProtector__RecifrarAutomaticamente=true</c>.
    ///
    /// Garantias:
    ///  • com N réplicas, só UMA executa a rodada (<see cref="ExecucaoExclusiva"/>);
    ///  • cada rodada é uma única transação: uma interrupção (deploy, queda de conexão,
    ///    valor alterado no meio) desfaz tudo e a próxima rodada tenta de novo;
    ///  • depois de gravar, relê tudo do banco; só conta como concluída com zero pendências
    ///    e zero valores ilegíveis;
    ///  • NUNCA remove a chave anterior: o check "recifragem" do /health/ready só avisa que
    ///    ela pode sair da configuração depois dessa conferência;
    ///  • não registra nenhum valor, só contagens.
    /// </summary>
    public class RecifragemAutomaticaService : BackgroundService
    {
        public const string ChaveLigado = "DataProtector:RecifrarAutomaticamente";
        public const string ChaveIntervaloMinutos = "DataProtector:RecifragemIntervaloMinutos";
        private const int IntervaloPadraoMinutos = 60;
        private const int IntervaloMinimoMinutos = 5;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly EstadoRecifragem _estado;
        private readonly ILogger<RecifragemAutomaticaService> _logger;

        public RecifragemAutomaticaService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            EstadoRecifragem estado,
            ILogger<RecifragemAutomaticaService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _estado = estado;
            _logger = logger;
        }

        public static bool EstaLigado(IConfiguration configuration) =>
            configuration.GetValue<bool>(ChaveLigado);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!EstaLigado(_configuration))
            {
                _logger.LogInformation("Recifragem automática desligada ({Chave}).", ChaveLigado);
                return;
            }

            var minutos = Math.Max(IntervaloMinimoMinutos,
                _configuration.GetValue(ChaveIntervaloMinutos, IntervaloPadraoMinutos));
            _logger.LogInformation("Recifragem automática ligada: uma rodada a cada {Minutos} min.", minutos);

            // Como os outros jobs: espera a subida terminar.
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExecutarRodadaAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A rodada já registra o próprio erro; isto cobre falha ao obter a trava.
                    _logger.LogError("Recifragem automática: rodada não executada ({Tipo}).", ex.GetType().Name);
                }

                await Task.Delay(TimeSpan.FromMinutes(minutos), stoppingToken);
            }
        }

        /// <summary>
        /// Uma rodada, sob a trava exclusiva. Devolve false se outra réplica já está rodando.
        /// Público para os testes.
        /// </summary>
        public async Task<bool> ExecutarRodadaAsync(CancellationToken ct)
        {
            var executou = await ExecucaoExclusiva.TentarExecutarAsync(
                _scopeFactory, ExecucaoExclusiva.ChaveRecifragem,
                (servicos, token) => RodadaAsync(
                    servicos.GetRequiredService<TratooContext>(), DataProtector.Chaves, _estado, _logger, token),
                _logger, ct);

            if (!executou)
                _logger.LogDebug("Recifragem automática já em andamento em outra réplica; rodada ignorada.");

            return executou;
        }

        /// <summary>
        /// Relatório → (se houver pendências) recifragem numa transação → releitura. Nunca
        /// lança, salvo cancelamento: o resultado, ou o erro, vai para <paramref name="estado"/>.
        /// </summary>
        public static async Task RodadaAsync(
            TratooContext db, ChavesDeDados chaves, EstadoRecifragem estado, ILogger logger, CancellationToken ct)
        {
            try
            {
                var antes = await ModoRecifragem.RecifrarAsync(db, chaves, aplicar: false, ct);
                var recifrados = 0;
                if (antes.Sum(r => r.Pendentes) > 0)
                {
                    db.ChangeTracker.Clear();
                    recifrados = (await ModoRecifragem.RecifrarAsync(db, chaves, aplicar: true, ct)).Sum(r => r.Recifrados);
                }

                db.ChangeTracker.Clear();
                var depois = await ModoRecifragem.RecifrarAsync(db, chaves, aplicar: false, ct);
                var resultado = new ResultadoRecifragem(
                    DateTime.UtcNow,
                    Pendentes: depois.Sum(r => r.Pendentes),
                    Ilegiveis: depois.Sum(r => r.Nenhuma + r.Ambas),
                    Recifrados: recifrados,
                    ComChaveAnterior: chaves.Anterior is not null,
                    Erro: null);
                estado.Registrar(resultado);

                logger.LogInformation(
                    "Recifragem automática: recifrados={Recifrados} pendentes={Pendentes} ilegíveis={Ilegiveis}.",
                    resultado.Recifrados, resultado.Pendentes, resultado.Ilegiveis);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A transação já foi desfeita; a próxima rodada tenta de novo. Só o tipo e a
                // mensagem (que nunca carregam valor cifrado ou decifrado) vão para o log.
                estado.Registrar(new ResultadoRecifragem(
                    DateTime.UtcNow, Pendentes: -1, Ilegiveis: -1, Recifrados: 0,
                    ComChaveAnterior: chaves.Anterior is not null, Erro: ex.GetType().Name));
                logger.LogWarning("Recifragem automática falhou e foi desfeita ({Tipo}: {Mensagem}). Nova tentativa na próxima rodada.",
                    ex.GetType().Name, ex.Message);
            }
        }
    }
}
