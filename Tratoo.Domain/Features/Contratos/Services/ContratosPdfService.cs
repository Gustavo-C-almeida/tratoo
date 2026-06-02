using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public class ContratosPdfService : IContratosPdfService
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public Task<MemoryStream> GerarAsync(ContratoServico contrato, ContratoSnapshot snapshot)
        {
            var conteudo = JsonSerializer.Deserialize<ContratoConteudoDto>(snapshot.ConteudoFinal, _jsonOpts)
                           ?? new ContratoConteudoDto();

            var ms = new MemoryStream();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(c => CabecalhoDoc(c, contrato));
                    page.Content().Element(c => CorpoDoc(c, conteudo, contrato, snapshot));
                    page.Footer().Element(RodapeDoc);
                });
            }).GeneratePdf(ms);

            ms.Position = 0;
            return Task.FromResult(ms);
        }

        // ── Cabeçalho ─────────────────────────────────────────────────────────────

        private static void CabecalhoDoc(IContainer container, ContratoServico contrato)
        {
            container.Column(col =>
            {
                col.Item().AlignCenter().Text("CONTRATO DE PRESTAÇÃO DE SERVIÇOS")
                    .FontSize(14).Bold().FontColor(Colors.Grey.Darken3);

                col.Item().AlignCenter().Text($"Contrato nº {contrato.Id.ToString().ToUpper()[..8]}")
                    .FontSize(9).FontColor(Colors.Grey.Medium);

                col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                col.Item().PaddingBottom(8);
            });
        }

        // ── Corpo ─────────────────────────────────────────────────────────────────

        private static void CorpoDoc(
            IContainer container,
            ContratoConteudoDto c,
            ContratoServico contrato,
            ContratoSnapshot snapshot)
        {
            container.Column(col =>
            {
                // 1. PARTES
                col.Item().Element(e => Secao(e, "1. PARTES"));
                col.Item().PaddingLeft(8).Column(partes =>
                {
                    partes.Item().Element(e => Parte(e, "CONTRATANTE", c.Contratante));
                    partes.Item().PaddingTop(6).Element(e => Parte(e, "PRESTADOR", c.Prestador));
                });

                // 2. OBJETO
                col.Item().PaddingTop(12).Element(e => Secao(e, "2. OBJETO"));
                col.Item().PaddingLeft(8).Text(c.Objeto);

                // 3. ESCOPO
                col.Item().PaddingTop(12).Element(e => Secao(e, "3. ESCOPO"));
                col.Item().PaddingLeft(8).Column(escopo =>
                {
                    escopo.Item().Element(e => Campo(e, "Entregáveis", c.Escopo.Entregaveis));
                    escopo.Item().Element(e => Campo(e, "Revisões incluídas", c.Escopo.Revisoes.ToString()));
                    escopo.Item().Element(e => Campo(e, "Formato de entrega", c.Escopo.FormatoEntrega));
                    escopo.Item().Element(e => Campo(e, "Não incluído", c.Escopo.OQueNaoEstaIncluso));
                });

                // 4. PRAZO
                col.Item().PaddingTop(12).Element(e => Secao(e, "4. PRAZO"));
                col.Item().PaddingLeft(8).Column(prazo =>
                {
                    prazo.Item().Element(e => Campo(e, "Início", c.Prazo.DataInicio.ToString("dd/MM/yyyy")));
                    prazo.Item().Element(e => Campo(e, "Término", c.Prazo.DataTermino.ToString("dd/MM/yyyy")));

                    if (c.Prazo.Marcos?.Count > 0)
                    {
                        prazo.Item().PaddingTop(4).Text("Marcos do projeto:").Bold();
                        foreach (var marco in c.Prazo.Marcos)
                        {
                            prazo.Item().PaddingLeft(8).Text(
                                $"• {marco.Descricao} — {marco.Prazo:dd/MM/yyyy}" +
                                (marco.Valor > 0 ? $" — R$ {marco.Valor:N2}" : ""));
                        }
                    }
                });

                // 5. PAGAMENTO
                col.Item().PaddingTop(12).Element(e => Secao(e, "5. PAGAMENTO"));
                col.Item().PaddingLeft(8).Column(pag =>
                {
                    pag.Item().Element(e => Campo(e, "Valor total", $"R$ {c.Pagamento.ValorTotal:N2}"));
                    pag.Item().Element(e => Campo(e, "Entrada", $"R$ {c.Pagamento.Entrada:N2}"));
                    pag.Item().Element(e => Campo(e, "Forma de pagamento", c.Pagamento.FormaPagamento));
                    pag.Item().Element(e => Campo(e, "Multa por atraso", c.Pagamento.MultaAtraso));
                });

                // 6. DIREITOS AUTORAIS
                col.Item().PaddingTop(12).Element(e => Secao(e, "6. DIREITOS AUTORAIS"));
                col.Item().PaddingLeft(8).Text(c.DireitosAutorais);

                // 7. CONFIDENCIALIDADE
                col.Item().PaddingTop(12).Element(e => Secao(e, "7. CONFIDENCIALIDADE"));
                col.Item().PaddingLeft(8).Text(c.Confidencialidade);

                // 8. CANCELAMENTO
                col.Item().PaddingTop(12).Element(e => Secao(e, "8. CANCELAMENTO"));
                col.Item().PaddingLeft(8).Text(c.Cancelamento);

                // 9. FORO
                col.Item().PaddingTop(12).Element(e => Secao(e, "9. FORO"));
                col.Item().PaddingLeft(8).Text($"Fica eleito o Foro da {c.Foro} para dirimir quaisquer litígios.");

                // 10. ASSINATURAS
                col.Item().PaddingTop(20).Element(e => Secao(e, "10. ASSINATURAS DIGITAIS"));
                col.Item().PaddingLeft(8).Column(assin =>
                {
                    assin.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                        });

                        table.Cell().Element(CelulaAssin).Column(col2 =>
                        {
                            col2.Item().Text("CONTRATANTE").Bold();
                            col2.Item().PaddingTop(4).Text(c.Contratante.Nome);
                            col2.Item().Text($"Data: {contrato.AssinadoContratanteEm?.ToLocalTime():dd/MM/yyyy HH:mm} (UTC-3)").FontSize(8);
                            col2.Item().Text($"IP: {contrato.IpContratante ?? "-"}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        table.Cell().Element(CelulaAssin).Column(col2 =>
                        {
                            col2.Item().Text("PRESTADOR").Bold();
                            col2.Item().PaddingTop(4).Text(c.Prestador.Nome);
                            col2.Item().Text($"Data: {contrato.AssinadoPrestadorEm?.ToLocalTime():dd/MM/yyyy HH:mm} (UTC-3)").FontSize(8);
                            col2.Item().Text($"IP: {contrato.IpPrestador ?? "-"}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                });

                // 11. INTEGRIDADE
                col.Item().PaddingTop(16).Background(Colors.Grey.Lighten3).Padding(8).Column(integ =>
                {
                    integ.Item().Text("CERTIFICADO DE INTEGRIDADE DIGITAL").Bold().FontSize(9);
                    integ.Item().PaddingTop(4).Text($"Hash SHA-256: {contrato.ConteudoHash}").FontSize(7).FontFamily("Courier New");
                    integ.Item().PaddingTop(2).Text(
                        $"Documento gerado em: {snapshot.CongeladoEm:dd/MM/yyyy HH:mm} UTC  |  " +
                        $"Contrato ID: {contrato.Id}")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                    integ.Item().PaddingTop(2).Text(
                        "Assinado digitalmente conforme a Medida Provisória nº 2.200-2/2001 e o Marco Civil da Internet (Lei nº 12.965/2014).")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }

        // ── Rodapé ────────────────────────────────────────────────────────────────

        private static void RodapeDoc(IContainer container)
        {
            container.AlignCenter().Text(text =>
            {
                text.Span("Tratoo — Plataforma de Contratação de Serviços  |  Página ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(" de ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        }

        // ── Helpers de layout ──────────────────────────────────────────────────────

        private static void Secao(IContainer container, string titulo)
        {
            container.Column(col =>
            {
                col.Item().Text(titulo).Bold().FontSize(11).FontColor(Colors.Blue.Darken3);
                col.Item().LineHorizontal(0.5f).LineColor(Colors.Blue.Lighten3);
                col.Item().PaddingBottom(4);
            });
        }

        private static void Parte(IContainer container, string papel, ContratoParteDto parte)
        {
            container.Column(col =>
            {
                col.Item().Text(papel).Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                col.Item().Text($"Nome: {parte.Nome}");
                col.Item().Text($"CPF/CNPJ: {parte.CpfCnpjMascarado}");
                col.Item().Text($"E-mail: {parte.Email}");
                col.Item().Text($"Endereço: {parte.Endereco}");
            });
        }

        private static void Campo(IContainer container, string label, string valor)
        {
            container.Row(row =>
            {
                row.AutoItem().Text($"{label}: ").Bold();
                row.RelativeItem().Text(valor);
            });
        }

        private static IContainer CelulaAssin(IContainer container)
            => container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8);
    }
}
