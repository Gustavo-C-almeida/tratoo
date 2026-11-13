using Microsoft.Extensions.Logging;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Avaliacoes;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    /// <summary>
    /// Orquestra o fluxo de entrega formal: registro pelo prestador, aprovação/rejeição
    /// pelo contratante e a liberação do pagamento ao aprovar (reuso do escrow existente).
    /// Cada ação é auditada em HistoricoContrato.
    /// </summary>
    public class EntregaService : IEntregaService
    {
        private readonly IEntregaRepository _entregaRepo;
        private readonly IContratoServicoRepository _contratoRepo;
        private readonly IPagamentoRepository _pagamentoRepo;
        private readonly IPagamentoService _pagamentoService;
        private readonly IAvaliacaoService _avaliacaoService;
        private readonly IR2PrivateStorageService _storage;
        private readonly IContratanteRepository _contratanteRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IEmailService _emailService;
        private readonly ILogger<EntregaService> _logger;

        private static readonly TimeSpan UrlValidade = TimeSpan.FromMinutes(30);

        public EntregaService(
            IEntregaRepository entregaRepo,
            IContratoServicoRepository contratoRepo,
            IPagamentoRepository pagamentoRepo,
            IPagamentoService pagamentoService,
            IAvaliacaoService avaliacaoService,
            IR2PrivateStorageService storage,
            IContratanteRepository contratanteRepo,
            IPrestadorRepository prestadorRepo,
            IEmailService emailService,
            ILogger<EntregaService> logger)
        {
            _entregaRepo = entregaRepo;
            _contratoRepo = contratoRepo;
            _pagamentoRepo = pagamentoRepo;
            _pagamentoService = pagamentoService;
            _avaliacaoService = avaliacaoService;
            _storage = storage;
            _contratanteRepo = contratanteRepo;
            _prestadorRepo = prestadorRepo;
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>Regras para registrar entrega: dono, contrato ativo, pagamento em garantia e nenhuma entrega pendente.</summary>
        private async Task<ContratoServico> CarregarContratoParaEntregaAsync(Guid contratoId, int prestadorId)
        {
            var contrato = await _contratoRepo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            if (contrato.PrestadorId != prestadorId)
                throw new NegocioException("Apenas o prestador pode registrar a entrega.");

            if (contrato.Status != ContratoServicoStatus.Ativo)
                throw new NegocioException("A entrega só pode ser registrada em contratos ativos.");

            // Bloqueio funcional do fluxo: a entrega só é permitida após o pagamento
            // estar protegido em garantia (escrow Retido). Impede pular a etapa de pagamento.
            var pagamento = await _pagamentoRepo.GetByContratoServicoIdAsync(contratoId);
            if (pagamento == null ||
                pagamento.Status is not (StatusPagamento.Retido
                                      or StatusPagamento.EmDisputa
                                      or StatusPagamento.TransferenciaEmProgresso
                                      or StatusPagamento.Liberado
                                      or StatusPagamento.FalhaTransferencia))
            {
                throw new NegocioException(
                    "A entrega só pode ser registrada após a confirmação do pagamento. " +
                    "Aguarde o contratante pagar e o valor ficar protegido em garantia.");
            }

            var pendente = await _entregaRepo.GetPendentePorContratoAsync(contratoId);
            if (pendente != null)
                throw new NegocioException("Já existe uma entrega aguardando aprovação para este contrato.");

            return contrato;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // REGISTRAR ENTREGA (prestador)
        // ─────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Confere, SEM gravar nada, se o prestador pode registrar entrega neste contrato. O
        /// endpoint chama isto ANTES de ler o formulário e de enviar arquivos ao R2: quem não é o
        /// prestador do contrato não consegue gravar nada no bucket privado (achado D3).
        /// <see cref="RegistrarEntregaAsync"/> repete a mesma conferência ao gravar.
        /// </summary>
        public async Task ValidarRegistroDeEntregaAsync(Guid contratoId, int prestadorId)
            => await CarregarContratoParaEntregaAsync(contratoId, prestadorId);

        public async Task<EntregaDetalheDto> RegistrarEntregaAsync(
            Guid contratoId, int prestadorId, RegistrarEntregaDto dto, List<EntregaAnexoUpload> anexos)
        {
            var contrato = await CarregarContratoParaEntregaAsync(contratoId, prestadorId);

            if (string.IsNullOrWhiteSpace(dto.DescricaoEntrega))
                throw new NegocioException("A descrição da entrega é obrigatória.");

            var entrega = new Entrega
            {
                ContratoServicoId = contratoId,
                DescricaoEntrega = dto.DescricaoEntrega.Trim(),
                Observacoes = string.IsNullOrWhiteSpace(dto.Observacoes) ? null : dto.Observacoes.Trim(),
                DataEntrega = dto.DataEntrega == default ? DatasUtc.HojeNoBrasil() : DatasUtc.Calendario(dto.DataEntrega),
                Status = EntregaStatus.PendenteAprovacao
            };

            foreach (var a in anexos)
            {
                entrega.Anexos.Add(new EntregaAnexo
                {
                    NomeArquivo = a.NomeArquivo,
                    ChaveR2 = a.ChaveR2,
                    TipoArquivo = a.TipoArquivo,
                    TamanhoArquivo = a.TamanhoArquivo
                });
            }

            if (dto.Links != null)
            {
                foreach (var l in dto.Links.Where(x => !string.IsNullOrWhiteSpace(x.Url)))
                {
                    entrega.Links.Add(new EntregaLink
                    {
                        Url = l.Url.Trim(),
                        Descricao = string.IsNullOrWhiteSpace(l.Descricao) ? null : l.Descricao.Trim()
                    });
                }
            }

            // Transição atômica Ativo → AguardandoAprovacaoEntrega, na mesma transação que grava a
            // entrega. Duas entregas simultâneas (mesmo em réplicas diferentes): só uma muda o
            // contrato; a outra recebe 400 e nada dela é gravado. Um cancelamento simultâneo
            // (que trava a linha) também é excludente com o registro.
            var agora = DateTime.UtcNow;
            await using var tx = await _contratoRepo.IniciarTransacaoAsync();
            if (!await _contratoRepo.TentarRegistrarEntregaAsync(contratoId, agora))
            {
                var atual = await _contratoRepo.ObterStatusAsync(contratoId);
                throw new NegocioException(atual == ContratoServicoStatus.AguardandoAprovacaoEntrega
                    ? "Já existe uma entrega aguardando aprovação para este contrato."
                    : "A entrega só pode ser registrada em contratos ativos.");
            }

            await _entregaRepo.AddAsync(entrega);

            // Mantém a entidade rastreada igual ao que o UPDATE condicional gravou.
            contrato.Status = ContratoServicoStatus.AguardandoAprovacaoEntrega;
            contrato.EntregaRegistradaEm = agora;

            // Auditoria
            await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.EntregaCriada, prestadorId,
                $"Entrega registrada com {entrega.Anexos.Count} anexo(s) e {entrega.Links.Count} link(s).");
            foreach (var a in entrega.Anexos)
                await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.AnexoAdicionado, prestadorId, $"Anexo: {a.NomeArquivo}");
            foreach (var l in entrega.Links)
                await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.LinkAdicionado, prestadorId, $"Link: {l.Url}");

            await _entregaRepo.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Entrega {EntregaId} registrada no contrato {ContratoId} pelo prestador {PrestadorId}.",
                entrega.Id, contratoId, prestadorId);

            // Notifica contratante (best-effort)
            try
            {
                var contratante = await _contratanteRepo.GetCompletoAsync(contrato.ContratanteId);
                if (contratante != null)
                    await _emailService.EnviarSolicitacaoAssinaturaAsync(contratante.Email, contratante.Nome);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao notificar contratante sobre entrega do contrato {ContratoId}.", contratoId);
            }

            return await MapAsync(entrega);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // APROVAR ENTREGA (contratante) → libera pagamento
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<EntregaDetalheDto> AprovarEntregaAsync(Guid contratoId, int contratanteId, string? ip, string? userAgent)
        {
            var contrato = await _contratoRepo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            // Apenas o contratante aprova — impede o prestador de aprovar a própria entrega.
            if (contrato.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode aprovar a entrega.");

            // Transição atômica AguardandoAprovacaoEntrega → Encerrado. Aprovação dupla ou aprovação
            // simultânea com rejeição: só uma das ações muda o contrato; a outra recebe 400.
            // Na MESMA transação, a liberação do pagamento retido fica pendente de forma durável
            // (liberação automática antecipada para agora): se a liberação imediata, logo abaixo,
            // falhar — ou o processo cair entre o commit e ela —, o job de liberação automática
            // a retoma. A reivindicação atômica do pagamento (Retido → TransferenciaEmProgresso)
            // impede transferência dupla entre a liberação imediata, o job e a liberação manual.
            var agora = DateTime.UtcNow;
            await using var tx = await _contratoRepo.IniciarTransacaoAsync();
            if (!await _contratoRepo.TentarAprovarEntregaAsync(contratoId))
                throw new NegocioException("Não há entrega aguardando aprovação neste contrato.");

            var entrega = await _entregaRepo.GetPendentePorContratoAsync(contratoId)
                ?? throw new NegocioException("Entrega pendente não encontrada.");

            entrega.Status = EntregaStatus.Aprovada;
            entrega.AprovadaEm = agora;
            entrega.AprovadorId = contratanteId;
            entrega.AtualizadoEm = agora;

            contrato.Status = ContratoServicoStatus.Encerrado;   // igual ao UPDATE condicional

            await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.EntregaAprovada, contratanteId,
                "Entrega aprovada pelo contratante.");

            await _pagamentoRepo.AnteciparLiberacaoAutomaticaAsync(contratoId, agora);

            await _entregaRepo.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Entrega {EntregaId} do contrato {ContratoId} aprovada pelo contratante {ContratanteId}.",
                entrega.Id, contratoId, contratanteId);

            // Cria slots de avaliação bilateral imediatamente após aprovação.
            // O método é idempotente — duplicatas são ignoradas se o PagamentoService também chamar.
            try
            {
                await _avaliacaoService.CriarAvaliacoesPendentesAsync(contratoId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao criar slots de avaliação para contrato {ContratoId}.", contratoId);
            }

            // Libera o pagamento retido — reuso do fluxo de escrow existente.
            await LiberarPagamentoSeRetidoAsync(contratoId, contratanteId, ip, userAgent);

            return await MapAsync(entrega);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // REJEITAR ENTREGA / SOLICITAR AJUSTES (contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<EntregaDetalheDto> RejeitarEntregaAsync(Guid contratoId, int contratanteId, string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new NegocioException("Informe o motivo da solicitação de ajustes.");

            var contrato = await _contratoRepo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            if (contrato.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode reprovar a entrega.");

            // Transição atômica AguardandoAprovacaoEntrega → Ativo (excludente com a aprovação).
            await using var tx = await _contratoRepo.IniciarTransacaoAsync();
            if (!await _contratoRepo.TentarRejeitarEntregaAsync(contratoId))
                throw new NegocioException("Não há entrega aguardando aprovação neste contrato.");

            var entrega = await _entregaRepo.GetPendentePorContratoAsync(contratoId)
                ?? throw new NegocioException("Entrega pendente não encontrada.");

            entrega.Status = EntregaStatus.Rejeitada;
            entrega.MotivoRejeicao = motivo.Trim();
            entrega.RejeitadaEm = DateTime.UtcNow;
            entrega.AtualizadoEm = DateTime.UtcNow;

            // Volta o contrato para execução; limpa o marco de entrega para permitir nova entrega.
            contrato.Status = ContratoServicoStatus.Ativo;
            contrato.EntregaRegistradaEm = null;

            await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.EntregaRejeitada, contratanteId,
                $"Ajustes solicitados: {motivo.Trim()}");

            await _entregaRepo.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Entrega {EntregaId} do contrato {ContratoId} rejeitada pelo contratante {ContratanteId}.",
                entrega.Id, contratoId, contratanteId);

            // Notifica prestador (best-effort)
            try
            {
                var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId);
                if (prestador != null)
                    await _emailService.EnviarSolicitacaoAssinaturaAsync(prestador.Email, prestador.Nome);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao notificar prestador sobre rejeição da entrega do contrato {ContratoId}.", contratoId);
            }

            return await MapAsync(entrega);
        }

        /// <summary>
        /// Depois de uma falha no registro: remove do bucket só os arquivos que NÃO estão gravados
        /// em nenhuma entrega. Cobre o commit ambíguo (a exceção chega, mas a entrega foi gravada:
        /// os anexos dela ficam) e o upload ambíguo (a chave entra na lista antes do envio). Se não
        /// for possível consultar o banco, não remove nada — arquivo órfão é melhor que anexo perdido.
        /// </summary>
        public async Task DescartarAnexosNaoGravadosAsync(IReadOnlyCollection<string> chaves)
        {
            if (chaves.Count == 0) return;

            HashSet<string> gravadas;
            try
            {
                gravadas = await _entregaRepo.ChavesReferenciadasAsync(chaves);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Não foi possível conferir {Total} anexo(s) da entrega no banco: nenhum foi removido do bucket (podem ficar órfãos).",
                    chaves.Count);
                return;
            }

            foreach (var chave in chaves.Where(c => !gravadas.Contains(c)))
            {
                try { await _storage.ExcluirAsync(chave); }
                catch (Exception ex) { _logger.LogWarning(ex, "Não foi possível remover o anexo órfão {Chave}.", chave); }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER ENTREGA
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<EntregaDetalheDto?> ObterEntregaAsync(Guid contratoId, int usuarioId)
        {
            var contrato = await _contratoRepo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            if (contrato.ContratanteId != usuarioId && contrato.PrestadorId != usuarioId)
                throw new NegocioException("Você não tem acesso a este contrato.");

            var entrega = await _entregaRepo.GetAtualPorContratoAsync(contratoId);
            return entrega == null ? null : await MapAsync(entrega);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers privados
        // ─────────────────────────────────────────────────────────────────────────
        private async Task LiberarPagamentoSeRetidoAsync(Guid contratoId, int contratanteId, string? ip, string? userAgent)
        {
            var pagamento = await _pagamentoRepo.GetByContratoServicoIdAsync(contratoId);
            if (pagamento == null)
            {
                _logger.LogInformation("Aprovação do contrato {ContratoId} sem pagamento associado — nada a liberar.", contratoId);
                return;
            }

            if (pagamento.Status != StatusPagamento.Retido)
            {
                _logger.LogInformation(
                    "Pagamento {PagamentoId} do contrato {ContratoId} está {Status} — liberação não aplicável.",
                    pagamento.Id, contratoId, pagamento.Status);
                return;
            }

            try
            {
                await _pagamentoService.LiberarPagamentoAsync(
                    pagamento.Id, contratanteId, "Liberação após aprovação da entrega.", ip, userAgent);

                await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.PagamentoLiberado, contratanteId,
                    "Pagamento liberado ao prestador após aprovação da entrega.");
                await _entregaRepo.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Falha ao liberar pagamento {PagamentoId} do contrato {ContratoId} após aprovação. A entrega permanece aprovada; liberação pode ser reprocessada.",
                    pagamento.Id, contratoId);

                await RegistrarHistoricoAsync(contratoId, AcaoHistoricoContrato.FalhaLiberacao, contratanteId,
                    $"Falha ao liberar pagamento: {ex.Message}");
                await _entregaRepo.SaveChangesAsync();
            }
        }

        private async Task RegistrarHistoricoAsync(Guid contratoId, AcaoHistoricoContrato acao, int usuarioId, string descricao)
        {
            await _entregaRepo.AddHistoricoAsync(new HistoricoContrato
            {
                ContratoServicoId = contratoId,
                Acao = acao,
                Descricao = descricao,
                UsuarioId = usuarioId
            });
        }

        private async Task<EntregaDetalheDto> MapAsync(Entrega e)
        {
            var dto = new EntregaDetalheDto
            {
                Id = e.Id,
                ContratoServicoId = e.ContratoServicoId,
                DescricaoEntrega = e.DescricaoEntrega,
                Observacoes = e.Observacoes,
                DataEntrega = e.DataEntrega,
                Status = e.Status,
                CriadoEm = e.CriadoEm,
                AprovadaEm = e.AprovadaEm,
                RejeitadaEm = e.RejeitadaEm,
                MotivoRejeicao = e.MotivoRejeicao,
                Links = e.Links.Select(l => new EntregaLinkDetalheDto
                {
                    Id = l.Id,
                    Url = l.Url,
                    Descricao = l.Descricao
                }).ToList()
            };

            foreach (var a in e.Anexos)
            {
                string url;
                try
                {
                    url = await _storage.GerarUrlAssinadaAsync(a.ChaveR2, UrlValidade);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao gerar URL assinada para anexo {AnexoId}.", a.Id);
                    url = string.Empty;
                }

                dto.Anexos.Add(new EntregaAnexoDto
                {
                    Id = a.Id,
                    NomeArquivo = a.NomeArquivo,
                    TipoArquivo = a.TipoArquivo,
                    TamanhoArquivo = a.TamanhoArquivo,
                    UrlDownload = url,
                    CriadoEm = a.CriadoEm
                });
            }

            return dto;
        }
    }
}
