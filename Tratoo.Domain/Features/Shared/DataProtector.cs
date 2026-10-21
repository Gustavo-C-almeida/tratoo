using System.Security.Cryptography;
using System.Text;

namespace Tratoo.Domain.Features.Shared
{
    /// <summary>
    /// Criptografia AES-256 dos dados sensíveis em repouso — conta bancária, chave PIX e
    /// CPF/CNPJ (LGPD Art. 46).
    ///
    /// A CHAVE NÃO FICA NO CÓDIGO. Ela vem da configuração <c>DataProtector:Chave</c>
    /// (variável de ambiente <c>DataProtector__Chave</c>; em desenvolvimento, user-secrets ou
    /// appsettings local). A API chama <see cref="Configurar"/> na subida e NÃO sobe sem ela
    /// (Program.cs); fora da API (ferramentas, testes) valem as variáveis de ambiente.
    ///
    /// Compatibilidade — o que NÃO pode mudar sem migração de dados:
    ///  • derivação da chave (<see cref="DerivarChave"/>): bytes UTF-8 do texto configurado,
    ///    truncados ou completados com zeros até 32 bytes;
    ///  • formato do valor cifrado: <c>base64(IV) + "|" + base64(AES-CBC/PKCS7)</c>.
    /// Com o MESMO texto de chave, este código lê tudo o que a versão anterior gravou.
    ///
    /// Rotação (Docs/ROTACAO-CHAVE-DADOS.md): durante a troca, <c>DataProtector:Chave</c> é a
    /// chave NOVA e <c>DataProtector:ChaveAnterior</c> a que está sendo aposentada. Grava-se
    /// sempre com a nova; lê-se com a nova e, se não decifrar, com a anterior. O comando
    /// <c>--recifrar-dados</c> regrava os valores antigos com a nova; depois disso a anterior
    /// sai da configuração.
    /// </summary>
    public static class DataProtector
    {
        /// <summary>Chave de configuração (variável de ambiente: <c>DataProtector__Chave</c>).</summary>
        public const string ChaveConfiguracao = "DataProtector:Chave";
        public const string VariavelDeAmbiente = "DataProtector__Chave";

        /// <summary>Chave sendo aposentada numa rotação. Opcional; ausente fora de uma rotação.</summary>
        public const string ChaveAnteriorConfiguracao = "DataProtector:ChaveAnterior";
        public const string VariavelDeAmbienteAnterior = "DataProtector__ChaveAnterior";

        /// <summary>Prefixo dos placeholders dos arquivos de exemplo — nunca é uma chave válida.</summary>
        private const string PrefixoPlaceholder = "DEFINA_";

        private static volatile ChavesDeDados? _chaves;

        /// <summary>
        /// Define as chaves do processo. Lança se a chave estiver ausente ou ainda for o
        /// placeholder do arquivo de exemplo — a API usa isso para se recusar a subir sem
        /// chave. <paramref name="chaveAnterior"/> vazia ou nula = sem rotação em curso.
        /// </summary>
        public static void Configurar(string? chave, string? chaveAnterior = null) =>
            _chaves = PrepararChaves(chave, chaveAnterior);

        /// <summary>As chaves em uso (configuradas, ou lidas das variáveis de ambiente).</summary>
        public static ChavesDeDados Chaves
        {
            get
            {
                var chaves = _chaves;
                if (chaves is not null)
                    return chaves;

                // Fora da API (ferramentas, testes): valem as variáveis de ambiente. Se a
                // principal também não existir, DerivarChave lança com a instrução.
                chaves = PrepararChaves(
                    Environment.GetEnvironmentVariable(VariavelDeAmbiente),
                    Environment.GetEnvironmentVariable(VariavelDeAmbienteAnterior));
                _chaves = chaves;
                return chaves;
            }
        }

        /// <summary>
        /// Valida e deriva o par de chaves sem alterar o estado do processo. A anterior, se
        /// informada, passa pelas mesmas regras da principal e não pode ser igual a ela
        /// (seria uma rotação que não troca nada — quase sempre variável copiada errado).
        /// </summary>
        public static ChavesDeDados PrepararChaves(string? chave, string? chaveAnterior)
        {
            var atual = DerivarChave(chave, VariavelDeAmbiente, ChaveConfiguracao);
            if (string.IsNullOrEmpty(chaveAnterior))
                return new ChavesDeDados(atual, null);

            var anterior = DerivarChave(chaveAnterior, VariavelDeAmbienteAnterior, ChaveAnteriorConfiguracao);
            if (CryptographicOperations.FixedTimeEquals(atual, anterior))
                throw new InvalidOperationException(
                    $"{VariavelDeAmbienteAnterior} é igual a {VariavelDeAmbiente}. Numa rotação, " +
                    $"{VariavelDeAmbiente} é a chave NOVA e {VariavelDeAmbienteAnterior} a antiga; " +
                    $"fora de uma rotação, remova {VariavelDeAmbienteAnterior}.");

            return new ChavesDeDados(atual, anterior);
        }

        /// <summary>
        /// Deriva a chave AES-256 do texto configurado, EXATAMENTE como sempre foi feito:
        /// bytes UTF-8, truncados ou completados com zeros até 32 bytes. O texto não é
        /// aparado nem normalizado — um espaço a mais no início muda a chave.
        /// </summary>
        public static byte[] DerivarChave(string? chave) =>
            DerivarChave(chave, VariavelDeAmbiente, ChaveConfiguracao);

        private static byte[] DerivarChave(string? chave, string variavel, string configuracao)
        {
            if (string.IsNullOrWhiteSpace(chave))
                throw new InvalidOperationException(
                    $"Chave de criptografia de dados sensíveis ausente: defina {variavel} " +
                    $"(configuração {configuracao}). Em produção ela precisa ser EXATAMENTE a chave " +
                    "com que os dados existentes foram cifrados — uma chave diferente os torna ilegíveis.");

            if (chave.StartsWith(PrefixoPlaceholder, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{variavel} ainda está com o placeholder do arquivo de exemplo. Defina uma chave real.");

            var raw = Encoding.UTF8.GetBytes(chave);
            var key = new byte[32];
            Buffer.BlockCopy(raw, 0, key, 0, Math.Min(raw.Length, 32));
            return key;
        }

        /// <summary>Cifra sempre com a chave atual — nunca com a anterior.</summary>
        public static string Encrypt(string data) => Encrypt(data, Chaves.Atual);

        /// <summary>
        /// Decifra com a chave atual. Numa rotação, o que não decifrar com ela é tentado com
        /// a anterior.
        /// </summary>
        public static string Decrypt(string data)
        {
            var chaves = Chaves;
            return chaves.Anterior is null
                ? Decrypt(data, chaves.Atual)
                : Decrypt(data, chaves.Atual, chaves.Anterior);
        }

        public static string Encrypt(string data, byte[] chave)
        {
            using var aes = Aes.Create();
            aes.Key = chave;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();

            byte[] input = Encoding.UTF8.GetBytes(data);
            byte[] encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);

            return Convert.ToBase64String(aes.IV) + "|" +
                   Convert.ToBase64String(encrypted);
        }

        public static string Decrypt(string data, byte[] chave)
        {
            var parts = data.Split('|');

            using var aes = Aes.Create();
            aes.Key = chave;
            aes.IV = Convert.FromBase64String(parts[0]);

            using var decryptor = aes.CreateDecryptor();

            byte[] cipher = Convert.FromBase64String(parts[1]);
            byte[] decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

            return Encoding.UTF8.GetString(decrypted);
        }

        /// <summary>Decifra com a atual e, se não der, com a anterior. Nenhuma das duas → lança.</summary>
        public static string Decrypt(string data, byte[] atual, byte[] anterior)
        {
            if (TentarDecifrar(data, atual, out var claro) || TentarDecifrar(data, anterior, out claro))
                return claro;

            throw new CryptographicException("O valor não decifra nem com a chave atual nem com a anterior.");
        }

        /// <summary>
        /// Decifra só se o resultado for confiável: o valor tem a forma de dado cifrado, o
        /// padding confere E o texto é plausível (<see cref="PareceTexto"/>).
        /// </summary>
        public static bool TentarDecifrar(string? valor, byte[] chave, out string claro)
        {
            claro = string.Empty;
            if (!PareceCifrado(valor))
                return false;

            try
            {
                var resultado = Decrypt(valor!, chave);
                if (!PareceTexto(resultado))
                    return false;

                claro = resultado;
                return true;
            }
            catch (CryptographicException)
            {
                return false; // chave errada = padding inválido
            }
        }

        /// <summary>
        /// Com a chave ERRADA o AES-CBC ainda "decifra" sem erro em ~1 de cada 256 valores (o
        /// padding casa por acaso) e devolve lixo. Os dados cifrados aqui são sempre texto
        /// (dígitos, e-mail, telefone, UUID): lixo quase sempre traz bytes UTF-8 inválidos
        /// (U+FFFD) ou caracteres de controle. É isso que separa "decifrou" de "padding casou".
        /// </summary>
        public static bool PareceTexto(string claro) =>
            !claro.Any(c => c == '�' || char.IsControl(c));

        /// <summary>
        /// Com qual chave o valor foi cifrado. Testa as duas, sem atalho: "Ambas" (as duas
        /// produzem texto plausível) é praticamente impossível, mas se acontecer o valor não
        /// pode ser recifrado às cegas.
        /// </summary>
        public static ChaveDoValor Identificar(string? valor, ChavesDeDados chaves)
        {
            if (!PareceCifrado(valor))
                return ChaveDoValor.NaoCifrado;

            var atual = TentarDecifrar(valor, chaves.Atual, out _);
            var anterior = chaves.Anterior is not null && TentarDecifrar(valor, chaves.Anterior, out _);

            return (atual, anterior) switch
            {
                (true, true) => ChaveDoValor.Ambas,
                (true, false) => ChaveDoValor.Atual,
                (false, true) => ChaveDoValor.Anterior,
                _ => ChaveDoValor.Nenhuma
            };
        }

        /// <summary>
        /// true se o valor tem a FORMA de algo cifrado por esta classe (IV de 16 bytes e
        /// blocos de 16), sem tentar decifrar. Serve para separar dado cifrado de lixo
        /// (ex.: valores fictícios de seed) antes de testar a chave.
        /// </summary>
        public static bool PareceCifrado(string? valor)
        {
            if (string.IsNullOrEmpty(valor))
                return false;

            var parts = valor.Split('|');
            if (parts.Length != 2)
                return false;

            Span<byte> iv = stackalloc byte[16];
            if (!Convert.TryFromBase64String(parts[0], iv, out var lidosIv) || lidosIv != 16)
                return false;

            var cifrado = new byte[(parts[1].Length * 3 / 4) + 3];
            return Convert.TryFromBase64String(parts[1], cifrado, out var lidos)
                   && lidos > 0 && lidos % 16 == 0;
        }
    }

    /// <summary>Chaves derivadas em uso. <see cref="Anterior"/> só existe durante uma rotação.</summary>
    public sealed record ChavesDeDados(byte[] Atual, byte[]? Anterior);

    public enum ChaveDoValor
    {
        /// <summary>Não tem a forma de dado cifrado (nulo, vazio, valor fictício de seed).</summary>
        NaoCifrado,
        Atual,
        Anterior,
        /// <summary>As duas chaves produzem texto plausível — não recifrar automaticamente.</summary>
        Ambas,
        /// <summary>Tem forma de dado cifrado, mas nenhuma das chaves decifra.</summary>
        Nenhuma
    }
}
