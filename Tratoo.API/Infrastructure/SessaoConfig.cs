using System.Globalization;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Tempos e limites do controle de sessão (seção <c>Sessao</c>, variáveis <c>Sessao__*</c>).
    /// Todos têm padrão no código: sem nenhuma variável, vale o que está aqui. Substitui o
    /// antigo <c>Jwt:ExpirationHours</c>.
    ///
    /// Os valores aceitam fração (<c>Sessao__InatividadeMinutos=0.05</c> = 3 s), e os testes
    /// constroem o objeto direto com <see cref="TimeSpan"/> em segundos — sem sleeps longos.
    /// </summary>
    public sealed record SessaoConfig
    {
        public const string Secao = "Sessao";

        public TimeSpan AccessToken { get; init; } = TimeSpan.FromMinutes(15);
        public TimeSpan AccessTokenAdmin { get; init; } = TimeSpan.FromMinutes(10);
        public TimeSpan Inatividade { get; init; } = TimeSpan.FromMinutes(30);
        public TimeSpan InatividadeAdmin { get; init; } = TimeSpan.FromMinutes(15);
        public TimeSpan DuracaoAbsoluta { get; init; } = TimeSpan.FromHours(12);
        public TimeSpan DuracaoAbsolutaAdmin { get; init; } = TimeSpan.FromHours(8);
        public TimeSpan ManterConectado { get; init; } = TimeSpan.FromDays(14);
        public TimeSpan InatividadeManterConectado { get; init; } = TimeSpan.FromDays(7);
        public int MaxSessoes { get; init; } = 5;
        public int MaxSessoesAdmin { get; init; } = 2;
        public TimeSpan Reautenticacao { get; init; } = TimeSpan.FromMinutes(10);
        public TimeSpan ReautenticacaoAdmin { get; init; } = TimeSpan.FromMinutes(5);
        public TimeSpan ToleranciaRefreshConcorrente { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Intervalo mínimo entre duas regravações da sessão por atividade. Abaixo disso a
        /// requisição só confere a sessão (um GET), sem escrever no Redis.
        /// </summary>
        public TimeSpan IntervaloRegistroAtividade { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>Por quanto tempo um refresh token já rotacionado é lembrado (detecção de reuso).</summary>
        public TimeSpan RetencaoRefreshUsado { get; init; } = TimeSpan.FromHours(24);

        public static SessaoConfig DeConfiguracao(IConfiguration configuration)
        {
            var s = configuration.GetSection(Secao);
            var p = new SessaoConfig();

            return p with
            {
                AccessToken = Ler(s, "AccessTokenMinutos", TimeSpan.FromMinutes, p.AccessToken),
                AccessTokenAdmin = Ler(s, "AccessTokenMinutosAdmin", TimeSpan.FromMinutes, p.AccessTokenAdmin),
                Inatividade = Ler(s, "InatividadeMinutos", TimeSpan.FromMinutes, p.Inatividade),
                InatividadeAdmin = Ler(s, "InatividadeMinutosAdmin", TimeSpan.FromMinutes, p.InatividadeAdmin),
                DuracaoAbsoluta = Ler(s, "DuracaoAbsolutaHoras", TimeSpan.FromHours, p.DuracaoAbsoluta),
                DuracaoAbsolutaAdmin = Ler(s, "DuracaoAbsolutaHorasAdmin", TimeSpan.FromHours, p.DuracaoAbsolutaAdmin),
                ManterConectado = Ler(s, "ManterConectadoDias", TimeSpan.FromDays, p.ManterConectado),
                InatividadeManterConectado = Ler(s, "InatividadeManterConectadoDias", TimeSpan.FromDays, p.InatividadeManterConectado),
                MaxSessoes = LerInteiro(s, "MaxSessoes", p.MaxSessoes),
                MaxSessoesAdmin = LerInteiro(s, "MaxSessoesAdmin", p.MaxSessoesAdmin),
                Reautenticacao = Ler(s, "ReautenticacaoMinutos", TimeSpan.FromMinutes, p.Reautenticacao),
                ReautenticacaoAdmin = Ler(s, "ReautenticacaoMinutosAdmin", TimeSpan.FromMinutes, p.ReautenticacaoAdmin),
                ToleranciaRefreshConcorrente = Ler(s, "ToleranciaRefreshConcorrenteSegundos", TimeSpan.FromSeconds, p.ToleranciaRefreshConcorrente),
                IntervaloRegistroAtividade = Ler(s, "IntervaloRegistroAtividadeSegundos", TimeSpan.FromSeconds, p.IntervaloRegistroAtividade),
            };
        }

        /// <summary>
        /// Os tempos que valem para uma sessão. "Manter conectado" não vale para Admin: a
        /// sessão administrativa é sempre curta.
        /// </summary>
        public PoliticaSessao Para(bool admin, bool manterConectado)
        {
            if (admin)
                return new PoliticaSessao(AccessTokenAdmin, InatividadeAdmin, DuracaoAbsolutaAdmin, MaxSessoesAdmin, ReautenticacaoAdmin, ManterConectado: false);

            return manterConectado
                ? new PoliticaSessao(AccessToken, InatividadeManterConectado, ManterConectado, MaxSessoes, Reautenticacao, ManterConectado: true)
                : new PoliticaSessao(AccessToken, Inatividade, DuracaoAbsoluta, MaxSessoes, Reautenticacao, ManterConectado: false);
        }

        private static TimeSpan Ler(IConfigurationSection s, string chave, Func<double, TimeSpan> converter, TimeSpan padrao)
        {
            var bruto = s[chave];
            if (string.IsNullOrWhiteSpace(bruto))
                return padrao;

            if (!double.TryParse(bruto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
                throw new InvalidOperationException($"{Secao}:{chave} precisa ser um número positivo (recebido: '{bruto}').");

            return converter(valor);
        }

        private static int LerInteiro(IConfigurationSection s, string chave, int padrao)
        {
            var bruto = s[chave];
            if (string.IsNullOrWhiteSpace(bruto))
                return padrao;

            if (!int.TryParse(bruto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) || valor < 1)
                throw new InvalidOperationException($"{Secao}:{chave} precisa ser um inteiro maior que zero (recebido: '{bruto}').");

            return valor;
        }
    }

    /// <summary>Tempos efetivos de uma sessão (dependem do papel e do "manter conectado").</summary>
    public sealed record PoliticaSessao(
        TimeSpan AccessToken,
        TimeSpan Inatividade,
        TimeSpan DuracaoAbsoluta,
        int MaxSessoes,
        TimeSpan Reautenticacao,
        bool ManterConectado);
}
