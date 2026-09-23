using System.Net;
using System.Net.Sockets;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Conjunto de faixas CIDR usado para decidir se um endereço é confiável.
    /// Imutável e pré-parseado na subida — o caminho quente (uma checagem por
    /// requisição) não faz parsing de string.
    /// </summary>
    public sealed class RedeConfiavel
    {
        private readonly List<(IPAddress Prefixo, int Bits)> _faixas;

        /// <summary>Texto das faixas aceitas, como configuradas (para diagnóstico/log).</summary>
        public IReadOnlyList<string> FaixasConfiguradas { get; }

        /// <summary>Entradas que não puderam ser interpretadas (para log de alerta na subida).</summary>
        public IReadOnlyList<string> FaixasInvalidas { get; }

        /// <summary>Nenhuma faixa válida configurada — o gate fica desligado.</summary>
        public bool Vazio => _faixas.Count == 0;

        public RedeConfiavel(IEnumerable<string> cidrs)
        {
            _faixas = new List<(IPAddress, int)>();
            var configuradas = new List<string>();
            var invalidas = new List<string>();

            foreach (var cidr in cidrs ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(cidr))
                    continue;

                if (TentarConverter(cidr, out var prefixo, out var bits))
                {
                    _faixas.Add((prefixo!, bits));
                    configuradas.Add(cidr.Trim());
                }
                else
                {
                    invalidas.Add(cidr.Trim());
                }
            }

            FaixasConfiguradas = configuradas;
            FaixasInvalidas = invalidas;
        }

        /// <summary>
        /// Aceita "10.0.0.0/8" e também um IP solto ("10.0.0.7"), tratado como /32 ou /128.
        /// </summary>
        public static bool TentarConverter(string cidr, out IPAddress? prefixo, out int bits)
        {
            prefixo = null;
            bits = 0;

            var texto = cidr.Trim();
            var barra = texto.IndexOf('/');

            if (barra < 0)
            {
                if (!IPAddress.TryParse(texto, out var solto))
                    return false;

                prefixo = Normalizar(solto);
                bits = prefixo.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
                return true;
            }

            if (!IPAddress.TryParse(texto[..barra], out var endereco))
                return false;

            if (!int.TryParse(texto[(barra + 1)..], out var tamanho))
                return false;

            var normalizado = Normalizar(endereco);
            var maximo = normalizado.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;

            if (tamanho < 0 || tamanho > maximo)
                return false;

            prefixo = normalizado;
            bits = tamanho;
            return true;
        }

        /// <summary>
        /// True se o endereço cai em alguma das faixas. IPv4 mapeado em IPv6
        /// (<c>::ffff:100.64.0.1</c>) é comparado como IPv4 — é assim que o Kestrel
        /// costuma expor um peer IPv4 quando o socket é dual-stack.
        /// </summary>
        public bool Contem(IPAddress? endereco)
        {
            if (endereco is null || _faixas.Count == 0)
                return false;

            var alvo = Normalizar(endereco);
            var bytesAlvo = alvo.GetAddressBytes();

            foreach (var (prefixo, bits) in _faixas)
            {
                if (prefixo.AddressFamily != alvo.AddressFamily)
                    continue;

                if (PrefixoConfere(prefixo.GetAddressBytes(), bytesAlvo, bits))
                    return true;
            }

            return false;
        }

        private static bool PrefixoConfere(byte[] prefixo, byte[] alvo, int bits)
        {
            var bytesInteiros = bits / 8;
            var bitsRestantes = bits % 8;

            for (var i = 0; i < bytesInteiros; i++)
            {
                if (prefixo[i] != alvo[i])
                    return false;
            }

            if (bitsRestantes == 0)
                return true;

            var mascara = (byte)(0xFF << (8 - bitsRestantes));
            return (prefixo[bytesInteiros] & mascara) == (alvo[bytesInteiros] & mascara);
        }

        /// <summary>IPv4 mapeado em IPv6 vira IPv4; o resto passa direto.</summary>
        private static IPAddress Normalizar(IPAddress endereco)
            => endereco.IsIPv4MappedToIPv6 ? endereco.MapToIPv4() : endereco;
    }
}
