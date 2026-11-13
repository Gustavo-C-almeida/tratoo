using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// D3 — fluxo de entrega contra a API real (duas réplicas, Postgres, Redis): concorrência nas
    /// transições do contrato, coordenação com a liberação do pagamento, limpeza do R2, links e
    /// limite de upload. Os testes de concorrência fazem várias rodadas: sem transição atômica, a
    /// corrida aparece em parte delas; com ela, a condição verificada vale em todas.
    /// </summary>
    [Collection(ColecaoApiReal.Nome)]
    public sealed class EntregaFluxoTests
    {
        private const long MiB = 1024 * 1024;
        private readonly ApiRealFixture _f;
        public EntregaFluxoTests(ApiRealFixture f) => _f = f;

        // ─────────────────────────────────────────────────────────────────────
        // Massa e utilitários
        // ─────────────────────────────────────────────────────────────────────

        private sealed record Contrato(Usuario Contratante, Usuario Prestador, Guid Id, Guid PagamentoId);

        /// <summary>Contrato Ativo com pagamento Retido (prazo de liberação automática daqui a 30 dias).</summary>
        private async Task<Contrato> ContratoPagoAsync()
        {
            var (c, p, cen) = await _f.NovoParAsync();
            await using var db = _f.Db();
            var pagamento = new Pagamento
            {
                ContratoServicoId = cen.ContratoId, ValorBruto = 1200m, Status = StatusPagamento.Retido, Metodo = MetodoPagamento.Pix,
                Gateway = "Asaas", GatewayPagamentoId = "pay_d3_" + Guid.NewGuid().ToString("N")[..8], AsaasClienteId = "cus_d3",
                StatusGateway = "RECEIVED", PagoEm = DateTime.UtcNow.AddDays(-1), LiberacaoAutomaticaEm = DateTime.UtcNow.AddDays(30)
            };
            db.Pagamentos.Add(pagamento);
            await db.SaveChangesAsync();
            return new Contrato(c, p, cen.ContratoId, pagamento.Id);
        }

        private static MultipartFormDataContent Formulario(string descricao = "Entrega do teste de fluxo.", string? links = null,
            params (string Nome, long Bytes)[] arquivos)
        {
            var form = new MultipartFormDataContent { { new StringContent(descricao), "descricaoEntrega" } };
            if (links is not null) form.Add(new StringContent(links), "links");
            foreach (var (nome, bytes) in arquivos.Length > 0 ? arquivos : new[] { ("entrega.pdf", 64L) })
            {
                var conteudo = new ByteArrayContent(new byte[bytes]);
                conteudo.Headers.ContentType = new MediaTypeHeaderValue(nome.EndsWith(".png") ? "image/png" : "application/pdf");
                form.Add(conteudo, "files", nome);
            }
            return form;
        }

        private Task<HttpResponseMessage> RegistrarAsync(ProcessoApiReal api, Sessao s, Guid contrato, MultipartFormDataContent? form = null)
            => api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{contrato}/entrega", s, conteudo: form ?? Formulario());

        private int ObjetosDoContrato(IEnumerable<string> objetos, Guid contrato) =>
            objetos.Where(o => o.Contains($"entregas/{contrato}/")).Distinct().Count();

        private int Transferencias() => _f.Externo.ChamadasAsaasDetalhadas.Count(c => c.StartsWith("POST") && c.Contains("/transfers"));

        private static async Task<string?> MensagemAsync(HttpResponseMessage r)
        {
            var j = await ClienteApi.LerJsonAsync(r);
            return j.ValueKind == JsonValueKind.Object && j.TryGetProperty("mensagem", out var m) ? m.GetString() : null;
        }

        private async Task<(ContratoServicoStatus Contrato, List<EntregaStatus> Entregas, StatusPagamento Pagamento, int Anexos)> EstadoAsync(Contrato c)
        {
            await using var db = _f.Db();
            var contrato = await db.ContratosServico.AsNoTracking().SingleAsync(x => x.Id == c.Id);
            var entregas = await db.Entregas.AsNoTracking().Where(e => e.ContratoServicoId == c.Id).Select(e => e.Status).ToListAsync();
            var pagamento = await db.Pagamentos.AsNoTracking().SingleAsync(p => p.Id == c.PagamentoId);
            var anexos = await db.EntregaAnexos.CountAsync(a => a.Entrega.ContratoServicoId == c.Id);
            return (contrato.Status, entregas, pagamento.Status, anexos);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Concorrência nas transições do contrato
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task EntregasSimultaneas_EntreAsReplicas_SoUmaFicaPendente_SemArquivoOrfao()
        {
            var falhas = new List<string>();
            for (var rodada = 1; rodada <= 4; rodada++)
            {
                var c = await ContratoPagoAsync();
                var s = await _f.LoginAsync(c.Prestador);
                var respostas = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
                    RegistrarAsync(i % 2 == 0 ? _f.Api : _f.Replica, s, c.Id, Formulario(arquivos: ($"e{i}.pdf", 64)))));
                var est = await EstadoAsync(c);
                var gravados = ObjetosDoContrato(_f.Externo.ObjetosGravados, c.Id);
                var removidos = ObjetosDoContrato(_f.Externo.ObjetosRemovidos, c.Id);
                var ok = respostas.Count(r => r.StatusCode == HttpStatusCode.OK);
                var pendentes = est.Entregas.Count(e => e == EntregaStatus.PendenteAprovacao);
                if (ok != 1 || pendentes != 1 || est.Anexos != 1 || gravados - removidos != 1)
                    falhas.Add($"rodada {rodada}: 200={ok} pendentes={pendentes} anexos={est.Anexos} R2 gravados={gravados} removidos={removidos}");
                foreach (var r in respostas) r.Dispose();
            }
            Assert.True(falhas.Count == 0, string.Join("\n", falhas));
        }

        [FactApiReal]
        public async Task AprovacoesSimultaneas_SoUmaVence_EUmaUnicaTransferencia()
        {
            var falhas = new List<string>();
            for (var rodada = 1; rodada <= 6; rodada++)
            {
                var c = await ContratoPagoAsync();
                using (var reg = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id))
                    Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
                var sc = await _f.LoginAsync(c.Contratante);
                await _f.ReautenticarAsync(sc);
                var antes = Transferencias();

                var rs = await Task.WhenAll(
                    _f.Api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/aprovar", sc),
                    _f.Replica.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/aprovar", sc));
                await Task.Delay(300);

                await using var db = _f.Db();
                var aprovadas = await db.Set<HistoricoContrato>().CountAsync(h => h.ContratoServicoId == c.Id && h.Acao == AcaoHistoricoContrato.EntregaAprovada);
                var codigos = rs.Select(r => (int)r.StatusCode).OrderBy(x => x).ToArray();
                var transferencias = Transferencias() - antes;
                if (!codigos.SequenceEqual(new[] { 200, 400 }) || transferencias != 1 || aprovadas != 1)
                    falhas.Add($"rodada {rodada}: respostas=[{string.Join(",", codigos)}] transferências={transferencias} históricoAprovada={aprovadas}");
                foreach (var r in rs) r.Dispose();
            }
            Assert.True(falhas.Count == 0, string.Join("\n", falhas));
        }

        [FactApiReal]
        public async Task AprovarERejeitarSimultaneos_NuncaDeixamEstadoFinanceiroEContratualIncompativel()
        {
            var falhas = new List<string>();
            for (var rodada = 1; rodada <= 8; rodada++)
            {
                var c = await ContratoPagoAsync();
                using (var reg = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id))
                    Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
                var sc = await _f.LoginAsync(c.Contratante);
                await _f.ReautenticarAsync(sc);

                var rs = await Task.WhenAll(
                    _f.Api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/aprovar", sc),
                    _f.Replica.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/rejeitar", sc, new { motivo = "Rejeição simultânea do teste." }));
                await Task.Delay(300);
                var est = await EstadoAsync(c);
                var aprovou = rs[0].StatusCode == HttpStatusCode.OK;
                var rejeitou = rs[1].StatusCode == HttpStatusCode.OK;

                var coerenteAprovado = aprovou && !rejeitou && est.Contrato == ContratoServicoStatus.Encerrado
                    && est.Entregas.SequenceEqual(new[] { EntregaStatus.Aprovada }) && est.Pagamento != StatusPagamento.Retido;
                var coerenteRejeitado = rejeitou && !aprovou && est.Contrato == ContratoServicoStatus.Ativo
                    && est.Entregas.SequenceEqual(new[] { EntregaStatus.Rejeitada }) && est.Pagamento == StatusPagamento.Retido;
                if (!coerenteAprovado && !coerenteRejeitado)
                    falhas.Add($"rodada {rodada}: aprovar={(int)rs[0].StatusCode} rejeitar={(int)rs[1].StatusCode} → contrato={est.Contrato} entrega={string.Join("/", est.Entregas)} pagamento={est.Pagamento}");
                foreach (var r in rs) r.Dispose();
            }
            Assert.True(falhas.Count == 0, string.Join("\n", falhas));
        }

        [FactApiReal]
        public async Task RegistroECancelamentoSimultaneos_NuncaDeixamEstadoIncompativel()
        {
            var falhas = new List<string>();
            for (var rodada = 1; rodada <= 8; rodada++)
            {
                var c = await ContratoPagoAsync();
                var sp = await _f.LoginAsync(c.Prestador);
                var sc = await _f.LoginAsync(c.Contratante);

                var rs = await Task.WhenAll(
                    RegistrarAsync(_f.Api, sp, c.Id),
                    _f.Replica.EnviarAsync(HttpMethod.Delete, $"/api/contratos/{c.Id}?motivo=cancelamento+simultaneo", sc));
                var est = await EstadoAsync(c);
                var registrou = rs[0].StatusCode == HttpStatusCode.OK;
                var cancelou = rs[1].StatusCode == HttpStatusCode.OK;
                var orfaos = ObjetosDoContrato(_f.Externo.ObjetosGravados, c.Id) - ObjetosDoContrato(_f.Externo.ObjetosRemovidos, c.Id) - est.Anexos;

                var coerenteEntregue = registrou && !cancelou && est.Contrato == ContratoServicoStatus.AguardandoAprovacaoEntrega
                    && est.Entregas.SequenceEqual(new[] { EntregaStatus.PendenteAprovacao }) && est.Pagamento == StatusPagamento.Retido;
                var coerenteCancelado = cancelou && !registrou && est.Contrato == ContratoServicoStatus.Cancelado
                    && est.Entregas.Count == 0 && est.Pagamento == StatusPagamento.Estornado;
                if ((!coerenteEntregue && !coerenteCancelado) || orfaos != 0)
                    falhas.Add($"rodada {rodada}: registrar={(int)rs[0].StatusCode} cancelar={(int)rs[1].StatusCode} → contrato={est.Contrato} entregas={string.Join("/", est.Entregas)} pagamento={est.Pagamento} órfãos={orfaos}");
                foreach (var r in rs) r.Dispose();
            }
            Assert.True(falhas.Count == 0, string.Join("\n", falhas));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Liberação do pagamento coordenada com a aprovação
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A aprovação vale e a liberação imediata falha (chave PIX inválida): o pagamento fica
        /// Retido, mas a intenção de liberar precisa estar gravada junto com a aprovação — o job de
        /// liberação automática passa a enxergá-lo já. Depois da correção da chave, a liberação
        /// manual conclui, uma única vez.
        /// </summary>
        [FactApiReal]
        public async Task FalhaNaLiberacaoAposAprovar_FicaPendenteDeRecuperacao_ERecuperaUmaUnicaVez()
        {
            var c = await ContratoPagoAsync();
            await using (var db = _f.Db())
                await db.ContasBancarias.Where(x => x.PrestadorId == c.Prestador.Id).ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.PixChave, Tratoo.Domain.Features.Shared.DataProtector.Encrypt("chave-que-nao-e-uuid")));

            using (var reg = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id))
                Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
            var sc = await _f.LoginAsync(c.Contratante);
            await _f.ReautenticarAsync(sc);
            var antes = Transferencias();

            using (var aprovar = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/aprovar", sc))
                Assert.Equal(HttpStatusCode.OK, aprovar.StatusCode);

            var est = await EstadoAsync(c);
            Assert.Equal(ContratoServicoStatus.Encerrado, est.Contrato);
            Assert.Equal(StatusPagamento.Retido, est.Pagamento);
            Assert.Equal(antes, Transferencias());
            await using (var db = _f.Db())
            {
                Assert.True(await db.Set<HistoricoContrato>().AnyAsync(h => h.ContratoServicoId == c.Id && h.Acao == AcaoHistoricoContrato.FalhaLiberacao));
                var pendentesDoJob = await new Tratoo.Domain.Features.Pagamentos.PagamentoRepository(db).GetPendentesLiberacaoAsync(DateTime.UtcNow);
                Assert.Contains(pendentesDoJob, p => p.Id == c.PagamentoId);
            }

            // Recuperação: chave corrigida; a liberação manual conclui. Uma segunda não transfere de novo.
            await using (var db = _f.Db())
                await db.ContasBancarias.Where(x => x.PrestadorId == c.Prestador.Id).ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.PixChave, Tratoo.Domain.Features.Shared.DataProtector.Encrypt(Guid.NewGuid().ToString())));
            using (var liberar = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/pagamentos/{c.PagamentoId}/liberar", sc, new { observacaoContratante = "recuperação" }))
                Assert.Equal(HttpStatusCode.OK, liberar.StatusCode);
            using (var deNovo = await _f.Replica.EnviarAsync(HttpMethod.Post, $"/api/pagamentos/{c.PagamentoId}/liberar", sc, new { observacaoContratante = "repetida" }))
                Assert.Equal(HttpStatusCode.BadRequest, deNovo.StatusCode);

            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, (await EstadoAsync(c)).Pagamento);
            Assert.Equal(antes + 1, Transferencias());
        }

        // ─────────────────────────────────────────────────────────────────────
        // Limpeza do R2
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Upload ambíguo: o R2 grava o 2º arquivo mas responde erro. Nada pode ficar no bucket —
        /// inclusive o arquivo cujo upload "falhou".
        /// </summary>
        [FactApiReal]
        public async Task UploadAmbiguo_NenhumArquivoFicaOrfaoNoBucket()
        {
            var c = await ContratoPagoAsync();
            var s = await _f.LoginAsync(c.Prestador);
            var marca = $"entregas/{c.Id}/";
            _f.Externo.GravarEResponderErro = caminho => caminho.Contains(marca) && caminho.EndsWith(".png");
            try
            {
                using var r = await RegistrarAsync(_f.Api, s, c.Id, Formulario(arquivos: new[] { ("parte1.pdf", 64L), ("parte2.png", 64L) }));
                Assert.False(r.IsSuccessStatusCode, $"Registro deveria falhar: {(int)r.StatusCode}");
            }
            finally
            {
                _f.Externo.GravarEResponderErro = null;
            }

            var gravados = _f.Externo.ObjetosGravados.Where(o => o.Contains(marca)).Distinct().ToList();
            var removidos = _f.Externo.ObjetosRemovidos.Where(o => o.Contains(marca)).Distinct().ToHashSet();
            Assert.Equal(2, gravados.Count);
            Assert.True(gravados.All(removidos.Contains), "Órfãos no bucket: " + string.Join(", ", gravados.Where(g => !removidos.Contains(g))));
            Assert.Empty((await EstadoAsync(c)).Entregas);
        }

        /// <summary>
        /// Commit ambíguo (a exceção chega, mas a entrega foi gravada): a limpeza não pode apagar os
        /// anexos dela. Testado no serviço, com Postgres real e o R2 falso — pela HTTP não dá para
        /// provocar a perda da confirmação do commit. Com o banco inacessível, nada é removido.
        /// </summary>
        [FactApiReal]
        public async Task LimpezaAposFalha_NaoApagaAnexoDeEntregaGravada_ESemBancoNaoApagaNada()
        {
            var c = await ContratoPagoAsync();
            var gravada = $"entregas/{c.Id}/{Guid.NewGuid()}.pdf";
            var orfa = $"entregas/{c.Id}/{Guid.NewGuid()}.pdf";
            await using (var db = _f.Db())
            {
                var entrega = new Entrega { ContratoServicoId = c.Id, DescricaoEntrega = "Gravada antes da falha.", Status = EntregaStatus.PendenteAprovacao, DataEntrega = DateTime.UtcNow };
                entrega.Anexos.Add(new EntregaAnexo { NomeArquivo = "a.pdf", ChaveR2 = gravada, TipoArquivo = ".pdf", TamanhoArquivo = 10 });
                db.Entregas.Add(entrega);
                await db.SaveChangesAsync();
            }

            await using (var db = _f.Db())
                await Servico(db).DescartarAnexosNaoGravadosAsync(new[] { gravada, orfa });

            var removidos = _f.Externo.ObjetosRemovidos.ToList();
            Assert.Contains(removidos, r => r.EndsWith(orfa));
            Assert.DoesNotContain(removidos, r => r.EndsWith(gravada));

            // Banco inacessível: não dá para saber o que está gravado → não remove nada.
            var outra = $"entregas/{c.Id}/{Guid.NewGuid()}.pdf";
            await using (var semBanco = Tratoo.Tests.Integracao.SeedIntegracao.Contexto("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"))
                await Servico(semBanco).DescartarAnexosNaoGravadosAsync(new[] { outra });
            Assert.DoesNotContain(_f.Externo.ObjetosRemovidos, r => r.EndsWith(outra));
        }

        private Tratoo.Domain.Features.Contratos.EntregaService Servico(Tratoo.Domain.Data.TratooContext db)
        {
            var storage = new Tratoo.Domain.Features.Storage.R2PrivateStorageService(
                Microsoft.Extensions.Options.Options.Create(new Tratoo.Domain.Features.Storage.R2PrivateConfig
                {
                    AccountId = "falso", AccessKey = "falso", SecretKey = "falso", BucketName = "privado", ServiceURL = _f.Externo.BaseUrl
                }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Tratoo.Domain.Features.Storage.R2PrivateStorageService>.Instance);
            return new Tratoo.Domain.Features.Contratos.EntregaService(
                new Tratoo.Domain.Features.Contratos.EntregaRepository(db),
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Contratos.IContratoServicoRepository>.Criar(),
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Pagamentos.IPagamentoRepository>.Criar(),
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Pagamentos.IPagamentoService>.Criar(),
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Avaliacoes.IAvaliacaoService>.Criar(),
                storage,
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Perfis.IContratanteRepository>.Criar(),
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Perfis.IPrestadorRepository>.Criar(),
                Tratoo.Tests.Integracao.Falso<Tratoo.Domain.Features.Infrastructure.IEmailService>.Criar(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Tratoo.Domain.Features.Contratos.EntregaService>.Instance);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Links
        // ─────────────────────────────────────────────────────────────────────

        public static IEnumerable<object[]> LinksMaliciosos => new[]
        {
            new object[] { "javascript:alert(document.cookie)" },
            new object[] { "JaVaScRiPt:alert(1)" },
            new object[] { " javascript:alert(1)" },
            new object[] { "data:text/html,<script>alert(1)</script>" },
            new object[] { "vbscript:msgbox(1)" },
            new object[] { "file:///etc/passwd" },
            new object[] { "ftp://exemplo.com/arquivo" },
            new object[] { "//exemplo.com/sem-esquema" },
            new object[] { "/caminho/relativo" },
            new object[] { "http:" },
            new object[] { "https://" },
            new object[] { "https://exemplo.com/" + new string('a', 1000) },
        };

        [TheoryApiReal]
        [MemberData(nameof(LinksMaliciosos))]
        public async Task LinkSemEsquemaHttpOuHttps_ERecusadoCom400_SemGravarNada(string url)
        {
            var c = await ContratoPagoAsync();
            var links = JsonSerializer.Serialize(new[] { new { url, descricao = "Ver entrega" } });
            using var r = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id, Formulario(links: links));

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(0, ObjetosDoContrato(_f.Externo.ObjetosGravados, c.Id));
            Assert.Empty((await EstadoAsync(c)).Entregas);
        }

        [FactApiReal]
        public async Task LinkComDescricaoAcimaDoLimite_ERecusado()
        {
            var c = await ContratoPagoAsync();
            var links = JsonSerializer.Serialize(new[] { new { url = "https://exemplo.com/ok", descricao = new string('d', 301) } });
            using var r = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id, Formulario(links: links));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(0, ObjetosDoContrato(_f.Externo.ObjetosGravados, c.Id));
        }

        [FactApiReal]
        public async Task LinksHttpEHttps_SaoAceitosEDevolvidosIguais()
        {
            var c = await ContratoPagoAsync();
            var validos = new[] { "https://www.figma.com/file/abc?node-id=1:2", "http://exemplo.com.br/entrega#v2" };
            var links = JsonSerializer.Serialize(validos.Select(u => new { url = u, descricao = "Material" }));
            var s = await _f.LoginAsync(c.Prestador);
            using (var r = await RegistrarAsync(_f.Api, s, c.Id, Formulario(links: links)))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            using var g = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/contratos/{c.Id}/entrega", s);
            var corpo = await g.Content.ReadAsStringAsync();
            foreach (var u in validos)
                Assert.Contains(JsonSerializer.Serialize(u).Trim('"'), corpo);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Limite de upload (25 MiB por entrega)
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task Upload_DentroDe25MiB_EAceito()
        {
            var c = await ContratoPagoAsync();
            using var r = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id,
                Formulario(arquivos: new[] { ("a.pdf", 12 * MiB), ("b.pdf", 12 * MiB) }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        /// <summary>Pouco acima do limite (cabe na folga do multipart): o corpo é lido e a resposta é 413 com mensagem.</summary>
        [FactApiReal]
        public async Task Upload_PoucoAcimaDe25MiB_Responde413ComMensagem_SemGravarNada()
        {
            var c = await ContratoPagoAsync();
            using var r = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id,
                Formulario(arquivos: new[] { ("a.pdf", 13 * MiB), ("b.pdf", 12 * MiB + MiB / 2) }));

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
            Assert.Contains("25 MB", await MensagemAsync(r) ?? "");
            Assert.Equal(0, ObjetosDoContrato(_f.Externo.ObjetosGravados, c.Id));
            Assert.Empty((await EstadoAsync(c)).Entregas);
        }

        /// <summary>Muito acima do limite: com "Expect: 100-continue" o cliente recebe o 413 com mensagem antes de enviar o corpo.</summary>
        [FactApiReal]
        public async Task Upload_MuitoAcimaDoLimite_Responde413ComMensagem_AntesDeReceberOCorpo()
        {
            var c = await ContratoPagoAsync();
            var s = await _f.LoginAsync(c.Prestador);
            using var req = ClienteApi.Requisicao(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega", s.Ip, s.Token,
                conteudo: Formulario(arquivos: new[] { ("a.pdf", 20 * MiB), ("b.pdf", 20 * MiB) }));
            req.Headers.ExpectContinue = true;
            using var r = await _f.Api.Http.SendAsync(req);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
            Assert.Contains("25 MB", await MensagemAsync(r) ?? "");
            Assert.Equal(0, ObjetosDoContrato(_f.Externo.ObjetosGravados, c.Id));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Fluxos legítimos
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task FluxoCompleto_Registrar_Rejeitar_RegistrarDeNovo_Aprovar_ELeitura()
        {
            var c = await ContratoPagoAsync();
            var sp = await _f.LoginAsync(c.Prestador);
            var sc = await _f.LoginAsync(c.Contratante);
            var antes = Transferencias();

            using (var r = await RegistrarAsync(_f.Api, sp, c.Id)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            foreach (var s in new[] { sp, sc })
                using (var g = await _f.Replica.EnviarAsync(HttpMethod.Get, $"/api/contratos/{c.Id}/entrega", s))
                {
                    Assert.Equal(HttpStatusCode.OK, g.StatusCode);
                    Assert.Contains("PendenteAprovacao", await g.Content.ReadAsStringAsync());
                }

            using (var r = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/rejeitar", sc, new { motivo = "Ajustar as cores do material." }))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(ContratoServicoStatus.Ativo, (await EstadoAsync(c)).Contrato);

            using (var r = await RegistrarAsync(_f.Replica, sp, c.Id)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            await _f.ReautenticarAsync(sc);
            using (var r = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/contratos/{c.Id}/entrega/aprovar", sc))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var est = await EstadoAsync(c);
            Assert.Equal(ContratoServicoStatus.Encerrado, est.Contrato);
            Assert.Equal(new[] { EntregaStatus.Aprovada, EntregaStatus.Rejeitada }, est.Entregas.OrderBy(x => x.ToString()).ToArray());
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, est.Pagamento);
            Assert.Equal(antes + 1, Transferencias());
            using (var g = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/contratos/{c.Id}/entrega", sp))
                Assert.Contains("Aprovada", await g.Content.ReadAsStringAsync());
        }

        [FactApiReal]
        public async Task CancelamentoSemEntrega_ContinuaEstornando()
        {
            var c = await ContratoPagoAsync();
            using var r = await _f.Api.EnviarAsync(HttpMethod.Delete, $"/api/contratos/{c.Id}?motivo=desistencia", await _f.LoginAsync(c.Contratante));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var est = await EstadoAsync(c);
            Assert.Equal(ContratoServicoStatus.Cancelado, est.Contrato);
            Assert.Equal(StatusPagamento.Estornado, est.Pagamento);
        }

        [FactApiReal]
        public async Task CancelamentoComEntregaPendente_ContinuaBloqueado()
        {
            var c = await ContratoPagoAsync();
            using (var reg = await RegistrarAsync(_f.Api, await _f.LoginAsync(c.Prestador), c.Id))
                Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
            using var r = await _f.Api.EnviarAsync(HttpMethod.Delete, $"/api/contratos/{c.Id}?motivo=tentativa", await _f.LoginAsync(c.Contratante));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var est = await EstadoAsync(c);
            Assert.Equal(ContratoServicoStatus.AguardandoAprovacaoEntrega, est.Contrato);
            Assert.Equal(StatusPagamento.Retido, est.Pagamento);
        }
    }
}
