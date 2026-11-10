using System.Text.RegularExpressions;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>Quem a rota DEVERIA atender (regra de negócio).</summary>
    public enum Intencao { Publico, Autenticado, Contratante, Prestador, Admin, SoDevelopment }

    /// <summary>O que a rota EXIGE de fato, no nível do endpoint (policy do ASP.NET).</summary>
    public enum Guarda { Nenhuma, Autenticado, Contratante, Prestador, Admin }

    public sealed record Rota(
        string Metodo,
        string Modelo,
        Intencao Intencao,
        Guarda Guarda,
        string? RateLimit = null,
        bool ReautenticacaoRecente = false,
        bool EncerraSessao = false)
    {
        /// <summary>Forma do Swagger: sem as restrições de rota (<c>{id:guid}</c> → <c>{id}</c>).</summary>
        public string ModeloSwagger => Regex.Replace(Modelo, @"\{(\w+):\w+\}", "{$1}");

        /// <summary>URL concreta com IDs que não existem (o objetivo é a guarda, não o recurso).</summary>
        public string Url(Guid? guid = null, int? inteiro = null) => Regex.Replace(Modelo, @"\{(\w+)(?::(\w+))?\}", m =>
            m.Groups[2].Value switch
            {
                "guid" => (guid ?? Guid.Parse("00000000-0000-0000-0000-0000000000aa")).ToString(),
                "int" => (inteiro ?? 999999).ToString(),
                _ => m.Groups[1].Value == "cep" ? "01310100" : (inteiro ?? 999999).ToString()
            });

        public HttpMethod Http => new(Metodo);
        public override string ToString() => $"{Metodo} {Modelo}";
    }

    /// <summary>
    /// Matriz de endpoints da auditoria — espelho de Docs/security-audit/endpoint-matrix.md.
    /// O teste de inventário falha se a API tiver uma rota que não está aqui (ou vice-versa):
    /// rota nova obriga a decidir, explicitamente, quem pode chamá-la.
    /// </summary>
    public static class MatrizEndpoints
    {
        private const Intencao Pub = Intencao.Publico, Aut = Intencao.Autenticado, Con = Intencao.Contratante,
            Pre = Intencao.Prestador, Adm = Intencao.Admin, Dev = Intencao.SoDevelopment;
        private const Guarda N = Guarda.Nenhuma, A = Guarda.Autenticado, C = Guarda.Contratante, P = Guarda.Prestador, AD = Guarda.Admin;

        public static readonly IReadOnlyList<Rota> Todas = new Rota[]
        {
            // ── Autenticação, sessão e conta ─────────────────────────────────
            new("POST", "/usuarios/login", Pub, N, "login"),
            new("POST", "/usuarios/login/mfa", Pub, N, "login"),
            new("POST", "/usuarios/logout", Pub, N, EncerraSessao: true),
            new("POST", "/usuarios/cadastro", Pub, N, "cadastro"),
            new("POST", "/usuarios/cadastro/reenviar-codigo", Pub, N, "cadastro"),
            new("POST", "/usuarios/cadastro/confirmar", Pub, N),
            new("POST", "/usuarios/senha/resetar/solicitar", Pub, N, "senha"),
            new("POST", "/usuarios/senha/resetar", Pub, N, "senha"),
            new("POST", "/usuarios/onboarding", Aut, A),
            new("DELETE", "/usuarios/conta", Aut, A, ReautenticacaoRecente: true, EncerraSessao: true),
            new("GET", "/api/me", Aut, A),
            new("POST", "/api/auth/refresh", Pub, N, "refresh"),
            new("POST", "/api/auth/logout", Pub, N, EncerraSessao: true),
            new("POST", "/api/auth/logout-todas", Aut, A, EncerraSessao: true),
            new("GET", "/api/auth/sessoes", Aut, A),
            new("DELETE", "/api/auth/sessoes/{id}", Aut, A),
            new("POST", "/api/auth/atividade", Aut, A),
            new("POST", "/api/auth/reautenticar", Aut, A, "reautenticar"),
            new("GET", "/api/conta/seguranca", Aut, A),
            new("POST", "/api/conta/senha", Aut, A, ReautenticacaoRecente: true),
            new("PUT", "/api/conta/mfa", Aut, A, ReautenticacaoRecente: true, EncerraSessao: true),

            // ── Administração ─────────────────────────────────────────────────
            new("GET", "/api/admin/disputas", Adm, AD),
            new("GET", "/api/admin/disputas/{disputaId:guid}", Adm, AD),
            new("POST", "/api/admin/disputas/{disputaId:guid}/resolver", Adm, AD, ReautenticacaoRecente: true),
            new("POST", "/api/admin/pagamentos/{id:guid}/reprocessar-transferencia", Adm, AD),
            new("GET", "/api/diagnostico/rede", Adm, AD),
            // Requisito: só admin. Hoje anônimas (sondas da Railway/Docker) — achado D1.
            new("GET", "/health/live", Adm, N),
            new("GET", "/health/ready", Adm, N),
            new("POST", "/api/dev/seed-admin", Dev, N),
            new("POST", "/api/dev/seed-pagamentos", Dev, N),

            // ── Webhook e configuração ────────────────────────────────────────
            new("POST", "/api/webhooks/asaas", Pub, N),
            new("GET", "/api/config/financeiro", Pub, N),
            new("GET", "/api/cep/{cep}", Pub, N),

            // ── Busca ─────────────────────────────────────────────────────────
            new("GET", "/api/busca/prestadores", Pub, N),
            new("GET", "/api/busca/prestadores/{id:int}/similares", Pub, N),
            new("GET", "/api/busca/projetos", Pub, N),
            new("GET", "/api/busca/projetos/{id:int}/prestadores-recomendados", Con, C),
            new("GET", "/api/busca/projetos/recomendados", Pre, P),

            // ── Projetos, chat e convites ─────────────────────────────────────
            new("GET", "/api/projects", Pub, N),
            new("GET", "/api/projects/{id:int}", Pub, N),
            new("POST", "/api/projects", Con, C),
            new("POST", "/api/projects/{id:int}/publish", Con, C),
            new("DELETE", "/api/projects/{id:int}", Con, C),
            new("GET", "/api/me/projects", Con, C),
            new("GET", "/api/projects/{id:int}/proposals", Con, C),
            new("GET", "/api/projects/{id:int}/mensagens", Aut, A),
            new("POST", "/api/projects/{id:int}/mensagens", Aut, A),
            new("GET", "/api/me/chats", Aut, A),
            new("POST", "/api/projects/{projetoId:int}/convites", Con, C),
            new("GET", "/api/projects/{projetoId:int}/convites", Con, C),
            new("PUT", "/api/convites/{id:guid}/aceitar", Pre, P),
            new("PUT", "/api/convites/{id:guid}/recusar", Pre, P),
            new("GET", "/api/me/convites", Pre, P),
            new("POST", "/api/convites/{conviteId:guid}/proposta", Con, C),

            // ── Propostas ─────────────────────────────────────────────────────
            new("POST", "/api/propostas", Pre, P),
            new("POST", "/api/propostas/{id:guid}/enviar", Pre, P),
            new("PUT", "/api/propostas/{id:guid}/aceitar-negociacao", Pre, P),
            new("POST", "/api/propostas/{id:guid}/contraproposta", Aut, A),
            new("PUT", "/api/propostas/{id:guid}/aceitar", Con, C),
            new("PUT", "/api/propostas/{id:guid}/recusar", Con, C),
            new("DELETE", "/api/propostas/{id:guid}", Pre, P),
            new("GET", "/api/propostas/{id:guid}", Aut, A),
            new("GET", "/api/me/propostas", Pre, P),

            // ── Contratos e entregas ──────────────────────────────────────────
            new("GET", "/api/contratos/{id:guid}", Aut, A),
            new("GET", "/api/me/contratos", Aut, A),
            new("POST", "/api/contratos/{id:guid}/assinar/solicitar-otp", Aut, A, "otp-assinatura"),
            new("POST", "/api/contratos/{id:guid}/assinar", Aut, A),
            new("GET", "/api/contratos/{id:guid}/pdf", Aut, A),
            new("POST", "/api/contratos/{id:guid}/entrega", Pre, A),            // D3/D6
            new("GET", "/api/contratos/{id:guid}/entrega", Aut, A),
            new("POST", "/api/contratos/{id:guid}/entrega/aprovar", Con, A, ReautenticacaoRecente: true), // D6
            new("POST", "/api/contratos/{id:guid}/entrega/rejeitar", Con, A),   // D6
            new("DELETE", "/api/contratos/{id:guid}", Aut, A),
            new("GET", "/api/contratos/{contratoId:guid}/avaliacoes/pendente", Aut, A),

            // ── Pagamentos ────────────────────────────────────────────────────
            new("POST", "/api/pagamentos/iniciar", Con, C),
            new("GET", "/api/pagamentos/{id:guid}/pix", Con, C),
            new("GET", "/api/pagamentos/{id:guid}", Aut, A),
            new("POST", "/api/pagamentos/{id:guid}/liberar", Con, C, ReautenticacaoRecente: true),
            new("POST", "/api/pagamentos/{id:guid}/disputar", Aut, A),
            new("POST", "/api/pagamentos/{id:guid}/estornar", Con, C),
            new("GET", "/api/pagamentos/{id:guid}/sincronizar", Aut, A),

            // ── Avaliações ────────────────────────────────────────────────────
            new("GET", "/api/avaliacoes/{id:guid}", Aut, A),
            new("POST", "/api/avaliacoes/{id:guid}/enviar", Aut, A),
            new("PUT", "/api/me/avaliacoes/privacidade", Aut, A),
            new("GET", "/api/usuarios/{id:int}/reputacao", Pub, N),
            new("GET", "/api/usuarios/{id:int}/avaliacoes", Pub, N),

            // ── Perfil do contratante ─────────────────────────────────────────
            new("GET", "/contratantes/{id}/perfil", Pub, N),
            new("GET", "/contratantes/me/perfil", Con, C),
            new("PUT", "/contratantes/me/perfil", Con, C),
            new("POST", "/contratantes/me/foto", Con, C),
            new("DELETE", "/contratantes/me/foto", Con, C),

            // ── Perfil do prestador ───────────────────────────────────────────
            new("GET", "/prestadores/{id}/perfil", Pub, N),
            new("GET", "/prestadores/me/perfil", Pre, P),
            new("PUT", "/prestadores/me/perfil", Pre, P),
            new("POST", "/prestadores/me/foto", Pre, P),
            new("POST", "/prestadores/me/portfolio", Pre, P),
            new("PUT", "/prestadores/me/portfolio/{id}", Pre, P),
            new("DELETE", "/prestadores/me/portfolio/{id}", Pre, P),
            new("POST", "/prestadores/me/experiencias", Pre, P),
            new("PUT", "/prestadores/me/experiencias/{id}", Pre, P),
            new("DELETE", "/prestadores/me/experiencias/{id}", Pre, P),
            new("POST", "/prestadores/me/certificacoes", Pre, P),
            new("PUT", "/prestadores/me/certificacoes/{id}", Pre, P),
            new("DELETE", "/prestadores/me/certificacoes/{id}", Pre, P),
            new("POST", "/prestadores/{prestadorId}/competencias", Pre, A),                                   // D6
            new("DELETE", "/prestadores/{prestadorId}/competencias/{id}", Pre, A),                            // D6
            new("POST", "/prestadores/me/experiencias/{expId}/competencias/{compId}", Pre, A),                // D6
            new("DELETE", "/prestadores/me/experiencias/{expId}/competencias/{compId}", Pre, A),              // D6
            new("POST", "/prestadores/me/certificacoes/{certId}/competencias/{compId}", Pre, A),              // D6
            new("DELETE", "/prestadores/me/certificacoes/{certId}/competencias/{compId}", Pre, A),            // D6
            new("POST", "/prestadores/me/portfolio/{portId}/competencias/{compId}", Pre, A),                  // D6
            new("DELETE", "/prestadores/me/portfolio/{portId}/competencias/{compId}", Pre, A),                // D6
            new("GET", "/prestadores/me/dados-bancarios", Pre, P),
            new("POST", "/prestadores/me/dados-bancarios/solicitar-alteracao", Pre, P, "dados-bancarios"),
            new("POST", "/prestadores/me/dados-bancarios/confirmar", Pre, P, "dados-bancarios"),
            new("PUT", "/prestadores/me/dados-bancarios", Pre, P, "dados-bancarios"),
        };

        /// <summary>Rotas que existem em Production (as de seed são só de Development).</summary>
        public static IEnumerable<Rota> Producao => Todas.Where(r => r.Intencao != Intencao.SoDevelopment);

        /// <summary>Rotas públicas que respondem 401 por regra própria (sem cookie de refresh / sem token do webhook).</summary>
        public static readonly HashSet<string> PublicasQueRecusamSemCredencialPropria = new()
        {
            "POST /api/auth/refresh", "POST /api/webhooks/asaas"
        };

        public static IEnumerable<object[]> Dados(IEnumerable<Rota> rotas) => rotas.Select(r => new object[] { r.ToString() });

        public static Rota Por(string chave) => Todas.Single(r => r.ToString() == chave);

        public static IEnumerable<object[]> Protegidas => Dados(Producao.Where(r => r.Guarda != Guarda.Nenhuma));
        public static IEnumerable<object[]> Publicas => Dados(Producao.Where(r => r.Guarda == Guarda.Nenhuma && r.Intencao == Intencao.Publico));
        public static IEnumerable<object[]> SoContratante => Dados(Producao.Where(r => r.Guarda == Guarda.Contratante));
        public static IEnumerable<object[]> SoPrestador => Dados(Producao.Where(r => r.Guarda == Guarda.Prestador));
        public static IEnumerable<object[]> SoAdmin => Dados(Producao.Where(r => r.Guarda == Guarda.Admin));
        public static IEnumerable<object[]> PapelSoNoServico => Dados(Producao.Where(r =>
            r.Guarda == Guarda.Autenticado && r.Intencao is Intencao.Contratante or Intencao.Prestador));
        public static IEnumerable<object[]> ComRateLimit => Producao.Where(r => r.RateLimit is not null)
            .Select(r => new object[] { r.ToString() });
    }
}
