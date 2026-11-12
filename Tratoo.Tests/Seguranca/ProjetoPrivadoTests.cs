using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Encerramento do achado D2 (projetos privados): o que ainda revelava ou dava acesso a
    /// projeto privado depois das correções do detalhe, da criação de proposta e do perfil.
    /// </summary>
    [Collection(ColecaoApiReal.Nome)]
    public sealed class ProjetoPrivadoTests
    {
        private readonly ApiRealFixture _f;
        public ProjetoPrivadoTests(ApiRealFixture f) => _f = f;

        // ─────────────────────────────────────────────────────────────────────
        // Massa
        // ─────────────────────────────────────────────────────────────────────

        private static Projeto NovoProjeto(int contratanteId, VisibilidadeProjeto vis, string marcador,
            StatusProjeto status = StatusProjeto.Aberto) => new()
        {
            ContratanteId = contratanteId,
            Titulo = $"Projeto {marcador}",
            Descricao = $"Descrição {marcador} usada pelos testes de projeto privado.",
            Categoria = CategoriaProjet.Design,
            OrcamentoMin = 1000m, OrcamentoMax = 3000m,
            PrazoEntrega = DateTime.UtcNow.AddDays(20),
            Status = status, Visibilidade = vis,
            Publicado = status != StatusProjeto.Rascunho,
            PublicadoEm = status != StatusProjeto.Rascunho ? DateTime.UtcNow.AddDays(-3) : null
        };

        /// <summary>Projeto + proposta convertida + contrato entre o par informado.</summary>
        private async Task<(int ProjetoId, Guid ContratoId)> ProjetoComContratoAsync(
            int contratanteId, int prestadorId, VisibilidadeProjeto vis, string marcador,
            ContratoServicoStatus statusContrato = ContratoServicoStatus.Encerrado)
        {
            await using var db = _f.Db();
            var projeto = NovoProjeto(contratanteId, vis, marcador, StatusProjeto.Concluido);
            projeto.FreelancerSelecionadoId = prestadorId;
            db.Projetos.Add(projeto);
            await db.SaveChangesAsync();
            var proposta = new PropostaProjeto { ProjetoId = projeto.Id, PrestadorId = prestadorId, Status = StatusPropostaProjeto.Convertida, VersaoAtual = 1, ValidoAte = DateTime.UtcNow.AddDays(5) };
            db.PropostasProjeto.Add(proposta);
            await db.SaveChangesAsync();
            var contrato = new ContratoServico
            {
                ProjetoId = projeto.Id, PropostaId = proposta.Id, ContratanteId = contratanteId, PrestadorId = prestadorId,
                Status = statusContrato, ConteudoJson = "{}", ConteudoHash = "HASH_" + marcador,
                AssinadoContratanteEm = DateTime.UtcNow.AddDays(-2), AssinadoPrestadorEm = DateTime.UtcNow.AddDays(-2),
                IpContratante = "127.0.0.1", IpPrestador = "127.0.0.1", CriadoEm = DateTime.UtcNow.AddDays(-2), ExpiraEm = DateTime.UtcNow.AddDays(60)
            };
            db.ContratosServico.Add(contrato);
            await db.SaveChangesAsync();
            return (projeto.Id, contrato.Id);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 1. Avaliações públicas
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// GET /api/usuarios/{id}/avaliacoes (anônimo) trazia título e ID do projeto de cada
        /// avaliação, inclusive de projetos privados. A avaliação continua pública; o projeto
        /// privado não é identificado.
        /// </summary>
        [FactApiReal]
        public async Task AvaliacaoPublica_DeProjetoPrivado_NaoExpoeTituloNemIdDoProjeto()
        {
            var (contratante, prestador, _) = await _f.NovoParAsync();
            var (publico, contratoPublico) = await ProjetoComContratoAsync(contratante.Id, prestador.Id, VisibilidadeProjeto.Publico, "AVAL-PUBLICO-OK");
            var (privado, contratoPrivado) = await ProjetoComContratoAsync(contratante.Id, prestador.Id, VisibilidadeProjeto.Privado, "AVAL-PRIVADO-SIGILOSO");

            await using (var db = _f.Db())
            {
                foreach (var (contrato, comentario, quando) in new[] { (contratoPublico, "Comentário no projeto público.", -2), (contratoPrivado, "Comentário no projeto privado.", -1) })
                    db.Avaliacoes.Add(new Avaliacao
                    {
                        Id = Guid.NewGuid(), ContratoServicoId = contrato, AvaliadorId = contratante.Id, AvaliadoId = prestador.Id,
                        Nota = 5, Comentario = comentario, Publica = true, Status = StatusAvaliacao.Publicada,
                        PublicadaEm = DateTime.UtcNow.AddHours(quando), CriadoEm = DateTime.UtcNow.AddDays(-1)
                    });
                await db.SaveChangesAsync();
            }

            using var r = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/usuarios/{prestador.Id}/avaliacoes?pagina=1&tamanho=10");
            var corpo = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var avaliacoes = JsonDocument.Parse(corpo).RootElement.GetProperty("avaliacoes").EnumerateArray().ToList();
            var dePublico = avaliacoes.Single(a => a.GetProperty("comentario").GetString()!.Contains("público"));
            var dePrivado = avaliacoes.Single(a => a.GetProperty("comentario").GetString()!.Contains("privado"));

            Assert.Equal(publico, dePublico.GetProperty("projetoId").GetInt32());
            Assert.Contains("AVAL-PUBLICO-OK", dePublico.GetProperty("projetoTitulo").GetString());
            Assert.Equal(0, dePrivado.GetProperty("projetoId").GetInt32());
            Assert.Equal("", dePrivado.GetProperty("projetoTitulo").GetString());
            Assert.DoesNotContain("AVAL-PRIVADO-SIGILOSO", corpo);
            Assert.DoesNotContain($"\"projetoId\":{privado},", corpo);
        }
    }
}
