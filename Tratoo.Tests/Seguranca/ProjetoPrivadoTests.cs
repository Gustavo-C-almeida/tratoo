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
        // ─────────────────────────────────────────────────────────────────────
        // 2. Oráculos de existência
        // ─────────────────────────────────────────────────────────────────────

        private async Task<(int Status, string Corpo)> RespostaAsync(Sessao s, HttpMethod m, string url, object? corpo = null)
        {
            using var r = await _f.Api.EnviarAsync(m, url, s, corpo);
            return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
        }

        public static IEnumerable<object[]> RotasDeProjeto => new[]
        {
            new object[] { "chat: ler (prestador)", "prestador", "GET", "/api/projects/{id}/mensagens" },
            new object[] { "chat: ler (contratante)", "contratante", "GET", "/api/projects/{id}/mensagens?prestadorId=1" },
            new object[] { "chat: escrever (prestador)", "prestador", "POST", "/api/projects/{id}/mensagens" },
            new object[] { "convite: criar", "contratante", "POST", "/api/projects/{id}/convites" },
            new object[] { "convite: listar", "contratante", "GET", "/api/projects/{id}/convites" },
            new object[] { "projeto: publicar", "contratante", "POST", "/api/projects/{id}/publish" },
            new object[] { "projeto: cancelar", "contratante", "DELETE", "/api/projects/{id}" },
            new object[] { "projeto: listar propostas", "contratante", "GET", "/api/projects/{id}/proposals" },
        };

        /// <summary>
        /// Para quem não tem vínculo, projeto privado (aberto ou rascunho) de outro dono e projeto
        /// inexistente precisam produzir exatamente a mesma resposta: senão a rota revela que o
        /// projeto existe (e às vezes o status).
        /// </summary>
        [TheoryApiReal]
        [MemberData(nameof(RotasDeProjeto))]
        public async Task ProjetoAlheioPrivado_RespondeIgualAInexistente(string nome, string papel, string metodo, string modelo)
        {
            var (dono, _, _) = await _f.NovoParAsync();
            var (outroContratante, outroPrestador, _) = await _f.NovoParAsync();
            int privado, rascunho;
            await using (var db = _f.Db())
            {
                var p = NovoProjeto(dono.Id, VisibilidadeProjeto.Privado, "ORACULO-PRIVADO");
                var r = NovoProjeto(dono.Id, VisibilidadeProjeto.Privado, "ORACULO-RASCUNHO", StatusProjeto.Rascunho);
                db.Projetos.AddRange(p, r);
                await db.SaveChangesAsync();
                (privado, rascunho) = (p.Id, r.Id);
            }
            var s = await _f.LoginAsync(papel == "prestador" ? outroPrestador : outroContratante);
            object? corpo = metodo == "POST" && modelo.Contains("mensagens") ? new { texto = "Olá, tenho interesse." }
                          : metodo == "POST" && modelo.Contains("convites") ? new { prestadorId = outroPrestador.Id, mensagemInicial = "Mensagem de convite com tamanho suficiente." }
                          : null;

            var inexistente = await RespostaAsync(s, new HttpMethod(metodo), modelo.Replace("{id}", "999999"), corpo);
            var dePrivado = await RespostaAsync(s, new HttpMethod(metodo), modelo.Replace("{id}", privado.ToString()), corpo);
            var deRascunho = await RespostaAsync(s, new HttpMethod(metodo), modelo.Replace("{id}", rascunho.ToString()), corpo);

            Assert.True(inexistente.Status is 400 or 404, $"{nome}: inexistente respondeu {inexistente.Status}");
            Assert.True(inexistente == dePrivado, $"{nome}: inexistente {inexistente} × privado {dePrivado}");
            Assert.True(inexistente == deRascunho, $"{nome}: inexistente {inexistente} × rascunho {deRascunho}");
        }

        /// <summary>Contraparte: dono e participantes seguem usando chat, convites e propostas.</summary>
        [FactApiReal]
        public async Task DonoEParticipante_ContinuamUsandoAsRotasDoProjeto()
        {
            var (dono, prestador, cenario) = await _f.NovoParAsync();   // proposta convertida do prestador no projeto
            var (_, convidavel, _) = await _f.NovoParAsync();
            var sd = await _f.LoginAsync(dono);
            var sp = await _f.LoginAsync(prestador);
            int rascunho, privado;
            await using (var db = _f.Db())
            {
                var r = NovoProjeto(dono.Id, VisibilidadeProjeto.Publico, "DONO-RASCUNHO", StatusProjeto.Rascunho);
                var p = NovoProjeto(dono.Id, VisibilidadeProjeto.Privado, "DONO-PRIVADO");
                db.Projetos.AddRange(r, p);
                await db.SaveChangesAsync();
                (rascunho, privado) = (r.Id, p.Id);
            }

            Assert.Equal(201, (await RespostaAsync(sp, HttpMethod.Post, $"/api/projects/{cenario.ProjetoId}/mensagens", new { texto = "Mensagem do prestador." })).Status);
            Assert.Equal(200, (await RespostaAsync(sp, HttpMethod.Get, $"/api/projects/{cenario.ProjetoId}/mensagens")).Status);
            Assert.Equal(200, (await RespostaAsync(sd, HttpMethod.Get, $"/api/projects/{cenario.ProjetoId}/mensagens?prestadorId={prestador.Id}")).Status);
            Assert.Equal(200, (await RespostaAsync(sd, HttpMethod.Get, $"/api/projects/{cenario.ProjetoId}/proposals")).Status);
            Assert.Equal(201, (await RespostaAsync(sd, HttpMethod.Post, $"/api/projects/{privado}/convites",
                new { prestadorId = convidavel.Id, mensagemInicial = "Convite do dono com tamanho suficiente." })).Status);
            Assert.Equal(200, (await RespostaAsync(sd, HttpMethod.Get, $"/api/projects/{privado}/convites")).Status);
            // Publicar exige identidade verificada (a massa não tem): o dono passa da checagem de
            // propriedade e para na etapa seguinte — não recebe "Projeto não encontrado".
            var publicar = await RespostaAsync(sd, HttpMethod.Post, $"/api/projects/{rascunho}/publish");
            Assert.Contains("Valide seu CPF/CNPJ", publicar.Corpo);
            Assert.Equal(200, (await RespostaAsync(sd, HttpMethod.Delete, $"/api/projects/{privado}?motivo=teste")).Status);
        }
        // ─────────────────────────────────────────────────────────────────────
        // 3. Proposta não é vínculo com projeto privado
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Regra aprovada: em projeto privado, proposta sozinha (qualquer status) não dá acesso.
        /// Toda proposta legítima em privado convive com um convite; as que não têm só podem ter
        /// vindo do contorno antigo do D2 — e não podem abrir o detalhe, o chat nem ser enviadas.
        /// </summary>
        [FactApiReal]
        public async Task PropostaSemConvite_EmProjetoPrivado_NaoDaAcessoNemPodeSerEnviada()
        {
            var (dono, _, _) = await _f.NovoParAsync();
            var (_, comEnviada, _) = await _f.NovoParAsync();
            var (_, comRascunho, _) = await _f.NovoParAsync();
            int privado; Guid rascunho;
            await using (var db = _f.Db())
            {
                var p = NovoProjeto(dono.Id, VisibilidadeProjeto.Privado, "PROPOSTA-SEM-CONVITE");
                db.Projetos.Add(p);
                await db.SaveChangesAsync();
                privado = p.Id;
                var enviada = new PropostaProjeto { ProjetoId = p.Id, PrestadorId = comEnviada.Id, Status = StatusPropostaProjeto.Submitted, VersaoAtual = 1, ValidoAte = DateTime.UtcNow.AddDays(5) };
                var draft = new PropostaProjeto { ProjetoId = p.Id, PrestadorId = comRascunho.Id, Status = StatusPropostaProjeto.Draft, VersaoAtual = 1, ValidoAte = DateTime.UtcNow.AddDays(5) };
                db.PropostasProjeto.AddRange(enviada, draft);
                await db.SaveChangesAsync();
                rascunho = draft.Id;
            }

            var se = await _f.LoginAsync(comEnviada);
            var sr = await _f.LoginAsync(comRascunho);
            Assert.Equal(404, (await RespostaAsync(se, HttpMethod.Get, $"/api/projects/{privado}")).Status);
            Assert.Equal(404, (await RespostaAsync(se, HttpMethod.Get, $"/api/projects/{privado}/mensagens")).Status);
            Assert.Equal(404, (await RespostaAsync(sr, HttpMethod.Get, $"/api/projects/{privado}")).Status);

            var enviar = await RespostaAsync(sr, HttpMethod.Post, $"/api/propostas/{rascunho}/enviar");
            Assert.Equal((400, "{\"mensagem\":\"Projeto não encontrado.\"}"), enviar);
            await using var db2 = _f.Db();
            Assert.Equal(StatusPropostaProjeto.Draft, (await db2.PropostasProjeto.AsNoTracking().SingleAsync(x => x.Id == rascunho)).Status);
        }

        /// <summary>Contraparte: convite aceito, seleção e contrato continuam dando acesso.</summary>
        [FactApiReal]
        public async Task ConviteAceitoSelecaoOuContrato_ContinuamDandoAcessoAoProjetoPrivado()
        {
            var (dono, comContrato, _) = await _f.NovoParAsync();
            var (_, convidado, _) = await _f.NovoParAsync();
            var (privadoComContrato, _) = await ProjetoComContratoAsync(dono.Id, comContrato.Id, VisibilidadeProjeto.Privado, "VINCULO-CONTRATO",
                ContratoServicoStatus.Cancelado);
            int privado;
            await using (var db = _f.Db())
            {
                // Contrato cancelado limpa a seleção (ContratoServicoService): só o contrato prova o vínculo.
                await db.Projetos.Where(x => x.Id == privadoComContrato)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.FreelancerSelecionadoId, (int?)null).SetProperty(p => p.Status, StatusProjeto.Aberto));
                var p = NovoProjeto(dono.Id, VisibilidadeProjeto.Privado, "VINCULO-CONVITE");
                db.Projetos.Add(p);
                await db.SaveChangesAsync();
                db.ConvitesProjeto.Add(new ConviteProjeto { ProjetoId = p.Id, ContratanteId = dono.Id, PrestadorId = convidado.Id, MensagemInicial = "convite", Status = StatusConvite.Aceito, CriadoEm = DateTime.UtcNow.AddDays(-90), RespondidoEm = DateTime.UtcNow.AddDays(-89) });
                await db.SaveChangesAsync();
                privado = p.Id;
            }

            Assert.Equal(200, (await RespostaAsync(await _f.LoginAsync(comContrato), HttpMethod.Get, $"/api/projects/{privadoComContrato}")).Status);
            Assert.Equal(200, (await RespostaAsync(await _f.LoginAsync(convidado), HttpMethod.Get, $"/api/projects/{privado}")).Status);
        }
        // ─────────────────────────────────────────────────────────────────────
        // 4. Expiração do convite pendente (30 dias ou projeto fora de Aberto)
        // ─────────────────────────────────────────────────────────────────────

        private async Task<(int ProjetoId, Guid ConviteId)> ConvitePendenteAsync(int dono, int prestador, DateTime criadoEm,
            StatusProjeto statusProjeto = StatusProjeto.Aberto)
        {
            await using var db = _f.Db();
            var p = NovoProjeto(dono, VisibilidadeProjeto.Privado, "CONVITE-EXPIRA", statusProjeto);
            db.Projetos.Add(p);
            await db.SaveChangesAsync();
            var c = new ConviteProjeto { ProjetoId = p.Id, ContratanteId = dono, PrestadorId = prestador, MensagemInicial = "Convite para o teste de expiração.", Status = StatusConvite.Pendente, CriadoEm = criadoEm };
            db.ConvitesProjeto.Add(c);
            await db.SaveChangesAsync();
            return (p.Id, c.Id);
        }

        private async Task<string?> StatusNaListaDoPrestadorAsync(Sessao s, Guid conviteId)
        {
            using var r = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/me/convites", s);
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.EnumerateArray()
                .Single(c => c.GetProperty("id").GetGuid() == conviteId).GetProperty("status").GetString();
        }

        /// <summary>Convite pendente há mais de 30 dias: não dá acesso, não aceita, aparece como Expirado.</summary>
        [FactApiReal]
        public async Task ConvitePendenteHaMaisDe30Dias_Expira()
        {
            var (dono, prestador, _) = await _f.NovoParAsync();
            var (projeto, convite) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-31));
            var sp = await _f.LoginAsync(prestador);

            Assert.Equal(404, (await RespostaAsync(sp, HttpMethod.Get, $"/api/projects/{projeto}")).Status);
            Assert.Equal("Expirado", await StatusNaListaDoPrestadorAsync(sp, convite));
            var aceitar = await RespostaAsync(sp, HttpMethod.Put, $"/api/convites/{convite}/aceitar");
            Assert.Equal((400, "{\"mensagem\":\"Este convite expirou.\"}"), aceitar);
            await using (var db = _f.Db())
                Assert.Equal(StatusConvite.Expirado, (await db.ConvitesProjeto.AsNoTracking().SingleAsync(c => c.Id == convite)).Status);

            // O dono pode convidar de novo: o convite vencido não conta como "pendente".
            var sd = await _f.LoginAsync(dono);
            Assert.Equal(201, (await RespostaAsync(sd, HttpMethod.Post, $"/api/projects/{projeto}/convites",
                new { prestadorId = prestador.Id, mensagemInicial = "Novo convite depois da expiração." })).Status);
        }

        /// <summary>Convite pendente recente, mas o projeto já não está Aberto: expira.</summary>
        [FactApiReal]
        public async Task ConvitePendente_DeProjetoQueSaiuDeAberto_Expira()
        {
            var (dono, prestador, _) = await _f.NovoParAsync();
            var (projeto, convite) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-1), StatusProjeto.EmAndamento);
            var sp = await _f.LoginAsync(prestador);

            Assert.Equal(404, (await RespostaAsync(sp, HttpMethod.Get, $"/api/projects/{projeto}")).Status);
            Assert.Equal("Expirado", await StatusNaListaDoPrestadorAsync(sp, convite));
            Assert.Equal((400, "{\"mensagem\":\"Este convite expirou.\"}"),
                await RespostaAsync(sp, HttpMethod.Put, $"/api/convites/{convite}/recusar", new { motivo = "sem interesse" }));
        }

        /// <summary>Linha de base: convite pendente dentro do prazo, em projeto aberto, segue valendo.</summary>
        [FactApiReal]
        public async Task ConvitePendenteDentroDoPrazo_ContinuaValendo()
        {
            var (dono, prestador, _) = await _f.NovoParAsync();
            var (projeto, convite) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-29));
            var sp = await _f.LoginAsync(prestador);

            Assert.Equal(200, (await RespostaAsync(sp, HttpMethod.Get, $"/api/projects/{projeto}")).Status);
            Assert.Equal("Pendente", await StatusNaListaDoPrestadorAsync(sp, convite));
            Assert.Equal(200, (await RespostaAsync(sp, HttpMethod.Put, $"/api/convites/{convite}/aceitar")).Status);
        }
        /// <summary>
        /// O job de expiração (de hora em hora, nas duas réplicas) marca só os pendentes vencidos,
        /// e duas execuções simultâneas não conflitam (UPDATE condicional único).
        /// </summary>
        [FactApiReal]
        public async Task JobDeExpiracao_MarcaSoOsVencidos_ESuportaReplicasSimultaneas()
        {
            var (dono, prestador, _) = await _f.NovoParAsync();
            var (_, vencidoPrazo) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-31));
            var (_, vencidoProjeto) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-1), StatusProjeto.Cancelado);
            var (_, vigente) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-29));
            var (projetoAceito, aceito) = await ConvitePendenteAsync(dono.Id, prestador.Id, DateTime.UtcNow.AddDays(-90));
            await using (var db = _f.Db())
                await db.ConvitesProjeto.Where(c => c.Id == aceito).ExecuteUpdateAsync(c => c.SetProperty(x => x.Status, StatusConvite.Aceito));

            var agora = DateTime.UtcNow;
            await using var db1 = _f.Db();
            await using var db2 = _f.Db();
            var marcados = await Task.WhenAll(
                new Tratoo.Domain.Features.Mensagens.ConviteProjetoRepository(db1).ExpirarVencidosAsync(agora),
                new Tratoo.Domain.Features.Mensagens.ConviteProjetoRepository(db2).ExpirarVencidosAsync(agora));
            Assert.True(marcados.Sum() >= 2, $"marcados: {string.Join("+", marcados)}");

            await using var db3 = _f.Db();
            var status = await db3.ConvitesProjeto.AsNoTracking()
                .Where(c => c.Id == vencidoPrazo || c.Id == vencidoProjeto || c.Id == vigente || c.Id == aceito)
                .ToDictionaryAsync(c => c.Id, c => c.Status);
            Assert.Equal(StatusConvite.Expirado, status[vencidoPrazo]);
            Assert.Equal(StatusConvite.Expirado, status[vencidoProjeto]);
            Assert.Equal(StatusConvite.Pendente, status[vigente]);
            Assert.Equal(StatusConvite.Aceito, status[aceito]);
        }
        // ─────────────────────────────────────────────────────────────────────
        // 5. Métricas do perfil público do contratante
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// As métricas do perfil público (anônimo) contavam projetos e contratos de projetos
        /// privados. Só projeto público entra em contagens, valor médio e tempo de decisão.
        /// Massa: projeto do cenário (público, EmAndamento, 1200/1200, contrato Ativo) +
        /// público Aberto (1000/3000) + público Concluído com contrato Encerrado (1000/3000) +
        /// privado Aberto + privado Concluído com contrato Encerrado.
        /// </summary>
        [FactApiReal]
        public async Task MetricasDoPerfilPublico_NaoContamProjetosPrivados()
        {
            var (dono, prestador, _) = await _f.NovoParAsync();
            await ProjetoComContratoAsync(dono.Id, prestador.Id, VisibilidadeProjeto.Publico, "METRICA-PUB-CONCLUIDO");
            await ProjetoComContratoAsync(dono.Id, prestador.Id, VisibilidadeProjeto.Privado, "METRICA-PRIV-CONCLUIDO");
            await using (var db = _f.Db())
            {
                db.Projetos.AddRange(
                    NovoProjeto(dono.Id, VisibilidadeProjeto.Publico, "METRICA-PUB-ABERTO"),
                    NovoProjeto(dono.Id, VisibilidadeProjeto.Privado, "METRICA-PRIV-ABERTO"));
                await db.SaveChangesAsync();
            }

            using var r = await _f.Api.EnviarAsync(HttpMethod.Get, $"/contratantes/{dono.Id}/perfil");
            var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            Assert.Equal(3, j.GetProperty("totalProjetosPublicados").GetInt32());
            Assert.Equal(1, j.GetProperty("totalProjetosConcluidos").GetInt32());
            Assert.Equal(1, j.GetProperty("totalProjetosAtivos").GetInt32());
            Assert.Equal(1, j.GetProperty("totalContratosConcluidoss").GetInt32());
            Assert.Equal(1733.33m, Math.Round(j.GetProperty("valorMedioProjetos").GetDecimal(), 2));
        }
    }
}
