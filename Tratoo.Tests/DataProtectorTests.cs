using System.Security.Cryptography;
using System.Text;
using Tratoo.Domain.Features.Shared;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// A chave do <see cref="DataProtector"/> saiu do código-fonte. O que estes testes
    /// travam é a COMPATIBILIDADE: com o mesmo texto de chave, o código novo precisa ler
    /// tudo o que o código antigo gravou — senão conta bancária, chave PIX e CPF/CNPJ já
    /// cifrados no banco viram lixo.
    ///
    /// <see cref="Referencia"/> é uma implementação independente, escrita a partir da regra
    /// (chave = UTF-8 truncado/completado com zeros até 32 bytes; valor = base64(IV) | base64
    /// (AES-CBC/PKCS7)) — é contra ela, e não contra o próprio DataProtector, que se compara.
    /// Nenhuma chave real aparece aqui.
    /// </summary>
    public class DataProtectorTests
    {
        /// <summary>O algoritmo de sempre, reescrito de forma independente.</summary>
        private static class Referencia
        {
            public static byte[] Chave(string texto)
            {
                var bytes = Encoding.UTF8.GetBytes(texto);
                var chave = new byte[32];
                Array.Copy(bytes, chave, Math.Min(bytes.Length, 32));
                return chave;
            }

            public static string Cifrar(string claro, string textoDaChave)
            {
                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Chave(textoDaChave);
                aes.GenerateIV();
                var cifrado = aes.EncryptCbc(Encoding.UTF8.GetBytes(claro), aes.IV, PaddingMode.PKCS7);
                return Convert.ToBase64String(aes.IV) + "|" + Convert.ToBase64String(cifrado);
            }

            public static string Decifrar(string valor, string textoDaChave)
            {
                var partes = valor.Split('|');
                using var aes = Aes.Create();
                aes.Key = Chave(textoDaChave);
                var claro = aes.DecryptCbc(Convert.FromBase64String(partes[1]), Convert.FromBase64String(partes[0]), PaddingMode.PKCS7);
                return Encoding.UTF8.GetString(claro);
            }
        }

        public static readonly IEnumerable<object[]> TextosDeChave = new[]
        {
            new object[] { "curta" },                                       // completa com zeros
            new object[] { "exatamente-trinta-e-dois-bytes-!" },            // 32 bytes
            new object[] { "MAIS_LONGA_QUE_TRINTA_E_DOIS_BYTES" },          // 34: trunca em 32
            new object[] { "com acentuação e ç — multibyte em UTF-8 ééé" }  // corte no meio de bytes UTF-8
        };

        [Theory]
        [MemberData(nameof(TextosDeChave))]
        public void Derivacao_da_chave_e_a_de_sempre(string texto)
        {
            Assert.Equal(Referencia.Chave(texto), DataProtector.DerivarChave(texto));
        }

        [Theory]
        [MemberData(nameof(TextosDeChave))]
        public void Le_o_que_a_implementacao_anterior_gravou_e_vice_versa(string texto)
        {
            const string claro = "12345678909";
            var chave = DataProtector.DerivarChave(texto);

            Assert.Equal(claro, DataProtector.Decrypt(Referencia.Cifrar(claro, texto), chave));
            Assert.Equal(claro, Referencia.Decifrar(DataProtector.Encrypt(claro, chave), texto));
        }

        [Fact]
        public void Texto_da_chave_nao_e_aparado_nem_normalizado()
        {
            // Um espaço ou caixa diferente muda a chave — por isso a variável na Railway
            // precisa ser EXATAMENTE o valor atual.
            Assert.NotEqual(DataProtector.DerivarChave("chave-de-exemplo"), DataProtector.DerivarChave(" chave-de-exemplo"));
            Assert.NotEqual(DataProtector.DerivarChave("chave-de-exemplo"), DataProtector.DerivarChave("CHAVE-DE-EXEMPLO"));
            // Depois do 32º byte nada entra na chave: um espaço no fim de um texto longo não importa.
            Assert.Equal(
                DataProtector.DerivarChave("MAIS_LONGA_QUE_TRINTA_E_DOIS_BYTES"),
                DataProtector.DerivarChave("MAIS_LONGA_QUE_TRINTA_E_DOIS_BYTES "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("DEFINA_UMA_CHAVE_ALEATORIA_COM_32_OU_MAIS_CARACTERES")]
        public void Chave_ausente_ou_placeholder_e_recusada_com_instrucao(string? texto)
        {
            var erro = Assert.Throws<InvalidOperationException>(() => DataProtector.DerivarChave(texto));
            Assert.Contains(DataProtector.VariavelDeAmbiente, erro.Message);
        }

        [Fact]
        public void Formato_do_valor_cifrado_e_IV_aleatorio()
        {
            var a = DataProtector.Encrypt("mesmo texto");
            var b = DataProtector.Encrypt("mesmo texto");

            Assert.NotEqual(a, b); // IV novo a cada vez
            var partes = a.Split('|');
            Assert.Equal(2, partes.Length);
            Assert.Equal(16, Convert.FromBase64String(partes[0]).Length);
            Assert.Equal(0, Convert.FromBase64String(partes[1]).Length % 16);
            Assert.Equal("mesmo texto", DataProtector.Decrypt(a));
        }

        [Fact]
        public void Reconhece_a_forma_de_dado_cifrado_sem_decifrar()
        {
            Assert.True(DataProtector.PareceCifrado(DataProtector.Encrypt("12345678909")));
            // Cifrado com OUTRA chave continua tendo a forma — é isso que permite detectar chave errada.
            Assert.True(DataProtector.PareceCifrado(Referencia.Cifrar("x", "outra-chave-qualquer")));

            Assert.False(DataProtector.PareceCifrado(null));
            Assert.False(DataProtector.PareceCifrado(""));
            Assert.False(DataProtector.PareceCifrado("SEED_ENCRYPTED_CPF_PRESTADOR"));   // valor fictício do seed de dev
            Assert.False(DataProtector.PareceCifrado("12345678909"));
            Assert.False(DataProtector.PareceCifrado("abc|def"));
            Assert.False(DataProtector.PareceCifrado(Convert.ToBase64String(new byte[8]) + "|" + Convert.ToBase64String(new byte[16]))); // IV curto
            Assert.False(DataProtector.PareceCifrado(Convert.ToBase64String(new byte[16]) + "|" + Convert.ToBase64String(new byte[10]))); // não é múltiplo de 16
        }

        [Fact]
        public void A_suite_roda_com_a_chave_propria_dos_testes()
        {
            // Garante que nenhum teste depende de DataProtector__Chave existir na máquina.
            var valor = DataProtector.Encrypt("ok");
            Assert.Equal("ok", DataProtector.Decrypt(valor, DataProtector.DerivarChave(ConfiguracaoDosTestes.ChaveDeDadosDeTeste)));
        }
    }
}
