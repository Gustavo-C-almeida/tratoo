using Microsoft.AspNetCore.Http;
using System.Net;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Normalização e fallbacks da fonte única de IP. Garante que o mesmo cliente
    /// produza sempre a mesma string — requisito tanto da chave de rate limiting
    /// quanto da comparação de registros de auditoria.
    /// </summary>
    public class ClientRequestInfoTests
    {
        private static HttpContext ContextoCom(IPAddress? endereco)
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = endereco;
            return http;
        }

        [Theory]
        [InlineData("203.0.113.45", "203.0.113.45")]
        [InlineData("::ffff:203.0.113.45", "203.0.113.45")]
        [InlineData("2001:db8::1", "2001:db8::1")]
        [InlineData("::1", "::1")]
        public void ObterIp_NormalizaEndereco(string entrada, string esperado)
        {
            Assert.Equal(esperado, ClientRequestInfo.ObterIp(ContextoCom(IPAddress.Parse(entrada))));
        }

        [Fact]
        public void Normalizar_RemoveScopeIdDeLinkLocal()
        {
            var comEscopo = IPAddress.Parse("fe80::1%12");

            Assert.Equal("fe80::1", ClientRequestInfo.Normalizar(comEscopo));
        }

        [Fact]
        public void Normalizar_NuncaExcedeOTamanhoDaColuna()
        {
            // Maior representação textual possível de um IPv6: 45 caracteres.
            var maior = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");

            Assert.True(ClientRequestInfo.Normalizar(maior).Length <= 45);
        }

        [Fact]
        public void ObterIp_SemConexao_UsaPadrao()
        {
            Assert.Equal("desconhecido", ClientRequestInfo.ObterIp(ContextoCom(null)));
            Assert.Equal("admin", ClientRequestInfo.ObterIp(ContextoCom(null), "admin"));
        }

        [Fact]
        public void ObterIpOuNulo_SemConexao_RetornaNulo()
        {
            Assert.Null(ClientRequestInfo.ObterIpOuNulo(ContextoCom(null)));
        }
    }
}
