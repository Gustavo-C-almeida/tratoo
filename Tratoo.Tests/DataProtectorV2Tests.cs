using System.Security.Cryptography;
using Tratoo.Domain.Features.Shared;
using Xunit;
using Referencia = Tratoo.Tests.DataProtectorTests.Referencia;

namespace Tratoo.Tests
{
    /// <summary>
    /// Migração AES-CBC → AES-GCM: o código GRAVA v2 (etapa 2b) e continua LENDO o formato
    /// antigo dos dados já gravados. Estes testes travam:
    ///  • v2 é gravado, lido corretamente e rejeita chave errada, adulteração e valor malformado;
    ///  • o que a versão anterior gravou (<see cref="Referencia"/>) continua legível.
    /// Nenhuma chave real aparece aqui.
    /// </summary>
    public class DataProtectorV2Tests
    {
        private const string TextoChaveA = "chave-de-teste-A-0123456789-abcdef";
        private const string TextoChaveB = "chave-de-teste-B-0123456789-abcdef";
        private static readonly byte[] ChaveA = DataProtector.DerivarChave(TextoChaveA);
        private static readonly byte[] ChaveB = DataProtector.DerivarChave(TextoChaveB);

        public static readonly IEnumerable<object[]> Claros = new[]
        {
            new object[] { "12345678909" },                       // CPF
            new object[] { "12345678000195" },                    // CNPJ
            new object[] { "fulano@exemplo.com.br" },             // PIX e-mail
            new object[] { "1f0e4c1a-3b2d-4c5e-8f9a-0b1c2d3e4f5a" }, // PIX aleatória
            new object[] { "ação ç ééé — multibyte" },
            new object[] { "" }
        };

        // ── Gravação ─────────────────────────────────────────────────────────

        [Theory]
        [MemberData(nameof(Claros))]
        public void Encrypt_grava_v2(string claro)
        {
            var valor = DataProtector.Encrypt(claro, ChaveA);

            Assert.StartsWith(DataProtector.PrefixoV2 + DataProtector.IdDaChaveV2(ChaveA) + ":", valor);
            Assert.Equal(claro, DataProtector.Decrypt(valor, ChaveA));
        }

        [Fact]
        public void Encrypt_sem_chave_explicita_usa_a_chave_atual_e_grava_v2()
        {
            var valor = DataProtector.Encrypt("12345678909");
            Assert.True(DataProtector.EhV2(valor));
            Assert.Equal("12345678909", DataProtector.Decrypt(valor));
        }

        [Theory]
        [MemberData(nameof(Claros))]
        public void EncryptLegado_mantem_o_formato_antigo_legivel_pela_versao_anterior(string claro)
        {
            var valor = DataProtector.EncryptLegado(claro, ChaveA);

            Assert.False(DataProtector.EhV2(valor));
            Assert.Equal(claro, Referencia.Decifrar(valor, TextoChaveA));
        }

        [Theory]
        [MemberData(nameof(Claros))]
        public void Valor_gravado_pela_versao_anterior_continua_legivel(string claro)
        {
            if (claro.Length == 0) return; // CBC de texto vazio não é dado real

            var antigo = Referencia.Cifrar(claro, TextoChaveA);

            Assert.Equal(claro, DataProtector.Decrypt(antigo, ChaveA));
            Assert.True(DataProtector.TentarDecifrar(antigo, ChaveA, out var lido));
            Assert.Equal(claro, lido);
        }

        // ── Leitura de v2 ────────────────────────────────────────────────────

        [Theory]
        [MemberData(nameof(Claros))]
        public void V2_ida_e_volta(string claro)
        {
            var valor = DataProtector.EncryptV2(claro, ChaveA);

            Assert.StartsWith(DataProtector.PrefixoV2 + DataProtector.IdDaChaveV2(ChaveA) + ":", valor);
            Assert.True(DataProtector.PareceCifrado(valor));
            Assert.Equal(claro, DataProtector.Decrypt(valor, ChaveA));   // o Decrypt de sempre reconhece v2
            Assert.Equal(claro, DataProtector.DecryptV2(valor, ChaveA));
        }

        [Fact]
        public void V2_usa_nonce_aleatorio()
        {
            var a = DataProtector.EncryptV2("12345678909", ChaveA);
            var b = DataProtector.EncryptV2("12345678909", ChaveA);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Id_da_chave_e_estavel_distinto_por_chave_e_nao_contem_a_chave()
        {
            var id = DataProtector.IdDaChaveV2(ChaveA);

            Assert.Equal(id, DataProtector.IdDaChaveV2(DataProtector.DerivarChave(TextoChaveA)));
            Assert.NotEqual(id, DataProtector.IdDaChaveV2(ChaveB));
            Assert.Matches("^[0-9a-f]{8}$", id);
            Assert.DoesNotContain(Convert.ToHexString(ChaveA).ToLowerInvariant()[..8], id);
        }

        [Fact]
        public void Formatos_antigo_e_v2_convivem_na_mesma_leitura()
        {
            var chaves = new ChavesDeDados(ChaveA, null);
            var antigo = Referencia.Cifrar("12345678909", TextoChaveA);
            var novo = DataProtector.EncryptV2("12345678000195", ChaveA);

            Assert.Equal(ChaveDoValor.Atual, DataProtector.Identificar(antigo, chaves));
            Assert.Equal(ChaveDoValor.Atual, DataProtector.Identificar(novo, chaves));
            Assert.Equal("12345678909", DataProtector.Decrypt(antigo, ChaveA));
            Assert.Equal("12345678000195", DataProtector.Decrypt(novo, ChaveA));
        }

        // ── Chave errada / rotação ───────────────────────────────────────────

        [Fact]
        public void V2_com_chave_errada_e_rejeitado()
        {
            var valor = DataProtector.EncryptV2("12345678909", ChaveA);

            Assert.Throws<CryptographicException>(() => DataProtector.Decrypt(valor, ChaveB));
            Assert.False(DataProtector.TentarDecifrar(valor, ChaveB, out _));
            Assert.Equal(ChaveDoValor.Nenhuma, DataProtector.Identificar(valor, new ChavesDeDados(ChaveB, null)));
        }

        [Fact]
        public void Durante_rotacao_v2_da_chave_anterior_e_identificado_e_lido()
        {
            var rotacao = new ChavesDeDados(Atual: ChaveB, Anterior: ChaveA);
            var naAnterior = DataProtector.EncryptV2("12345678909", ChaveA);
            var naAtual = DataProtector.EncryptV2("12345678909", ChaveB);

            Assert.Equal(ChaveDoValor.Anterior, DataProtector.Identificar(naAnterior, rotacao));
            Assert.Equal(ChaveDoValor.Atual, DataProtector.Identificar(naAtual, rotacao));
            Assert.Equal("12345678909", DataProtector.Decrypt(naAnterior, ChaveB, ChaveA));
        }

        // ── Adulteração ──────────────────────────────────────────────────────

        public static readonly IEnumerable<object[]> PosicoesAdulteradas = new[]
        {
            new object[] { 0 },    // nonce
            new object[] { 12 },   // cifra
            new object[] { -1 }    // tag (último byte)
        };

        [Theory]
        [MemberData(nameof(PosicoesAdulteradas))]
        public void V2_adulterado_e_rejeitado(int posicao)
        {
            var valor = DataProtector.EncryptV2("12345678909", ChaveA);
            var separador = valor.LastIndexOf(':');
            var pacote = Convert.FromBase64String(valor[(separador + 1)..]);
            pacote[posicao >= 0 ? posicao : pacote.Length + posicao] ^= 0x01;
            var adulterado = valor[..(separador + 1)] + Convert.ToBase64String(pacote);

            Assert.True(DataProtector.PareceCifrado(adulterado)); // forma ok — só a tag denuncia
            Assert.ThrowsAny<CryptographicException>(() => DataProtector.Decrypt(adulterado, ChaveA));
            Assert.False(DataProtector.TentarDecifrar(adulterado, ChaveA, out _));
        }

        [Fact]
        public void V2_com_id_trocado_e_rejeitado_mesmo_que_o_id_seja_de_outra_chave_valida()
        {
            var valor = DataProtector.EncryptV2("12345678909", ChaveA);
            var trocado = valor.Replace(DataProtector.IdDaChaveV2(ChaveA), DataProtector.IdDaChaveV2(ChaveB));

            // O id faz parte do dado autenticado: nem com a chave B (id agora "certo") decifra.
            Assert.ThrowsAny<CryptographicException>(() => DataProtector.Decrypt(trocado, ChaveB));
            Assert.ThrowsAny<CryptographicException>(() => DataProtector.Decrypt(trocado, ChaveA));
        }

        // ── Malformado ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("v2:")]
        [InlineData("v2:abcdef01")]
        [InlineData("v2:abcdef01:")]
        [InlineData("v2:abcdef01:AAAA")]                 // menor que nonce + tag
        [InlineData("v2:ABCDEF01:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // id maiúsculo
        [InlineData("v2:abcd:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]     // id curto
        [InlineData("v2:zzzzzzzz:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // id não-hex
        [InlineData("v2:abcdef01:não é base64!!")]
        public void V2_malformado_nao_parece_cifrado_e_nao_decifra(string valor)
        {
            Assert.False(DataProtector.PareceCifrado(valor));
            Assert.False(DataProtector.TentarDecifrar(valor, ChaveA, out _));
            Assert.Throws<CryptographicException>(() => DataProtector.Decrypt(valor, ChaveA));
            Assert.Equal(ChaveDoValor.NaoCifrado, DataProtector.Identificar(valor, new ChavesDeDados(ChaveA, null)));
        }

        [Fact]
        public void Outra_versao_de_prefixo_nao_e_tratada_como_v2()
        {
            var v3 = "v3:" + DataProtector.EncryptV2("12345678909", ChaveA)[3..];
            Assert.False(DataProtector.EhV2(v3));
            Assert.False(DataProtector.PareceCifrado(v3));
        }

        // ── Rollback ─────────────────────────────────────────────────────────

        /// <summary>
        /// Uma versão anterior à 2a (só CBC) NÃO lê o que a produção grava agora: por isso o
        /// rollback não pode voltar para antes da 2a (Docs/ROTACAO-CHAVES.md).
        /// </summary>
        [Fact]
        public void Versao_anterior_a_leitura_de_v2_NAO_le_o_que_e_gravado_agora()
        {
            var valor = DataProtector.Encrypt("12345678909", ChaveA);
            Assert.ThrowsAny<Exception>(() => Referencia.Decifrar(valor, TextoChaveA));
        }
    }
}
