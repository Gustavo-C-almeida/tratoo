using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Contratos
{
    /// <summary>
    /// Regras de entrada da entrega formal, aplicadas pelo endpoint ANTES de qualquer upload e
    /// repetidas pelo serviço ao gravar (achado D3).
    /// </summary>
    public static class EntregaRegras
    {
        /// <summary>Soma máxima dos arquivos de uma entrega: 25 MiB.</summary>
        public const long LimiteTotalArquivosBytes = 25L * 1024 * 1024;

        /// <summary>
        /// Limite do corpo da requisição: os 25 MiB dos arquivos mais 1 MiB de folga para o
        /// envelope multipart (fronteiras, cabeçalhos de cada parte, descrição, links).
        /// </summary>
        public const long LimiteRequisicaoBytes = LimiteTotalArquivosBytes + 1024 * 1024;

        public const string MensagemLimiteExcedido =
            "A entrega ultrapassa o limite de 25 MB no total. Reduza ou compacte os arquivos e envie de novo.";

        /// <summary>Mesmos limites das colunas EntregaLinks.Url (1000) e .Descricao (300).</summary>
        public const int TamanhoMaximoUrl = 1000;
        public const int TamanhoMaximoDescricaoLink = 300;

        /// <summary>
        /// Só URL absoluta http/https, com host, até 1000 caracteres; descrição até 300.
        /// Recusa javascript:, data:, vbscript:, file:, ftp:, relativas e sem host — o link é
        /// exibido ao contratante como &lt;a href&gt;. Itens com URL vazia são ignorados (como antes).
        /// </summary>
        public static void ValidarLinks(IEnumerable<EntregaLinkDto>? links)
        {
            if (links is null) return;

            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link.Url))
                    continue;

                var url = link.Url.Trim();
                if (url.Length > TamanhoMaximoUrl)
                    throw new NegocioException($"Link muito longo: o máximo é {TamanhoMaximoUrl} caracteres.");

                var valido = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                             && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                             && !string.IsNullOrWhiteSpace(uri.Host);
                if (!valido)
                    throw new NegocioException("Link inválido: informe um endereço completo começando com http:// ou https://.");

                if (link.Descricao is not null && link.Descricao.Trim().Length > TamanhoMaximoDescricaoLink)
                    throw new NegocioException($"Descrição do link muito longa: o máximo é {TamanhoMaximoDescricaoLink} caracteres.");
            }
        }
    }
}
