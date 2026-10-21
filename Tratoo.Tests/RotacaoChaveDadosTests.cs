using System.Security.Cryptography;
using Tratoo.Domain.Features.Shared;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Rotação da chave de dados: durante a troca o processo grava com a chave NOVA e lê com
    /// a nova ou, se não decifrar, com a ANTERIOR. O risco a travar aqui é escolher a chave
    /// errada em silêncio — com a chave errada o AES-CBC "decifra" ~1 em 256 valores sem
    /// erro e devolve lixo, que a recifragem gravaria de volta como se fosse o dado.
    ///
    /// Tudo aqui usa chaves explícitas: o estado global do DataProtector (a chave da suíte)
    /// não é tocado, para não interferir nos outros testes.
    /// </summary>
    public class RotacaoChaveDadosTests
    {
        private const string TextoNova = "chave-nova-dos-testes-de-rotacao-0123";
        private const string TextoAnterior = "chave-anterior-dos-testes-de-rotacao-0123";

        private static readonly ChavesDeDados Rotacao = DataProtector.PrepararChaves(TextoNova, TextoAnterior);
        private static readonly byte[] Terceira = DataProtector.DerivarChave("uma-terceira-chave-qualquer-0123456");

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Sem_chave_anterior_nao_ha_rotacao(string? anterior)
        {
            var chaves = DataProtector.PrepararChaves(TextoNova, anterior);

            Assert.Null(chaves.Anterior);
            Assert.Equal(DataProtector.DerivarChave(TextoNova), chaves.Atual);
        }

        [Theory]
        [InlineData("   ")]
        [InlineData("DEFINA_UMA_CHAVE_ALEATORIA_COM_32_OU_MAIS_CARACTERES")]
        [InlineData(TextoNova)]                                   // igual à atual: variável copiada errado
        [InlineData(TextoNova + " sufixo depois do byte 32")]     // texto diferente, mesma chave derivada
        public void Chave_anterior_invalida_ou_igual_a_atual_e_recusada(string anterior)
        {
            var erro = Assert.Throws<InvalidOperationException>(() => DataProtector.PrepararChaves(TextoNova, anterior));
            Assert.Contains(DataProtector.VariavelDeAmbienteAnterior, erro.Message);
        }

        [Fact]
        public void Le_com_a_atual_e_com_a_anterior_e_recusa_a_terceira()
        {
            var daAnterior = DataProtector.Encrypt("12345678909", Rotacao.Anterior!);
            var daAtual = DataProtector.Encrypt("98765432100", Rotacao.Atual);
            var deOutra = DataProtector.Encrypt("11122233344", Terceira);

            Assert.Equal("12345678909", DataProtector.Decrypt(daAnterior, Rotacao.Atual, Rotacao.Anterior!));
            Assert.Equal("98765432100", DataProtector.Decrypt(daAtual, Rotacao.Atual, Rotacao.Anterior!));
            Assert.Throws<CryptographicException>(() => DataProtector.Decrypt(deOutra, Rotacao.Atual, Rotacao.Anterior!));
        }

        [Fact]
        public void Identifica_a_chave_de_cada_valor()
        {
            Assert.Equal(ChaveDoValor.Atual, DataProtector.Identificar(DataProtector.Encrypt("a@b.com", Rotacao.Atual), Rotacao));
            Assert.Equal(ChaveDoValor.Anterior, DataProtector.Identificar(DataProtector.Encrypt("a@b.com", Rotacao.Anterior!), Rotacao));
            Assert.Equal(ChaveDoValor.Nenhuma, DataProtector.Identificar(DataProtector.Encrypt("a@b.com", Terceira), Rotacao));

            Assert.Equal(ChaveDoValor.NaoCifrado, DataProtector.Identificar(null, Rotacao));
            Assert.Equal(ChaveDoValor.NaoCifrado, DataProtector.Identificar("", Rotacao));
            Assert.Equal(ChaveDoValor.NaoCifrado, DataProtector.Identificar("SEED_ENCRYPTED_CPF_PRESTADOR", Rotacao));

            // Fora de uma rotação, um valor da chave antiga é simplesmente ilegível.
            var semRotacao = DataProtector.PrepararChaves(TextoNova, null);
            Assert.Equal(ChaveDoValor.Nenhuma, DataProtector.Identificar(DataProtector.Encrypt("x", Rotacao.Anterior!), semRotacao));
        }

        [Fact]
        public void Padding_que_casa_por_acaso_nao_engana_a_identificacao()
        {
            // Milhares de valores curtos (como CPF e conta) cifrados com a ANTERIOR. Alguns
            // passam no padding com a chave atual — é o caso perigoso — e mesmo assim todos
            // têm de ser reconhecidos como da anterior e decifrar para o texto original.
            const int quantidade = 4000;
            var paddingCasouComAErrada = 0;

            for (var i = 0; i < quantidade; i++)
            {
                var claro = (10_000_000_000L + i * 7919L).ToString();
                var valor = DataProtector.Encrypt(claro, Rotacao.Anterior!);

                try { DataProtector.Decrypt(valor, Rotacao.Atual); paddingCasouComAErrada++; }
                catch (CryptographicException) { }

                Assert.Equal(ChaveDoValor.Anterior, DataProtector.Identificar(valor, Rotacao));
                Assert.Equal(claro, DataProtector.Decrypt(valor, Rotacao.Atual, Rotacao.Anterior!));
            }

            // Esperado ~16 (1/256). Prova de que o teste exercitou o caso de verdade.
            Assert.True(paddingCasouComAErrada > 0, "nenhum valor passou no padding com a chave errada; o teste não exercitou o caso");
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("12345678909", true)]
        [InlineData("fulano+pix@exemplo.com.br", true)]
        [InlineData("José da Silva — conta 12.345-6", true)]
        [InlineData("abc�def", false)]   // byte UTF-8 inválido
        [InlineData("abc\u0001def", false)]   // caractere de controle
        [InlineData("abc\ndef", false)]
        public void Texto_plausivel(string claro, bool esperado)
        {
            Assert.Equal(esperado, DataProtector.PareceTexto(claro));
        }
    }
}
