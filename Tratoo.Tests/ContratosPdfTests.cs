using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuestPDF.Infrastructure;
using Tratoo.Domain.Features.Contratos;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Gera o PDF do contrato DE VERDADE com o <see cref="ContratosPdfService"/>. Os demais testes
    /// usam um falso do serviço, e nada exercitava o QuestPDF: foi assim que o QuestPDF 2026.9
    /// passaria na CI e quebraria em produção. A partir dessa versão, uma família de fonte que não
    /// está disponível (como "Arial" na imagem Ubuntu da aspnet:10.0) interrompe a geração com
    /// <c>DocumentDrawingException</c>, em vez de ser trocada em silêncio pela Lato, como fazia a
    /// 2025.1 — os contratos de produção sempre saíram em Lato.
    /// </summary>
    public class ContratosPdfTests
    {
        public ContratosPdfTests()
        {
            // Igual ao Program.cs da API: sem isto o QuestPDF recusa gerar o documento.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private static (ContratoServico Contrato, ContratoSnapshot Snapshot) ContratoDeExemplo()
        {
            var conteudo = new ContratoConteudoDto
            {
                Contratante = new ContratoParteDto
                {
                    Nome = "Ana Conceição Araújo",
                    CpfCnpjMascarado = "***.456.789-**",
                    Email = "ana@example.com",
                    Endereco = "Rua das Flores, 123 — São Paulo/SP"
                },
                Prestador = new ContratoParteDto
                {
                    Nome = "João Ferreira Gonçalves",
                    CpfCnpjMascarado = "***.654.321-**",
                    Email = "joao@example.com",
                    Endereco = "Av. Paulista, 1000 — São Paulo/SP"
                },
                Objeto = "Desenvolvimento de site institucional com formulário de contato.",
                Escopo = new ContratoEscopoDto
                {
                    Entregaveis = "Layout responsivo; 5 páginas; formulário",
                    Revisoes = 2,
                    FormatoEntrega = "Código-fonte e publicação",
                    OQueNaoEstaIncluso = "Hospedagem e domínio"
                },
                Prazo = new ContratoPrazoDto
                {
                    DataInicio = new DateTime(2026, 11, 10),
                    DataTermino = new DateTime(2026, 12, 20),
                    Marcos =
                    {
                        new ContratoMarcoDto { Descricao = "Protótipo aprovado", Prazo = new DateTime(2026, 11, 25), Valor = 1500m },
                        new ContratoMarcoDto { Descricao = "Entrega final", Prazo = new DateTime(2026, 12, 20), Valor = 3500m }
                    }
                },
                Pagamento = new ContratoPagamentoDto { ValorTotal = 5000m, Entrada = 1500m }
            };

            // Assinado pelas duas partes e com hash: o PDF inclui o bloco de integridade, que usa
            // uma fonte própria na linha do hash.
            var contrato = new ContratoServico
            {
                AssinadoContratanteEm = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc),
                IpContratante = "203.0.113.10",
                AssinadoPrestadorEm = new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc),
                IpPrestador = "198.51.100.20",
                ConteudoHash = new string('a', 64)
            };
            var snapshot = new ContratoSnapshot
            {
                ConteudoFinal = JsonSerializer.Serialize(conteudo),
                CongeladoEm = new DateTime(2026, 11, 10, 15, 5, 0, DateTimeKind.Utc)
            };
            return (contrato, snapshot);
        }

        [Fact]
        public async Task GerarAsync_ProduzPdfValidoComConteudo()
        {
            var (contrato, snapshot) = ContratoDeExemplo();

            using var pdf = await new ContratosPdfService().GerarAsync(contrato, snapshot);
            var bytes = pdf.ToArray();

            Assert.Equal(0, pdf.Position); // pronto para ser enviado como resposta
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.True(bytes.Length > 10_000, $"PDF pequeno demais ({bytes.Length} bytes): provavelmente vazio.");
        }

        [Fact]
        public async Task GerarAsync_EmbuteSoAFonteLato()
        {
            var (contrato, snapshot) = ContratoDeExemplo();

            using var pdf = await new ContratosPdfService().GerarAsync(contrato, snapshot);
            var texto = Encoding.Latin1.GetString(pdf.ToArray());

            // Nomes das fontes embutidas, sem o prefixo de subconjunto ("AAAAAA+Lato-Bold" → "Lato-Bold").
            var fontes = Regex.Matches(texto, @"/BaseFont\s*/(?:[A-Z]{6}\+)?([A-Za-z0-9-]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(fontes);
            Assert.All(fontes, f => Assert.StartsWith("Lato", f));
        }
    }
}
