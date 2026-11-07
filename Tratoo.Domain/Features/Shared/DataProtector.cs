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
    /// Formato v2 (AES-256-GCM, autenticado): <c>v2:&lt;id&gt;:base64(nonce ‖ cifra ‖ tag)</c>.
    ///  • a chave GCM e o <c>id</c> (8 hex) são derivados por HKDF-SHA256 dos mesmos 32 bytes
    ///    acima, com rótulos próprios — um único segredo configurado serve aos dois formatos;
    ///  • o <c>id</c> diz QUAL chave cifrou, sem tentativa e erro; o prefixo inteiro
    ///    (<c>v2:&lt;id&gt;</c>) entra como dado associado, então adulterar qualquer parte invalida;
    ///  • <see cref="Encrypt(string)"/> GRAVA v2. O formato antigo continua sendo LIDO (dados
    ///    já existentes) até a recifragem convertê-los (<c>--recifrar-dados</c>); só se grava
    ///    CBC com <see cref="EncryptLegado"/>, que existe para testes de compatibilidade.
    ///  • ROLLBACK: valores v2 só são lidos a partir da versão que introduziu a leitura de v2
    ///    (etapa 2a). Não volte a produção para uma versão anterior a ela.
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

        /// <summary>Tamanho mínimo, em bytes UTF-8, da chave NOVA durante uma rotação.</summary>
        public const int TamanhoMinimoChaveNova = 32;

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

            // Numa troca, a chave NOVA tem de ter 32+ bytes: abaixo disso a derivação completa
            // com zeros e a chave perde entropia. A chave atual fora de uma troca segue aceita
            // como está, para não impedir a subida com a chave que já cifra os dados.
            if (Encoding.UTF8.GetByteCount(chave!) < TamanhoMinimoChaveNova)
                throw new InvalidOperationException(
                    $"Numa rotação, a chave nova ({VariavelDeAmbiente}) precisa ter ao menos " +
                    $"{TamanhoMinimoChaveNova} bytes. Gere com `openssl rand -base64 32`.");

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

        /// <summary>Cifra no formato v2 (AES-256-GCM).</summary>
        public static string Encrypt(string data, byte[] chave) => EncryptV2(data, chave);

        /// <summary>
        /// Formato ANTIGO (AES-CBC, sem autenticação). Nenhum código de produção grava com
        /// isto: existe só para testes de compatibilidade com os dados já gravados.
        /// </summary>
        public static string EncryptLegado(string data, byte[] chave)
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
            if (EhV2(data))
                return DecryptV2(data, chave);

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
                // v2 é autenticado (GCM): se decifrou, é o valor certo. A heurística de texto
                // só existe para o CBC, que "decifra" lixo com a chave errada.
                if (!EhV2(valor) && !PareceTexto(resultado))
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

            if (EhV2(valor))
                return TentarLerV2(valor, out _, out _);

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

        // ─── Formato v2 (AES-256-GCM) ─────────────────────────────────────────────

        public const string PrefixoV2 = "v2:";
        private const int TamanhoIdV2 = 8;      // hex
        private const int TamanhoNonce = 12;
        private const int TamanhoTag = 16;
        private static readonly byte[] InfoCifraV2 = Encoding.UTF8.GetBytes("tratoo/dados/v2/cifra");
        private static readonly byte[] InfoIdV2 = Encoding.UTF8.GetBytes("tratoo/dados/v2/id");

        /// <summary>true se o valor declara o formato v2 (pelo prefixo; não valida o resto).</summary>
        public static bool EhV2(string? valor) =>
            valor is not null && valor.StartsWith(PrefixoV2, StringComparison.Ordinal);

        /// <summary>Identificador público (8 hex) da chave no formato v2. Não revela a chave.</summary>
        public static string IdDaChaveV2(byte[] chave) =>
            Convert.ToHexString(HKDF.DeriveKey(HashAlgorithmName.SHA256, chave, TamanhoIdV2 / 2, info: InfoIdV2))
                .ToLowerInvariant();

        private static byte[] ChaveGcm(byte[] chave) =>
            HKDF.DeriveKey(HashAlgorithmName.SHA256, chave, 32, info: InfoCifraV2);

        /// <summary>Cifra no formato v2 (é o que <see cref="Encrypt(string, byte[])"/> usa).</summary>
        public static string EncryptV2(string data, byte[] chave)
        {
            var id = IdDaChaveV2(chave);
            var aad = Encoding.UTF8.GetBytes(PrefixoV2 + id);
            var claro = Encoding.UTF8.GetBytes(data);

            var pacote = new byte[TamanhoNonce + claro.Length + TamanhoTag];
            var nonce = pacote.AsSpan(0, TamanhoNonce);
            var cifra = pacote.AsSpan(TamanhoNonce, claro.Length);
            var tag = pacote.AsSpan(TamanhoNonce + claro.Length, TamanhoTag);
            RandomNumberGenerator.Fill(nonce);

            using var gcm = new AesGcm(ChaveGcm(chave), TamanhoTag);
            gcm.Encrypt(nonce, claro, cifra, tag, aad);

            return PrefixoV2 + id + ":" + Convert.ToBase64String(pacote);
        }

        /// <summary>
        /// Decifra um valor v2. Lança <see cref="CryptographicException"/> se o valor estiver
        /// malformado, se for de outra chave (id diferente) ou se tiver sido adulterado (tag).
        /// </summary>
        public static string DecryptV2(string data, byte[] chave)
        {
            if (!TentarLerV2(data, out var id, out var pacote))
                throw new CryptographicException("Valor v2 malformado.");

            if (!string.Equals(id, IdDaChaveV2(chave), StringComparison.Ordinal))
                throw new CryptographicException("Valor v2 cifrado com outra chave.");

            var tamanhoClaro = pacote.Length - TamanhoNonce - TamanhoTag;
            var claro = new byte[tamanhoClaro];
            using var gcm = new AesGcm(ChaveGcm(chave), TamanhoTag);
            gcm.Decrypt(
                pacote.AsSpan(0, TamanhoNonce),
                pacote.AsSpan(TamanhoNonce, tamanhoClaro),
                pacote.AsSpan(TamanhoNonce + tamanhoClaro, TamanhoTag),
                claro,
                Encoding.UTF8.GetBytes(PrefixoV2 + id)); // lança AuthenticationTagMismatchException

            return Encoding.UTF8.GetString(claro);
        }

        /// <summary>Lê a estrutura de um valor v2 sem decifrar: <c>v2:&lt;8 hex&gt;:&lt;base64&gt;</c>.</summary>
        private static bool TentarLerV2(string valor, out string id, out byte[] pacote)
        {
            id = string.Empty;
            pacote = Array.Empty<byte>();

            var resto = valor.AsSpan(PrefixoV2.Length);
            if (resto.Length <= TamanhoIdV2 + 1 || resto[TamanhoIdV2] != ':')
                return false;

            var idSpan = resto[..TamanhoIdV2];
            foreach (var c in idSpan)
                if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c))
                    return false;

            var base64 = resto[(TamanhoIdV2 + 1)..];
            var buffer = new byte[(base64.Length * 3 / 4) + 3];
            if (!Convert.TryFromBase64Chars(base64, buffer, out var lidos)
                || lidos < TamanhoNonce + TamanhoTag)
                return false;

            id = idSpan.ToString();
            pacote = buffer.AsSpan(0, lidos).ToArray();
            return true;
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
