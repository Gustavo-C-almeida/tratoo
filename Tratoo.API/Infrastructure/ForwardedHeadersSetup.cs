using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Configuração da seção "ForwardedHeaders" do appsettings / variáveis de ambiente.
    /// No Railway use o formato plano: <c>ForwardedHeaders__ConfiarNoProxyImediato=true</c>.
    /// </summary>
    public class ForwardedHeadersSettings
    {
        /// <summary>Liga/desliga o processamento dos headers encaminhados. Padrão: true.</summary>
        public bool Habilitado { get; set; } = true;

        /// <summary>
        /// Quantos saltos de proxy confiar, contados da direita para a esquerda em
        /// <c>X-Forwarded-For</c>. Padrão 1 = apenas o proxy de borda.
        /// Só aumente se houver mais de um proxy controlado por você em cadeia
        /// (ex.: Cloudflare → Nginx → app = 2).
        /// </summary>
        public int ForwardLimit { get; set; } = 1;

        /// <summary>
        /// Quando true, limpa KnownProxies/KnownNetworks — o peer TCP imediato é
        /// aceito como proxy confiável, qualquer que seja seu IP.
        ///
        /// Necessário em PaaS onde o IP interno do proxy é dinâmico e não documentado
        /// (Railway). Só é seguro porque o Kestrel não é alcançável diretamente da
        /// internet: todo tráfego entra pelo proxy da plataforma.
        ///
        /// Quando null, assume <c>true</c> fora do ambiente Development.
        /// Prefira <c>false</c> + <see cref="KnownProxies"/>/<see cref="KnownNetworks"/>
        /// assim que o endereço do proxy for conhecido e estável (ex.: Nginx próprio).
        /// </summary>
        public bool? ConfiarNoProxyImediato { get; set; }

        /// <summary>IPs de proxies confiáveis (ex.: "10.0.0.7").</summary>
        public string[] KnownProxies { get; set; } = Array.Empty<string>();

        /// <summary>Redes de proxies confiáveis em CIDR (ex.: "10.0.0.0/8").</summary>
        public string[] KnownNetworks { get; set; } = Array.Empty<string>();
    }

    public static class ForwardedHeadersSetup
    {
        public const string SecaoConfiguracao = "ForwardedHeaders";

        /// <summary>
        /// Registra o middleware oficial de Forwarded Headers para que
        /// <c>Connection.RemoteIpAddress</c>, <c>Request.Scheme</c> e
        /// <c>Request.IsHttps</c> reflitam o cliente original, e não o proxy.
        ///
        /// <c>X-Forwarded-Host</c> NÃO é processado de propósito: nada no projeto
        /// gera URL absoluta a partir de <c>Request.Host</c>, e confiar no host
        /// encaminhado com <c>AllowedHosts: "*"</c> abriria host-header injection.
        /// Se um dia o backend passar a montar links absolutos, adicione
        /// <c>XForwardedHost</c> aqui E restrinja <c>AllowedHosts</c> ao domínio real.
        /// </summary>
        public static IServiceCollection AddTratooForwardedHeaders(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var settings = new ForwardedHeadersSettings();
            configuration.GetSection(SecaoConfiguracao).Bind(settings);

            services.Configure<ForwardedHeadersOptions>(options =>
                Aplicar(options, settings, environment.IsDevelopment()));

            return services;
        }

        /// <summary>
        /// Traduz <see cref="ForwardedHeadersSettings"/> em <see cref="ForwardedHeadersOptions"/>.
        /// Exposto para permitir cobertura por testes sem subir a aplicação inteira.
        /// </summary>
        public static void Aplicar(
            ForwardedHeadersOptions options,
            ForwardedHeadersSettings settings,
            bool isDevelopment)
        {
            if (!settings.Habilitado)
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;

            // Os defaults do framework confiam apenas em loopback (::1 e 127.0.0.1/8).
            // Atrás do Railway o peer é o IP interno do proxy — sem limpar/substituir
            // essas listas o middleware silenciosamente não faz nada.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            var confiarNoProxyImediato = settings.ConfiarNoProxyImediato ?? !isDevelopment;

            if (confiarNoProxyImediato)
            {
                // Ambas as listas vazias => o middleware não valida o peer e aplica
                // os headers. Combinado com ForwardLimit=1, o IP adotado é a ÚLTIMA
                // entrada de X-Forwarded-For — a que o proxy de borda escreveu.
                return;
            }

            foreach (var proxy in settings.KnownProxies)
            {
                if (IPAddress.TryParse(proxy.Trim(), out var endereco))
                    options.KnownProxies.Add(endereco);
            }

            foreach (var rede in settings.KnownNetworks)
            {
                if (TentarConverterCidr(rede, out var network))
                    options.KnownNetworks.Add(network!);
            }

            // Sem nenhum proxy configurado, restaura o default do framework (loopback).
            // Listas vazias significariam "confiar em qualquer peer" — o oposto do
            // que ConfiarNoProxyImediato=false pediu.
            if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
            {
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
                options.KnownNetworks.Add(
                    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("::ffff:127.0.0.1"), 104));
            }
        }

        private static bool TentarConverterCidr(
            string cidr,
            out Microsoft.AspNetCore.HttpOverrides.IPNetwork? network)
        {
            network = null;

            var partes = cidr.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 2)
                return false;

            if (!IPAddress.TryParse(partes[0], out var prefixo))
                return false;

            if (!int.TryParse(partes[1], out var tamanhoPrefixo))
                return false;

            var maximo = prefixo.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
            if (tamanhoPrefixo < 0 || tamanhoPrefixo > maximo)
                return false;

            network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefixo, tamanhoPrefixo);
            return true;
        }
    }
}
