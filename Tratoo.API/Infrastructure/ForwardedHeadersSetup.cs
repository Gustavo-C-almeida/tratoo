using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Configuração da seção "ForwardedHeaders" do appsettings / variáveis de ambiente.
    /// Em produção (Railway) NÃO existe appsettings.json dentro da imagem — ele está no
    /// .dockerignore — então toda config vem de variáveis planas no formato
    /// <c>ForwardedHeaders__ForwardLimit</c>, <c>ForwardedHeaders__PeersConfiaveis__0</c>.
    /// Sem nenhuma variável definida, valem os padrões desta classe.
    /// </summary>
    public class ForwardedHeadersSettings
    {
        /// <summary>Liga/desliga o processamento dos headers encaminhados. Padrão: true.</summary>
        public bool Habilitado { get; set; } = true;

        /// <summary>
        /// Quantos saltos percorrer em <c>X-Forwarded-For</c>, da direita para a esquerda.
        ///
        /// Padrão 2 — topologia observada em produção no Railway (2026-09-22): o header
        /// chega ao Kestrel como "&lt;cliente&gt;, &lt;ip-do-pool-de-borda&gt;", e o primeiro
        /// valor bate com o campo <c>srcIp</c> autoritativo do `railway logs --http`.
        /// Com 1, a aplicação gravaria o IP do pool de borda como se fosse o do cliente.
        ///
        /// Só mude se a topologia mudar (ex.: Nginx/Cloudflare próprios à frente do
        /// Railway somam +1 salto cada).
        /// </summary>
        public int ForwardLimit { get; set; } = 2;

        /// <summary>
        /// Faixas CIDR de onde a conexão TCP pode legitimamente chegar ao Kestrel.
        /// É ESTE o trust boundary: requisição cujo peer não estiver aqui tem os headers
        /// <c>X-Forwarded-*</c> descartados antes de qualquer processamento
        /// (ver <see cref="PeerConfiavelMiddleware"/>).
        ///
        /// Padrões — cada faixa é observada ou não-roteável por definição, nenhuma inventada:
        ///  • 100.64.0.0/10  — RFC 6598 (CGNAT). É o que o Kestrel enxerga como peer no
        ///    Railway (100.64.0.1/.2/.3/.12/.13/.14 observados). Não é roteável na internet.
        ///  • fc00::/7       — RFC 4193 (ULA). Os network flow logs do Railway mostram o
        ///    ingresso na porta 8080 vindo de fd12:0:8::/48 (21 endereços distintos em 2h).
        ///    Coberto de forma ampla porque o Railway não documenta o prefixo exato.
        ///  • 127.0.0.0/8 e ::1/128 — loopback, para desenvolvimento local e health checks.
        ///
        /// Lista vazia desliga o gate (qualquer peer passa). É opt-out explícito e gera
        /// aviso na subida — use só se a aplicação não estiver atrás de proxy nenhum.
        ///
        /// IMPORTANTE — fica <c>null</c> por padrão de propósito, e o padrão real vem de
        /// <see cref="PeersConfiaveisPadrao"/> via <see cref="ResolverPeersConfiaveis"/>.
        /// O binder de configuração do .NET ANEXA a arrays que já têm valor inicial em
        /// vez de substituí-los: se o padrão estivesse aqui, configurar a seção só
        /// conseguiria AMPLIAR o trust boundary, nunca restringi-lo — e as faixas padrão
        /// continuariam confiáveis silenciosamente.
        /// </summary>
        public string[]? PeersConfiaveis { get; set; }

        /// <summary>Faixas usadas quando a configuração não define <see cref="PeersConfiaveis"/>.</summary>
        public static readonly string[] PeersConfiaveisPadrao =
        {
            "100.64.0.0/10",
            "fc00::/7",
            "127.0.0.0/8",
            "::1/128"
        };

        /// <summary>
        /// Faixas efetivas: o que veio da configuração (mesmo vazio, que desliga o gate)
        /// ou, se nada foi configurado, <see cref="PeersConfiaveisPadrao"/>.
        /// </summary>
        public string[] ResolverPeersConfiaveis() => PeersConfiaveis ?? PeersConfiaveisPadrao;

        /// <summary>
        /// IPs de proxies confiáveis repassados ao middleware do ASP.NET Core para
        /// validar CADA SALTO da cadeia (não só o peer).
        ///
        /// Padrão vazio, deliberadamente. ATENÇÃO à armadilha, comprovada por teste
        /// (<c>PinarSoAFaixaDoPeer_QuebraAIdentificacaoDoCliente</c>): preencher isto
        /// cobrindo apenas a faixa do peer faz o middleware parar no primeiro salto e
        /// adotar o IP do pool de borda do Railway como "cliente". Para usar esta lista
        /// é preciso cobrir TODOS os saltos — e a faixa do pool de borda do Railway não
        /// é documentada (a lista de CIDRs deles responde 404). Enquanto isso, o trust
        /// boundary é o <see cref="PeersConfiaveis"/>.
        /// </summary>
        public string[] KnownProxies { get; set; } = Array.Empty<string>();

        /// <summary>Redes de proxies confiáveis em CIDR. Mesma ressalva de <see cref="KnownProxies"/>.</summary>
        public string[] KnownNetworks { get; set; } = Array.Empty<string>();
    }

    public static class ForwardedHeadersSetup
    {
        public const string SecaoConfiguracao = "ForwardedHeaders";

        /// <summary>
        /// Registra o middleware oficial de Forwarded Headers e o gate de peer confiável.
        ///
        /// Apenas <c>XForwardedFor</c> e <c>XForwardedProto</c> são processados.
        /// <c>X-Forwarded-Host</c> fica de fora de propósito: nada no projeto gera URL
        /// absoluta a partir de <c>Request.Host</c>, e confiar no host encaminhado com
        /// <c>AllowedHosts: "*"</c> abriria host-header injection sem nenhum ganho.
        /// </summary>
        public static IServiceCollection AddTratooForwardedHeaders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var settings = new ForwardedHeadersSettings();
            configuration.GetSection(SecaoConfiguracao).Bind(settings);

            services.AddSingleton(settings);
            services.AddSingleton(new RedeConfiavel(settings.ResolverPeersConfiaveis()));
            services.Configure<ForwardedHeadersOptions>(options => Aplicar(options, settings));

            return services;
        }

        /// <summary>
        /// Traduz <see cref="ForwardedHeadersSettings"/> em <see cref="ForwardedHeadersOptions"/>.
        /// Exposto para permitir cobertura por testes sem subir a aplicação inteira.
        /// </summary>
        public static void Aplicar(ForwardedHeadersOptions options, ForwardedHeadersSettings settings)
        {
            if (!settings.Habilitado)
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;

            // Os defaults do framework confiam só em loopback; atrás do Railway o peer
            // nunca é loopback, então sem limpar isto o middleware não faria nada.
            // Quem restringe a origem aqui é o PeerConfiavelMiddleware, não estas listas
            // (ver comentário em ForwardedHeadersSettings.KnownProxies).
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var proxy in settings.KnownProxies)
            {
                if (IPAddress.TryParse(proxy.Trim(), out var endereco))
                    options.KnownProxies.Add(endereco);
            }

            foreach (var rede in settings.KnownNetworks)
            {
                if (RedeConfiavel.TentarConverter(rede, out var prefixo, out var bits))
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefixo!, bits));
            }
        }
    }
}
