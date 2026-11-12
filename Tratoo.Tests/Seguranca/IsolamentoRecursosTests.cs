using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Fase 3 da auditoria — isolamento de recursos (IDOR/BOLA) e mass assignment.
    /// Os usuários B tentam ler, alterar e apagar o que é de A, com o MESMO papel de A
    /// (e reautenticados quando a rota pede), para que a recusa venha da checagem de dono.
    /// </summary>
    [Collection(ColecaoApiReal.Nome)]
    public sealed class IsolamentoRecursosTests
    {
        private readonly ApiRealFixture _f;
        public IsolamentoRecursosTests(ApiRealFixture f) => _f = f;

        public enum Ator { ContratanteB, PrestadorB }

        public sealed record Caso(string Nome, Ator Ator, string Metodo, Func<Massa, string> Url, Func<Massa, object?>? Corpo = null, bool Reautenticar = false)
        {
            public override string ToString() => Nome;
        }

        private static readonly Caso[] Casos =
        {
            // Contrato de A
            new("contrato: ler", Ator.ContratanteB, "GET", m => $"/api/contratos/{m.CenarioA.ContratoId}"),
            new("contrato: ler (prestador)", Ator.PrestadorB, "GET", m => $"/api/contratos/{m.CenarioA.ContratoId}"),
            new("contrato: PDF", Ator.ContratanteB, "GET", m => $"/api/contratos/{m.CenarioA.ContratoId}/pdf"),
            new("contrato: ver entrega", Ator.PrestadorB, "GET", m => $"/api/contratos/{m.CenarioA.ContratoId}/entrega"),
            new("contrato: pedir OTP de assinatura", Ator.ContratanteB, "POST", m => $"/api/contratos/{m.CenarioA.ContratoId}/assinar/solicitar-otp"),
            new("contrato: assinar", Ator.ContratanteB, "POST", m => $"/api/contratos/{m.CenarioA.ContratoId}/assinar", _ => new { confirmo = true, otp = "123456" }),
            new("contrato: cancelar", Ator.ContratanteB, "DELETE", m => $"/api/contratos/{m.CenarioA.ContratoId}?motivo=idor"),
            new("contrato: aprovar entrega", Ator.ContratanteB, "POST", m => $"/api/contratos/{m.CenarioA.ContratoId}/entrega/aprovar", Reautenticar: true),
            new("contrato: rejeitar entrega", Ator.ContratanteB, "POST", m => $"/api/contratos/{m.CenarioA.ContratoId}/entrega/rejeitar", _ => new { motivo = "tentativa de IDOR" }),
            new("contrato: avaliação pendente", Ator.ContratanteB, "GET", m => $"/api/contratos/{m.CenarioA.ContratoId}/avaliacoes/pendente"),

            // Pagamento de A
            new("pagamento: detalhe", Ator.ContratanteB, "GET", m => $"/api/pagamentos/{m.PagamentoA}"),
            new("pagamento: detalhe (prestador)", Ator.PrestadorB, "GET", m => $"/api/pagamentos/{m.PagamentoA}"),
            new("pagamento: PIX", Ator.ContratanteB, "GET", m => $"/api/pagamentos/{m.PagamentoA}/pix"),
            new("pagamento: sincronizar", Ator.ContratanteB, "GET", m => $"/api/pagamentos/{m.PagamentoA}/sincronizar"),
            new("pagamento: iniciar no contrato alheio", Ator.ContratanteB, "POST", _ => "/api/pagamentos/iniciar", m => new { contratoServicoId = m.CenarioA.ContratoId }),
            new("pagamento: liberar", Ator.ContratanteB, "POST", m => $"/api/pagamentos/{m.PagamentoA}/liberar", _ => new { observacaoContratante = "idor" }, Reautenticar: true),
            new("pagamento: estornar", Ator.ContratanteB, "POST", m => $"/api/pagamentos/{m.PagamentoA}/estornar"),
            new("pagamento: abrir disputa", Ator.ContratanteB, "POST", m => $"/api/pagamentos/{m.PagamentoA}/disputar", _ => new { motivo = "Tentativa de IDOR abrindo disputa alheia.", evidencias = new string[0] }),
            new("pagamento: abrir disputa (prestador)", Ator.PrestadorB, "POST", m => $"/api/pagamentos/{m.PagamentoA}/disputar", _ => new { motivo = "Tentativa de IDOR abrindo disputa alheia.", evidencias = new string[0] }),

            // Proposta de A
            new("proposta: ler", Ator.PrestadorB, "GET", m => $"/api/propostas/{m.CenarioA.PropostaId}"),
            new("proposta: ler (contratante)", Ator.ContratanteB, "GET", m => $"/api/propostas/{m.CenarioA.PropostaId}"),
            new("proposta: contraproposta", Ator.ContratanteB, "POST", m => $"/api/propostas/{m.CenarioA.PropostaId}/contraproposta",
                _ => new { objetivo = "idor", escopo = "idor", revisoesInclusas = 1, prazoTotal = DateTime.UtcNow.AddDays(10), valorTotal = 1m, formaPagamento = "PIX" }),
            new("proposta: aceitar", Ator.ContratanteB, "PUT", m => $"/api/propostas/{m.CenarioA.PropostaId}/aceitar"),
            new("proposta: recusar", Ator.ContratanteB, "PUT", m => $"/api/propostas/{m.CenarioA.PropostaId}/recusar", _ => new { motivo = "idor" }),
            new("proposta: excluir", Ator.PrestadorB, "DELETE", m => $"/api/propostas/{m.CenarioA.PropostaId}"),
            new("proposta: enviar", Ator.PrestadorB, "POST", m => $"/api/propostas/{m.CenarioA.PropostaId}/enviar"),

            // Projeto de A
            new("projeto: propostas recebidas", Ator.ContratanteB, "GET", m => $"/api/projects/{m.CenarioA.ProjetoId}/proposals"),
            new("projeto: publicar rascunho alheio", Ator.ContratanteB, "POST", m => $"/api/projects/{m.ProjetoRascunhoA}/publish"),
            new("projeto: cancelar", Ator.ContratanteB, "DELETE", m => $"/api/projects/{m.ProjetoPrivadoA}?motivo=idor"),
            new("projeto: convites (listar)", Ator.ContratanteB, "GET", m => $"/api/projects/{m.CenarioA.ProjetoId}/convites"),
            new("projeto: convites (criar)", Ator.ContratanteB, "POST", m => $"/api/projects/{m.ProjetoPrivadoA}/convites", m => new { prestadorId = m.PrestadorB.Id, mensagemInicial = "idor" }),
            new("projeto: recomendados (IA)", Ator.ContratanteB, "GET", m => $"/api/busca/projetos/{m.CenarioA.ProjetoId}/prestadores-recomendados"),
            new("chat: ler (prestador de fora)", Ator.PrestadorB, "GET", m => $"/api/projects/{m.CenarioA.ProjetoId}/mensagens"),
            new("chat: ler (contratante de fora)", Ator.ContratanteB, "GET", m => $"/api/projects/{m.CenarioA.ProjetoId}/mensagens?prestadorId={m.PrestadorA.Id}"),
            new("chat: escrever (prestador de fora)", Ator.PrestadorB, "POST", m => $"/api/projects/{m.CenarioA.ProjetoId}/mensagens", _ => new { texto = "mensagem IDOR" }),
            new("chat: escrever (contratante de fora)", Ator.ContratanteB, "POST", m => $"/api/projects/{m.CenarioA.ProjetoId}/mensagens", m => new { texto = "mensagem IDOR", prestadorId = m.PrestadorA.Id }),

            // Perfil de A
            new("competência: adicionar no perfil alheio", Ator.PrestadorB, "POST", m => $"/prestadores/{m.PrestadorA.Id}/competencias", _ => new { nome = "IDOR", nivel = "Básico" }),
        };

        public static IEnumerable<object[]> DadosCasos => Casos.Select(c => new object[] { c.Nome });

        [TheoryApiReal]
        [MemberData(nameof(DadosCasos))]
        public async Task UsuarioB_NaoAcessaRecursoDeA(string nome)
        {
            var c = Casos.Single(x => x.Nome == nome);
            var m = _f.Dados;
            var usuario = c.Ator == Ator.ContratanteB ? m.ContratanteB : m.PrestadorB;

            var s = c.Reautenticar ? await _f.LoginAsync(usuario) : await _f.SessaoAsync(usuario);
            if (c.Reautenticar) await _f.ReautenticarAsync(s);

            using var r = await _f.Api.EnviarAsync(new HttpMethod(c.Metodo), c.Url(m), s, c.Corpo?.Invoke(m));
            var corpo = await r.Content.ReadAsStringAsync();

            Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"{nome}: {(int)r.StatusCode} {corpo}");
            // Nenhum dado de A na resposta de erro.
            Assert.DoesNotContain(m.ContratanteA.Email, corpo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(m.PrestadorA.Email, corpo, StringComparison.OrdinalIgnoreCase);
        }

        [FactApiReal]
        public async Task DepoisDasTentativasDeB_RecursosDeA_ContinuamIntactos()
        {
            // Roda as tentativas mutáveis (idempotente: cada caso já é recusado) e confere o banco.
            foreach (var c in Casos.Where(x => x.Metodo != "GET"))
                await UsuarioB_NaoAcessaRecursoDeA(c.Nome);

            var m = _f.Dados;
            await using var db = _f.Db();
            var contrato = await db.ContratosServico.AsNoTracking().SingleAsync(x => x.Id == m.CenarioA.ContratoId);
            var pagamento = await db.Pagamentos.AsNoTracking().SingleAsync(x => x.Id == m.PagamentoA);
            var proposta = await db.PropostasProjeto.AsNoTracking().SingleAsync(x => x.Id == m.CenarioA.PropostaId);
            var rascunho = await db.Projetos.AsNoTracking().SingleAsync(x => x.Id == m.ProjetoRascunhoA);
            var privado = await db.Projetos.AsNoTracking().SingleAsync(x => x.Id == m.ProjetoPrivadoA);

            Assert.Equal(ContratoServicoStatus.Ativo, contrato.Status);
            Assert.Equal(StatusPagamento.Retido, pagamento.Status);
            Assert.Equal(StatusPropostaProjeto.Convertida, proposta.Status);
            Assert.Equal(StatusProjeto.Rascunho, rascunho.Status);
            Assert.Equal(StatusProjeto.Aberto, privado.Status);
            Assert.False(await db.MensagensProjeto.AnyAsync(x => x.ProjetoId == m.CenarioA.ProjetoId && x.Texto == "mensagem IDOR"));
            Assert.False(await db.DisputasPagamento.AnyAsync(x => x.PagamentoId == m.PagamentoA));
            Assert.False(await db.ConvitesProjeto.AnyAsync(x => x.ProjetoId == m.ProjetoPrivadoA));
        }

        [FactApiReal]
        public async Task UsuarioB_NaoEncerraSessaoDeA()
        {
            var a = await _f.LoginAsync(_f.Dados.ContratanteA);
            var b = await _f.SessaoAsync(_f.Dados.ContratanteB);

            using var r = await _f.Api.EnviarAsync(HttpMethod.Delete, $"/api/auth/sessoes/{a.Sid}", b);
            using var aindaValida = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/me", a);

            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal(HttpStatusCode.OK, aindaValida.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Mass assignment
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task AtualizarPerfil_CamposDePrivilegioSaoIgnorados()
        {
            var m = _f.Dados;
            var (contratante, prestador, _) = await _f.NovoParAsync();
            var extras = new Dictionary<string, object?>
            {
                ["id"] = m.ContratanteA.Id, ["contratanteId"] = m.ContratanteA.Id, ["prestadorId"] = m.PrestadorA.Id,
                ["isAdmin"] = true, ["IsAdmin"] = true, ["role"] = "Admin", ["tipoUsuario"] = "Admin",
                ["status"] = "Blocked", ["email"] = "sequestro@teste.local", ["senhaHash"] = "x", ["mfa"] = false,
                ["identidadeVerificada"] = true, ["descricao"] = "Descrição válida de teste de mass assignment."
            };

            var sc = await _f.LoginAsync(contratante);
            var sp = await _f.LoginAsync(prestador);
            using var rc = await _f.Api.EnviarAsync(HttpMethod.Put, "/contratantes/me/perfil", sc, extras);
            using var rp = await _f.Api.EnviarAsync(HttpMethod.Put, "/prestadores/me/perfil", sp, extras);
            Assert.True((int)rc.StatusCode < 500, $"contratante: {(int)rc.StatusCode}");
            Assert.True((int)rp.StatusCode < 500, $"prestador: {(int)rp.StatusCode}");

            await using var db = _f.Db();
            foreach (var (u, tipo) in new[] { (contratante, TipoUsuario.Contratante), (prestador, TipoUsuario.Prestador) })
            {
                var depois = await db.Usuarios.AsNoTracking().SingleAsync(x => x.Id == u.Id);
                Assert.False(depois.IsAdmin);
                Assert.Equal(tipo, depois.TipoUsuario);
                Assert.Equal(StatusUsuario.Active, depois.Status);
                Assert.Equal(u.Email, depois.Email);
                Assert.Equal(u.SenhaHash, depois.SenhaHash);
            }
            var a = await db.Usuarios.AsNoTracking().SingleAsync(x => x.Id == m.ContratanteA.Id);
            Assert.Equal(m.ContratanteA.Email, a.Email);
            Assert.Equal(StatusUsuario.Active, a.Status);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Achados
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Achado D2 (corrigido): o detalhe anônimo devolvia rascunhos e projetos privados.</summary>
        [FactApiReal]
        public async Task ProjetoRascunhoOuPrivado_NaoApareceParaTerceiros()
        {
            var m = _f.Dados;
            var b = await _f.SessaoAsync(m.PrestadorB);
            var vazou = new List<string>();
            foreach (var id in new[] { m.ProjetoRascunhoA, m.ProjetoPrivadoA })
            {
                using var anon = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/projects/{id}");
                using var outro = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/projects/{id}", b);
                foreach (var (quem, r) in new[] { ("anônimo", anon), ("prestador B", outro) })
                {
                    var corpo = await r.Content.ReadAsStringAsync();
                    if (r.IsSuccessStatusCode || corpo.Contains("SIGILOSO"))
                        vazou.Add($"projeto {id} para {quem}: {(int)r.StatusCode} {corpo[..Math.Min(160, corpo.Length)]}");
                }
            }
            Assert.True(vazou.Count == 0, string.Join("\n", vazou));
        }

        /// <summary>
        /// Contraparte do D2: quem participa do projeto continua vendo o detalhe, e o projeto
        /// público segue aberto — a correção não pode quebrar as telas do dono, do chat e do convite.
        /// </summary>
        [FactApiReal]
        public async Task ProjetoRascunhoOuPrivado_ContinuaVisivelParaQuemParticipa()
        {
            var m = _f.Dados;
            var (dono, convidado, cenario) = await _f.NovoParAsync();
            var (_, recusou, _) = await _f.NovoParAsync();
            var (_, comProposta, _) = await _f.NovoParAsync();

            int rascunho, privado;
            await using (var db = _f.Db())
            {
                var r = NovoProjeto(dono.Id, StatusProjeto.Rascunho, VisibilidadeProjeto.Publico);
                var p = NovoProjeto(dono.Id, StatusProjeto.Aberto, VisibilidadeProjeto.Privado);
                db.Projetos.AddRange(r, p);
                await db.SaveChangesAsync();
                db.ConvitesProjeto.AddRange(
                    new ConviteProjeto { ProjetoId = p.Id, ContratanteId = dono.Id, PrestadorId = convidado.Id, MensagemInicial = "convite", Status = StatusConvite.Pendente },
                    new ConviteProjeto { ProjetoId = p.Id, ContratanteId = dono.Id, PrestadorId = recusou.Id, MensagemInicial = "convite", Status = StatusConvite.Recusado });
                db.PropostasProjeto.Add(new PropostaProjeto { ProjetoId = p.Id, PrestadorId = comProposta.Id, Status = StatusPropostaProjeto.Recusada, VersaoAtual = 1, ValidoAte = DateTime.UtcNow.AddDays(5) });
                await db.SaveChangesAsync();
                (rascunho, privado) = (r.Id, p.Id);
            }

            async Task<HttpStatusCode> Ver(int id, Domain.Models.Usuario? u)
            {
                using var resp = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/projects/{id}", u is null ? null : await _f.SessaoAsync(u));
                return resp.StatusCode;
            }

            Assert.Equal(HttpStatusCode.OK, await Ver(rascunho, dono));
            Assert.Equal(HttpStatusCode.OK, await Ver(privado, dono));
            Assert.Equal(HttpStatusCode.OK, await Ver(privado, convidado));
            Assert.Equal(HttpStatusCode.OK, await Ver(privado, comProposta));
            Assert.Equal(HttpStatusCode.OK, await Ver(privado, m.Admin));
            Assert.Equal(HttpStatusCode.NotFound, await Ver(privado, recusou));     // convite recusado não dá acesso
            Assert.Equal(HttpStatusCode.NotFound, await Ver(rascunho, convidado));  // rascunho: só o dono
            Assert.Equal(HttpStatusCode.OK, await Ver(cenario.ProjetoId, null));    // público publicado: aberto
            Assert.Equal(HttpStatusCode.NotFound, await Ver(999_999, null));
        }

        private static Projeto NovoProjeto(int contratanteId, StatusProjeto status, VisibilidadeProjeto vis) => new()
        {
            ContratanteId = contratanteId,
            Titulo = $"Projeto de visibilidade {status}/{vis}",
            Descricao = "Projeto criado pelo teste de visibilidade do detalhe.",
            Categoria = CategoriaProjet.Design,
            OrcamentoMin = 500m, OrcamentoMax = 900m,
            PrazoEntrega = DateTime.UtcNow.AddDays(20),
            Status = status, Visibilidade = vis,
            Publicado = status != StatusProjeto.Rascunho,
            PublicadoEm = status != StatusProjeto.Rascunho ? DateTime.UtcNow : null
        };

        [FactAchado("D3")]
        public async Task Entrega_DeQuemNaoEOPrestador_NaoGravaArquivoNoBucketPrivado()
        {
            var m = _f.Dados;
            var gravados = new List<string>();
            foreach (var u in new[] { m.PrestadorB, m.ContratanteB })
            {
                var s = await _f.SessaoAsync(u);
                using var form = new MultipartFormDataContent
                {
                    { new StringContent("Entrega forjada por quem não é parte."), "descricaoEntrega" }
                };
                var arquivo = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 teste de seguranca"));
                arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                form.Add(arquivo, "files", "prova.pdf");

                var antes = _f.Externo.ObjetosGravados.Count;
                using var r = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{m.CenarioA.ContratoId}/entrega", s, conteudo: form);
                var novos = _f.Externo.ObjetosGravados.Skip(antes).Where(o => o.Contains($"entregas/{m.CenarioA.ContratoId}")).ToList();

                Assert.False(r.IsSuccessStatusCode, $"{u.TipoUsuario} B registrou entrega no contrato de A: {(int)r.StatusCode}");
                gravados.AddRange(novos.Select(o => $"{u.TipoUsuario} B → {o} (resposta {(int)r.StatusCode})"));
            }
            Assert.True(gravados.Count == 0, "Arquivos gravados no bucket privado antes da checagem de dono:\n" + string.Join("\n", gravados));
        }
        // ─────────────────────────────────────────────────────────────────────
        // D2 (revisão): proposta como porta de entrada e perfil público do dono
        // ─────────────────────────────────────────────────────────────────────

        private static object PropostaValida() => new
        {
            validoAte = DateTime.UtcNow.AddDays(10),
            objetivo = "Objetivo da proposta criada pelo teste de visibilidade de projeto.",
            escopo = "Escopo detalhado o suficiente para passar na validação mínima de cinquenta caracteres.",
            revisoesInclusas = 1, prazoTotal = DateTime.UtcNow.AddDays(20), valorTotal = 700m, formaPagamento = "PIX"
        };

        private async Task<(HttpStatusCode Status, string Mensagem)> CriarPropostaAsync(Sessao s, int projetoId)
        {
            using var r = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/propostas?projetoId={projetoId}", s, PropostaValida());
            var json = await ClienteApi.LerJsonAsync(r);
            var msg = json.ValueKind == System.Text.Json.JsonValueKind.Object && json.TryGetProperty("mensagem", out var m) ? m.GetString() ?? "" : "";
            return (r.StatusCode, msg);
        }

        /// <summary>
        /// Revisão do D2: criar proposta num projeto privado (sem convite) dava 201 com o título
        /// e, pela regra "proposta dá acesso", abria o detalhe. Inexistente, rascunho e privado
        /// sem convite precisam receber a MESMA resposta.
        /// </summary>
        [FactApiReal]
        public async Task PropostaEmProjetoPrivadoOuRascunho_SemConvite_NaoECriada_NemAbreODetalhe()
        {
            var (dono, _, _) = await _f.NovoParAsync();
            var (_, intruso, _) = await _f.NovoParAsync();
            int privado, rascunho;
            await using (var db = _f.Db())
            {
                var p = NovoProjeto(dono.Id, StatusProjeto.Aberto, VisibilidadeProjeto.Privado);
                var r = NovoProjeto(dono.Id, StatusProjeto.Rascunho, VisibilidadeProjeto.Publico);
                db.Projetos.AddRange(p, r);
                await db.SaveChangesAsync();
                (privado, rascunho) = (p.Id, r.Id);
            }
            var s = await _f.LoginAsync(intruso);

            var inexistente = await CriarPropostaAsync(s, 999_999);
            var emPrivado = await CriarPropostaAsync(s, privado);
            var emRascunho = await CriarPropostaAsync(s, rascunho);

            Assert.Equal(HttpStatusCode.BadRequest, inexistente.Status);
            Assert.Equal("Projeto não encontrado.", inexistente.Mensagem);
            Assert.Equal(inexistente, emPrivado);    // mesma resposta: não revela que o projeto existe
            Assert.Equal(inexistente, emRascunho);

            await using (var db = _f.Db())
                Assert.False(await db.PropostasProjeto.AnyAsync(x => x.PrestadorId == intruso.Id && (x.ProjetoId == privado || x.ProjetoId == rascunho)));
            using var detalhe = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/projects/{privado}", s);
            Assert.Equal(HttpStatusCode.NotFound, detalhe.StatusCode);
        }

        /// <summary>Contraparte: quem pode propor continua propondo.</summary>
        [FactApiReal]
        public async Task PropostaLegitima_EmProjetoPublico_OuPrivadoComConvite_ContinuaFuncionando()
        {
            var (dono, _, _) = await _f.NovoParAsync();
            var (_, convidado, _) = await _f.NovoParAsync();
            var (_, recusou, _) = await _f.NovoParAsync();
            var (_, qualquer, _) = await _f.NovoParAsync();
            int publico, privado;
            await using (var db = _f.Db())
            {
                var pub = NovoProjeto(dono.Id, StatusProjeto.Aberto, VisibilidadeProjeto.Publico);
                var pri = NovoProjeto(dono.Id, StatusProjeto.Aberto, VisibilidadeProjeto.Privado);
                db.Projetos.AddRange(pub, pri);
                await db.SaveChangesAsync();
                db.ConvitesProjeto.AddRange(
                    new ConviteProjeto { ProjetoId = pri.Id, ContratanteId = dono.Id, PrestadorId = convidado.Id, MensagemInicial = "convite", Status = StatusConvite.Aceito },
                    new ConviteProjeto { ProjetoId = pri.Id, ContratanteId = dono.Id, PrestadorId = recusou.Id, MensagemInicial = "convite", Status = StatusConvite.Recusado });
                await db.SaveChangesAsync();
                (publico, privado) = (pub.Id, pri.Id);
            }

            Assert.Equal(HttpStatusCode.Created, (await CriarPropostaAsync(await _f.LoginAsync(qualquer), publico)).Status);
            Assert.Equal(HttpStatusCode.Created, (await CriarPropostaAsync(await _f.LoginAsync(convidado), privado)).Status);
            Assert.Equal("Projeto não encontrado.", (await CriarPropostaAsync(await _f.LoginAsync(recusou), privado)).Mensagem);
        }

    }
}
