using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Perfis;

namespace Tratoo.Domain.Features.Pagamentos
{
    public class PagamentoService : IPagamentoService
    {
        // ── Prazo de carência para liberação automática após prazo de entrega ───
        private const int DiasCarenciaLiberacaoAutomatica = 7;

        // ── Limite operacional diário por contratante (AML — antifraude básico) ──
        // Acima deste total acumulado no dia, o pagamento é bloqueado para análise.
        private const decimal LimiteDiarioContratante = 5_000m;

        private readonly IPagamentoRepository _repo;
        private readonly IContratoServicoRepository _contratoRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IContratanteRepository _contratanteRepo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly IAsaasGatewayService _gateway;
        private readonly IEmailService _emailService;
        private readonly IAvaliacaoService _avaliacaoService;
        private readonly IAuditLogRepository _auditRepo;
        private readonly ILogger<PagamentoService> _logger;
        private readonly AsaasConfig _asaasConfig;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public PagamentoService(
            IPagamentoRepository repo,
            IContratoServicoRepository contratoRepo,
            IPrestadorRepository prestadorRepo,
            IContratanteRepository contratanteRepo,
            IIdentidadeRepository identidadeRepo,
            IAsaasGatewayService gateway,
            IEmailService emailService,
            IAvaliacaoService avaliacaoService,
            IAuditLogRepository auditRepo,
            IOptions<AsaasConfig> asaasConfig,
            ILogger<PagamentoService> logger)
        {
            _repo = repo;
            _contratoRepo = contratoRepo;
            _prestadorRepo = prestadorRepo;
            _contratanteRepo = contratanteRepo;
            _identidadeRepo = identidadeRepo;
            _gateway = gateway;
            _emailService = emailService;
            _avaliacaoService = avaliacaoService;
            _auditRepo = auditRepo;
            _asaasConfig = asaasConfig.Value;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // INICIAR PAGAMENTO
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoPixDto> IniciarPagamentoAsync(Guid contratoServicoId, int contratanteId)
        {
            var contrato = await _contratoRepo.GetByIdAsync(contratoServicoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            // ── Validações de negócio ──────────────────────────────────────────
            if (contrato.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode iniciar o pagamento.");

            if (contrato.Status != ContratoServicoStatus.Ativo)
                throw new NegocioException("O pagamento só pode ser iniciado para contratos com status Ativo (ambas as partes devem ter assinado).");

            // ── Idempotência: evita duplo pagamento por contrato ───────────────
            var pagamentoExistente = await _repo.GetByContratoServicoIdAsync(contratoServicoId);
            if (pagamentoExistente != null)
            {
                if (pagamentoExistente.Status == StatusPagamento.Cancelado ||
                    pagamentoExistente.Status == StatusPagamento.Falhou ||
                    pagamentoExistente.Status == StatusPagamento.Estornado)
                {
                    // Permite novo pagamento após falha/cancelamento/estorno
                }
                else
                {
                    // Retorna o pagamento existente com QR Code atualizado se necessário
                    return await ObterPixAsync(pagamentoExistente.Id, contratanteId);
                }
            }

            // ── Extrai valor do contrato ───────────────────────────────────────
            var valorBruto = ExtrairValorDoContrato(contrato);
            if (valorBruto <= 0)
                throw new NegocioException("O valor do contrato deve ser maior que zero para iniciar o pagamento.");

            // ── Anti-fraude básico ─────────────────────────────────────────────
            ValidarAntifraude(valorBruto, contratanteId);

            // ── Limite operacional diário (AML) ────────────────────────────────
            // Soma o que já foi comprometido hoje + este pagamento. Acima do teto,
            // bloqueia (hard gate) e registra a tentativa para análise.
            await ValidarLimiteDiarioAsync(contratanteId, valorBruto);

            // ── Obtém dados do contratante para criar cliente no Asaas ─────────
            var contratante = await _contratanteRepo.GetCompletoAsync(contratanteId)
                ?? throw new NegocioException("Contratante não encontrado.");

            var identidade = await _identidadeRepo.ObterPorUserIdAsync(contratanteId)
                ?? throw new NegocioException("Identidade do contratante não verificada. Verifique seu CPF/CNPJ antes de realizar pagamentos.");

            var cpfCnpj = DataProtector.Decrypt(identidade.CpfCnpjCriptografado);
            var asaasClienteId = await _gateway.CriarOuObterClienteAsync(new AsaasClienteRequest(
                UsuarioId: contratanteId,
                Nome: identidade.NomeLegal,
                CpfCnpj: cpfCnpj,
                Email: contratante.Email
            ));

            // ── Cria registro de pagamento ─────────────────────────────────────
            var prazoEntrega = ObterPrazoEntregaDoContrato(contrato);
            var liberacaoAutomatica = prazoEntrega?.AddDays(DiasCarenciaLiberacaoAutomatica);

            var pagamento = new Pagamento
            {
                ContratoServicoId = contratoServicoId,
                ValorBruto = valorBruto,
                Status = StatusPagamento.Criado,
                Metodo = MetodoPagamento.Pix,
                Gateway = "Asaas",
                AsaasClienteId = asaasClienteId,
                LiberacaoAutomaticaEm = liberacaoAutomatica,
                IdempotencyKey = Guid.NewGuid().ToString()
            };

            await _repo.AddAsync(pagamento);
            await _repo.SaveChangesAsync();

            // ── Cria cobrança PIX no Asaas ─────────────────────────────────────
            AsaasCobrancaResponse cobranca;
            try
            {
                var projetoTitulo = contrato.Projeto?.Titulo ?? "Projeto";
                cobranca = await _gateway.CriarCobrancaPixAsync(new AsaasCobrancaRequest(
                    AsaasClienteId: asaasClienteId,
                    Valor: valorBruto,
                    Descricao: $"Pagamento - {projetoTitulo}",
                    ReferenciaExterna: $"pag-{pagamento.Id}",
                    DataVencimento: DateTime.UtcNow.AddDays(3)
                ));
            }
            catch (Exception ex)
            {
                pagamento.Status = StatusPagamento.Falhou;
                await _repo.SaveChangesAsync();
                _logger.LogError(ex, "Falha ao criar cobrança PIX no Asaas para pagamento {PagamentoId}", pagamento.Id);
                throw new NegocioException("Não foi possível gerar a cobrança PIX. Tente novamente em instantes.");
            }

            // ── Obtém QR Code ──────────────────────────────────────────────────
            AsaasPixQrCodeResponse qrCode;
            try
            {
                qrCode = await _gateway.ObterQrCodePixAsync(cobranca.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível obter QR Code para cobrança {CobrancaId}", cobranca.Id);
                qrCode = new AsaasPixQrCodeResponse("", null, null);
            }

            // ── Atualiza pagamento com dados do gateway ────────────────────────
            pagamento.GatewayPagamentoId = cobranca.Id;
            pagamento.StatusGateway = cobranca.Status;
            pagamento.Status = StatusPagamento.Aguardando;
            pagamento.PixQrCodePayload = qrCode.Payload;
            pagamento.PixQrCodeImagem = qrCode.EncodedImage;
            pagamento.PixQrCodeExpiracao = qrCode.ExpirationDate ?? DateTime.UtcNow.AddHours(24);

            // ── Registra no ledger ─────────────────────────────────────────────
            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.CobrancaCriada,
                Valor = valorBruto,
                Descricao = $"Cobrança PIX criada no Asaas: {cobranca.Id}",
                ReferenciaExterna = cobranca.Id,
                CriadoPorId = contratanteId
            });

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Pagamento {PagamentoId} iniciado para contrato {ContratoId}. Cobrança Asaas: {CobrancaId}",
                pagamento.Id, contratoServicoId, cobranca.Id);

            return MapearParaPixDto(pagamento);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER DETALHE
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoDetalheDto> ObterDetalheAsync(Guid pagamentoId, int usuarioId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            ValidarAcessoPagamento(pagamento, usuarioId);
            return MapearParaDetalheDto(pagamento);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER PIX (atualiza QR Code se expirado)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoPixDto> ObterPixAsync(Guid pagamentoId, int usuarioId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            ValidarAcessoPagamento(pagamento, usuarioId);

            if (pagamento.Status != StatusPagamento.Aguardando)
                throw new NegocioException($"O pagamento está com status '{pagamento.Status}'. O QR Code PIX só está disponível para pagamentos aguardando.");

            // Se QR Code expirado, obtém um novo
            if (pagamento.PixQrCodeExpiracao.HasValue &&
                pagamento.PixQrCodeExpiracao.Value < DateTime.UtcNow &&
                !string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
            {
                try
                {
                    var qrCode = await _gateway.ObterQrCodePixAsync(pagamento.GatewayPagamentoId);
                    pagamento.PixQrCodePayload = qrCode.Payload;
                    pagamento.PixQrCodeImagem = qrCode.EncodedImage;
                    pagamento.PixQrCodeExpiracao = qrCode.ExpirationDate ?? DateTime.UtcNow.AddHours(24);
                    await _repo.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Não foi possível atualizar QR Code para pagamento {PagamentoId}", pagamentoId);
                }
            }

            return MapearParaPixDto(pagamento);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PROCESSAR WEBHOOK
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ProcessarWebhookAsync(string tipoEvento, string payloadJson)
        {
            // Eventos de transferência (TRANSFER_*) trazem o objeto "transfer";
            // eventos de cobrança trazem "payment". O ID e o lookup diferem entre eles.
            var isEventoTransferencia = tipoEvento.StartsWith("TRANSFER_", StringComparison.Ordinal);

            // Parseia o payload para extrair o ID externo relevante
            string? asaasId = null;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                var propriedade = isEventoTransferencia ? "transfer" : "payment";
                if (doc.RootElement.TryGetProperty(propriedade, out var el))
                    asaasId = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao parsear payload de webhook tipo {TipoEvento}", tipoEvento);
                return;
            }

            if (string.IsNullOrWhiteSpace(asaasId))
            {
                _logger.LogWarning("Webhook {TipoEvento} sem ID ({Campo}) — ignorado.",
                    tipoEvento, isEventoTransferencia ? "transfer.id" : "payment.id");
                return;
            }

            // ── Idempotência: verifica se já foi processado ────────────────────
            var chaveIdempotencia = $"{tipoEvento}_{asaasId}";
            var webhookLog = await _repo.GetWebhookLogAsync(chaveIdempotencia);
            if (webhookLog?.ProcessadoComSucesso == true)
            {
                _logger.LogInformation("Webhook {Chave} já processado — ignorado (idempotência).", chaveIdempotencia);
                return;
            }

            // ── Registra o webhook recebido ────────────────────────────────────
            // A chave é única: um reenvio depois de falha retoma o registro existente.
            if (webhookLog is null)
            {
                webhookLog = new WebhookLog
                {
                    ChaveIdempotencia = chaveIdempotencia,
                    TipoEvento = tipoEvento,
                    AsaasCobrancaId = asaasId,
                    PayloadJson = payloadJson,
                    RecebidoEm = DateTime.UtcNow
                };

                await _repo.AddWebhookLogAsync(webhookLog);
            }
            else
            {
                webhookLog.PayloadJson = payloadJson;
            }

            // ── Localiza o pagamento no sistema ───────────────────────────────
            // Transferências são localizadas pelo AsaasTransferenciaId; cobranças pelo GatewayPagamentoId.
            var pagamento = isEventoTransferencia
                ? await _repo.GetByTransferenciaIdAsync(asaasId)
                : await _repo.GetByGatewayIdAsync(asaasId);
            if (pagamento == null)
            {
                _logger.LogWarning("Webhook {TipoEvento} para ID externo {AsaasId} sem pagamento correspondente no sistema.", tipoEvento, asaasId);
                webhookLog.ProcessadoComSucesso = true;
                webhookLog.ErroMensagem = "Pagamento não encontrado no sistema";
                webhookLog.ProcessadoEm = DateTime.UtcNow;
                await _repo.SaveChangesAsync();
                return;
            }

            // ── Processa o evento ──────────────────────────────────────────────
            string? erro = null;
            try
            {
                await ProcessarEventoAsync(tipoEvento, pagamento, payloadJson);
                pagamento.PayloadGateway = payloadJson;
            }
            catch (Exception ex)
            {
                erro = ex.Message;
                _logger.LogError(ex, "Erro ao processar webhook {TipoEvento} para pagamento {PagamentoId}", tipoEvento, pagamento.Id);
            }

            webhookLog.ProcessadoComSucesso = erro == null;
            webhookLog.ErroMensagem = erro;
            webhookLog.ProcessadoEm = DateTime.UtcNow;

            try
            {
                await _repo.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_WebhookLogs_ChaveIdempotencia"
            })
            {
                // O mesmo evento chegou em paralelo e o outro processo gravou primeiro.
                _logger.LogInformation("Webhook {Chave} processado em paralelo por outra requisição — ignorado.", chaveIdempotencia);
            }
        }

        private async Task ProcessarEventoAsync(string tipoEvento, Pagamento pagamento, string payloadJson)
        {
            switch (tipoEvento)
            {
                case "PAYMENT_RECEIVED":
                case "PAYMENT_CONFIRMED":
                    await ProcessarPagamentoPagoAsync(pagamento);
                    break;

                case "PAYMENT_OVERDUE":
                    _logger.LogWarning("Pagamento {PagamentoId} (Asaas: {AsaasId}) está vencido.", pagamento.Id, pagamento.GatewayPagamentoId);
                    break;

                case "PAYMENT_REFUNDED":
                    await ProcessarEstornoConfirmadoAsync(pagamento);
                    break;

                case "PAYMENT_REFUND_IN_PROGRESS":
                    pagamento.StatusGateway = "REFUND_IN_PROGRESS";
                    _logger.LogInformation("Estorno em andamento para pagamento {PagamentoId}.", pagamento.Id);
                    break;

                case "PAYMENT_DELETED":
                    if (pagamento.Status == StatusPagamento.Aguardando)
                    {
                        pagamento.Status = StatusPagamento.Cancelado;
                        _logger.LogInformation("Pagamento {PagamentoId} cancelado via webhook.", pagamento.Id);
                    }
                    break;

                case "PAYMENT_AWAITING_RISK_ANALYSIS":
                    pagamento.Status = StatusPagamento.Processando;
                    pagamento.StatusGateway = "AWAITING_RISK_ANALYSIS";
                    break;

                case "PAYMENT_APPROVED_BY_RISK_ANALYSIS":
                    pagamento.StatusGateway = "APPROVED_BY_RISK_ANALYSIS";
                    break;

                case "PAYMENT_REPROVED_BY_RISK_ANALYSIS":
                    pagamento.Status = StatusPagamento.Falhou;
                    pagamento.StatusGateway = "REPROVED_BY_RISK_ANALYSIS";
                    _logger.LogWarning("Pagamento {PagamentoId} reprovado por análise de risco.", pagamento.Id);
                    break;

                case "TRANSFER_DONE":
                    await ProcessarTransferenciaConcluidaAsync(pagamento, payloadJson);
                    break;

                case "TRANSFER_FAILED":
                    await ProcessarTransferenciaFalhaAsync(pagamento, "TRANSFER_FAILED",
                        "Transferência PIX ao prestador FALHOU no Asaas.");
                    break;

                case "TRANSFER_CANCELLED":
                    await ProcessarTransferenciaFalhaAsync(pagamento, "TRANSFER_CANCELLED",
                        "Transferência PIX ao prestador foi CANCELADA no Asaas.");
                    break;

                default:
                    _logger.LogDebug("Evento de webhook não tratado: {TipoEvento}", tipoEvento);
                    break;
            }
        }

        private async Task ProcessarPagamentoPagoAsync(Pagamento pagamento)
        {
            // Proteção contra dupla confirmação
            if (pagamento.Status == StatusPagamento.Retido)
            {
                _logger.LogInformation("Pagamento {PagamentoId} já está Retido — evento duplicado ignorado.", pagamento.Id);
                return;
            }

            if (pagamento.Status != StatusPagamento.Aguardando &&
                pagamento.Status != StatusPagamento.Processando)
            {
                _logger.LogWarning("Pagamento {PagamentoId} em status inesperado {Status} ao receber confirmação de pagamento.",
                    pagamento.Id, pagamento.Status);
                return;
            }

            // A confirmação chega por vários caminhos (dois eventos do Asaas, polling, simulação),
            // e cada um pode ter lido o status antes de o outro gravar. A checagem acima não basta:
            // só quem vence a transição no banco registra o ledger, na mesma transação.
            await using (var transacao = await _repo.IniciarTransacaoAsync())
            {
                if (!await _repo.TryMarcarRetidoAsync(pagamento.Id))
                {
                    await transacao.RollbackAsync();
                    await _repo.RecarregarAsync(pagamento);
                    _logger.LogInformation("Pagamento {PagamentoId} confirmado em paralelo por outra requisição — ignorado.", pagamento.Id);
                    return;
                }

                pagamento.Status = StatusPagamento.Retido;
                pagamento.PagoEm = DateTime.UtcNow;
                pagamento.StatusGateway = "RECEIVED";

                // Registra no ledger
                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId = pagamento.Id,
                    Tipo = TipoEntradaLedger.CobrancaPaga,
                    Valor = pagamento.ValorBruto,
                    Descricao = $"Pagamento PIX confirmado. Valor bruto: R$ {pagamento.ValorBruto:F2}",
                    ReferenciaExterna = pagamento.GatewayPagamentoId,
                    CriadoPorId = 0
                });

                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId = pagamento.Id,
                    Tipo = TipoEntradaLedger.EscrowRetido,
                    Valor = pagamento.ValorBruto,
                    Descricao = $"Valor retido em escrow. Prestador receberá R$ {pagamento.ValorBruto:F2} integralmente.",
                    ReferenciaExterna = pagamento.GatewayPagamentoId,
                    CriadoPorId = 0
                });

                await _repo.SaveChangesAsync();
                await transacao.CommitAsync();
            }

            _logger.LogInformation(
                "Pagamento {PagamentoId} confirmado. Valor bruto: R$ {Bruto}",
                pagamento.Id, pagamento.ValorBruto);

            // Notifica partes do contrato
            await NotificarPagamentoConfirmadoAsync(pagamento);
        }

        // Estados em que nenhum valor saiu para o prestador: a confirmação de estorno do gateway
        // leva a Estornado. TransferenciaEmProgresso e Liberado NÃO: o valor já saiu ou está
        // saindo — o status fica como está e o caso vai para conciliação manual.
        private static readonly StatusPagamento[] OrigensEstornoConfirmado =
        {
            StatusPagamento.Criado, StatusPagamento.Aguardando, StatusPagamento.Processando,
            StatusPagamento.Retido, StatusPagamento.EmDisputa, StatusPagamento.EstornoEmProgresso,
            StatusPagamento.FalhaTransferencia
        };

        /// <summary>
        /// PAYMENT_REFUNDED (achado A1). Idempotente pelo ESTADO, não só pela chave do webhook: a
        /// linha do pagamento fica travada durante a decisão, a transição é condicional e a
        /// entrada EstornoConcluido do ledger é gravada uma única vez, na mesma transação.
        /// </summary>
        private async Task ProcessarEstornoConfirmadoAsync(Pagamento pagamento)
        {
            await using var tx = await _repo.IniciarTransacaoAsync();
            await _repo.BloquearParaAtualizacaoAsync(pagamento.Id);

            var atual = await _repo.ObterStatusAsync(pagamento.Id);
            var jaRegistrado = await _repo.ExisteLedgerAsync(pagamento.Id, TipoEntradaLedger.EstornoConcluido);
            string? descricaoLedger = null;

            if (atual is StatusPagamento origem && OrigensEstornoConfirmado.Contains(origem))
            {
                await _repo.TryConfirmarEstornoAsync(pagamento.Id, DateTime.UtcNow, OrigensEstornoConfirmado);
                descricaoLedger = $"Estorno confirmado pelo gateway. Valor devolvido: R$ {pagamento.ValorBruto:F2}";
                _logger.LogInformation("Estorno confirmado para pagamento {PagamentoId} ({Origem} → Estornado).", pagamento.Id, origem);
            }
            else if (atual == StatusPagamento.Estornado)
            {
                descricaoLedger = $"Estorno confirmado pelo gateway. Valor devolvido: R$ {pagamento.ValorBruto:F2}";
                if (jaRegistrado)
                    _logger.LogInformation("Estorno do pagamento {PagamentoId} já confirmado — ignorado (idempotência).", pagamento.Id);
            }
            else if (atual is StatusPagamento.TransferenciaEmProgresso or StatusPagamento.Liberado)
            {
                descricaoLedger = $"[CONFLITO — CONCILIAÇÃO MANUAL] Estorno de R$ {pagamento.ValorBruto:F2} confirmado pelo gateway " +
                                  $"com o pagamento em {atual}. Status não alterado.";
                _logger.LogCritical(
                    "CONFLITO: o gateway confirmou o estorno do pagamento {PagamentoId}, que está {Status} (valor já saiu ou está " +
                    "saindo ao prestador). Status NÃO alterado. REQUER CONCILIAÇÃO MANUAL.", pagamento.Id, atual);
            }
            else
            {
                _logger.LogWarning("Confirmação de estorno do pagamento {PagamentoId} em status {Status} — ignorada.", pagamento.Id, atual);
            }

            LedgerFinanceiro? registro = null;
            if (descricaoLedger is not null && !jaRegistrado)
            {
                registro = new LedgerFinanceiro
                {
                    PagamentoId = pagamento.Id,
                    Tipo = TipoEntradaLedger.EstornoConcluido,
                    Valor = -pagamento.ValorBruto,
                    Descricao = descricaoLedger,
                    ReferenciaExterna = pagamento.GatewayPagamentoId,
                    CriadoPorId = 0
                };
                await _repo.AddLedgerAsync(registro);
            }

            try
            {
                await _repo.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                // O webhook falha e o Asaas reenvia; a entrada não pode ir ao banco depois, fora
                // da transação, pelo SaveChanges do registro do webhook.
                if (registro is not null) _repo.Descartar(registro);
                throw;
            }
        }

        private async Task ProcessarTransferenciaConcluidaAsync(Pagamento pagamento, string payloadJson)
        {
            // Tenta extrair o ID da transferência do payload (confirma/atualiza referência)
            string? transferId = null;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("transfer", out var t))
                    transferId = t.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            }
            catch { }

            await FinalizarLiberacaoAsync(pagamento, transferId, aprovadoPorId: 0);
        }

        // Finaliza a liberação do escrow ao prestador. Idempotente: só transiciona para
        // Liberado uma vez. Chamado pelo webhook TRANSFER_DONE e, em modo sem webhook,
        // imediatamente após a criação da transferência.
        private async Task FinalizarLiberacaoAsync(Pagamento pagamento, string? transferId, int aprovadoPorId)
        {
            // Proteção contra liberação duplicada do mesmo pagamento.
            if (pagamento.Status == StatusPagamento.Liberado)
            {
                _logger.LogInformation("Pagamento {PagamentoId} já liberado — finalização ignorada (idempotência).", pagamento.Id);
                return;
            }

            // Só finaliza pagamentos cuja transferência foi de fato iniciada (ou liberação direta/automática).
            if (pagamento.Status != StatusPagamento.TransferenciaEmProgresso &&
                pagamento.Status != StatusPagamento.Retido)
            {
                _logger.LogWarning("Pagamento {PagamentoId} em status inesperado {Status} ao finalizar transferência.",
                    pagamento.Id, pagamento.Status);
                return;
            }

            pagamento.Status = StatusPagamento.Liberado;
            pagamento.LiberadoEm = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(transferId)) pagamento.AsaasTransferenciaId = transferId;

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.LiberacaoPrestador,
                Valor = -pagamento.ValorBruto,
                Descricao = $"Transferência PIX ao prestador concluída: R$ {pagamento.ValorBruto:F2} (100% do valor pago)",
                ReferenciaExterna = transferId ?? pagamento.AsaasTransferenciaId,
                CriadoPorId = aprovadoPorId
            });

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Liberação concluída para pagamento {PagamentoId}. Prestador recebeu R$ {Valor} (integral). Transferência: {TransId}",
                pagamento.Id, pagamento.ValorBruto, pagamento.AsaasTransferenciaId);

            // ── Cria slots de avaliação bilateral (blind review) ────────────────
            if (pagamento.ContratoServicoId.HasValue)
            {
                try
                {
                    await _avaliacaoService.CriarAvaliacoesPendentesAsync(pagamento.ContratoServicoId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Falha ao criar slots de avaliação para contrato {ContratoId}. Pode ser criado manualmente.",
                        pagamento.ContratoServicoId.Value);
                }
            }

            // ── Notifica o prestador (best-effort) ──────────────────────────────
            try
            {
                var contrato = pagamento.ContratoServico;
                var prestador = contrato != null
                    ? await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId)
                    : null;

                if (prestador != null)
                {
                    await _emailService.EnviarNotificacaoLiberacaoAsync(
                        prestador.Email,
                        prestador.Nome,
                        pagamento.ValorBruto,
                        contrato?.Projeto?.Titulo ?? "Projeto");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar e-mail de liberação para o prestador (pagamento {PagamentoId}).", pagamento.Id);
            }
        }

        // Trata falha/cancelamento da transferência via webhook (TRANSFER_FAILED/CANCELLED).
        private async Task ProcessarTransferenciaFalhaAsync(Pagamento pagamento, string evento, string descricaoEvento)
        {
            if (pagamento.Status == StatusPagamento.Liberado)
            {
                _logger.LogWarning(
                    "{Evento} recebido para pagamento {PagamentoId} já LIBERADO — possível inconsistência no gateway. Verificar manualmente.",
                    evento, pagamento.Id);
                return;
            }

            pagamento.Status      = StatusPagamento.FalhaTransferencia;
            pagamento.StatusGateway = evento;

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId       = pagamento.Id,
                Tipo              = TipoEntradaLedger.LiberacaoPrestador,
                Valor             = 0, // nenhum valor saiu do escrow
                Descricao         = $"[{evento}] Transferência PIX não concluída. Valor R$ {pagamento.ValorBruto:F2} permanece retido. Transferência: {pagamento.AsaasTransferenciaId}",
                ReferenciaExterna = pagamento.AsaasTransferenciaId,
                CriadoPorId       = 0
            });

            _logger.LogError(
                "Webhook {Evento}: transferência ao prestador não concluída. " +
                "PagamentoId: {PagamentoId}, TransfId: {TransfId}, Valor: R$ {Valor}. Status → FalhaTransferencia.",
                evento, pagamento.Id, pagamento.AsaasTransferenciaId, pagamento.ValorBruto);

            // Notifica o prestador (best-effort — falha no envio não interrompe o fluxo)
            await NotificarFalhaTransferenciaAsync(pagamento);
        }

        // Trata falha detectada localmente durante ExecutarLiberacaoAsync (exceção do gateway).
        // Classifica o tipo de falha e decide se reverte para Retido (recuperável) ou
        // marca como FalhaTransferencia permanente (não recuperável).
        private async Task TratarFalhaTransferenciaAsync(
            Pagamento pagamento,
            object prestador,
            ContratoServico contrato,
            AsaasErroTipo tipoErro,
            string mensagemErro,
            int aprovadoPorId)
        {
            if (tipoErro.EhRecuperavel())
            {
                // Reverte para Retido: a transferência não foi criada no Asaas, o escrow está intacto.
                try
                {
                    await _repo.ReverterTransferenciaParaRetidoAsync(pagamento.Id);
                    pagamento.Status = StatusPagamento.Retido;
                }
                catch (Exception revEx)
                {
                    _logger.LogError(revEx,
                        "Falha crítica: não foi possível reverter pagamento {PagamentoId} para Retido. REQUER INTERVENÇÃO MANUAL.",
                        pagamento.Id);
                }

                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId       = pagamento.Id,
                    Tipo              = TipoEntradaLedger.LiberacaoPrestador,
                    Valor             = 0, // nenhum valor saiu do escrow
                    Descricao         = $"[FALHA RECUPERÁVEL – {tipoErro}] Transferência PIX não criada. Valor R$ {pagamento.ValorBruto:F2} retido. Motivo: {mensagemErro[..Math.Min(mensagemErro.Length, 200)]}",
                    ReferenciaExterna = null,
                    CriadoPorId       = aprovadoPorId
                });

                try { await _repo.SaveChangesAsync(); } catch { /* log já feito */ }

                _logger.LogWarning(
                    "Falha recuperável na transferência PIX. PagamentoId: {PagamentoId}, " +
                    "TipoErro: {TipoErro}, Motivo: {Motivo}. Pagamento revertido para Retido — pode ser reprocessado.",
                    pagamento.Id, tipoErro, mensagemErro);

                throw new NegocioException(
                    $"Não foi possível realizar a transferência ({tipoErro}). O escrow está seguro. Tente novamente ou acione o suporte.");
            }
            else
            {
                // Erro não recuperável: marca FalhaTransferencia para intervenção administrativa.
                pagamento.Status      = StatusPagamento.FalhaTransferencia;
                pagamento.StatusGateway = tipoErro.ToString();

                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId       = pagamento.Id,
                    Tipo              = TipoEntradaLedger.LiberacaoPrestador,
                    Valor             = 0, // nenhum valor saiu do escrow
                    Descricao         = $"[FALHA PERMANENTE – {tipoErro}] Transferência PIX rejeitada. Valor R$ {pagamento.ValorBruto:F2} retido. Motivo: {mensagemErro[..Math.Min(mensagemErro.Length, 200)]}",
                    ReferenciaExterna = null,
                    CriadoPorId       = aprovadoPorId
                });

                try { await _repo.SaveChangesAsync(); } catch { /* log já feito */ }

                _logger.LogError(
                    "Falha permanente na transferência PIX. PagamentoId: {PagamentoId}, " +
                    "ContratoId: {ContratoId}, TipoErro: {TipoErro}, Motivo: {Motivo}. REQUER INTERVENÇÃO ADMINISTRATIVA.",
                    pagamento.Id, contrato.Id, tipoErro, mensagemErro);

                // Notifica o prestador (best-effort)
                await NotificarFalhaTransferenciaAsync(pagamento);

                throw new NegocioException(
                    $"Transferência rejeitada pelo gateway ({tipoErro}). O escrow está seguro, mas é necessária intervenção administrativa.");
            }
        }

        /// <summary>Valor de <c>Pagamento.StatusGateway</c> para transferência com resultado incerto.</summary>
        public const string MarcadorResultadoIncerto = "RESULTADO_INCERTO";

        // A chamada de criação da transferência terminou sem resposta conclusiva (timeout,
        // rede, 5xx, 2xx ilegível, status não documentado). A transferência PODE existir no
        // Asaas. O pagamento FICA em TransferenciaEmProgresso — status que nenhuma varredura
        // nem o reprocessamento admin pegam —, então não há nova tentativa automática.
        // Sai daqui só por conciliação com o Asaas (pendente no roadmap; ver ARQUITETURA.md,
        // "Resultado incerto na transferência").
        private async Task RegistrarResultadoIncertoAsync(Pagamento pagamento, string motivo, int aprovadoPorId)
        {
            pagamento.Status        = StatusPagamento.TransferenciaEmProgresso; // já está assim no banco
            pagamento.StatusGateway = MarcadorResultadoIncerto;

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId       = pagamento.Id,
                Tipo              = TipoEntradaLedger.LiberacaoPrestador,
                Valor             = 0, // não se sabe se saiu; o escrow só baixa com TRANSFER_DONE
                Descricao         = $"[RESULTADO INCERTO] Criação da transferência PIX de R$ {pagamento.ValorBruto:F2} sem resposta conclusiva do Asaas. " +
                                    $"NÃO repetir antes de conciliar. Motivo: {motivo[..Math.Min(motivo.Length, 200)]}",
                ReferenciaExterna = null,
                CriadoPorId       = aprovadoPorId
            });

            try
            {
                await _repo.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // A reivindicação (TransferenciaEmProgresso) já está gravada: mesmo sem esta
                // marca, nenhuma varredura repete a transferência.
                _logger.LogError(ex, "Falha ao gravar a marca de resultado incerto do pagamento {PagamentoId}.", pagamento.Id);
            }

            _logger.LogError(
                "Transferência PIX com RESULTADO INCERTO. PagamentoId: {PagamentoId}, Valor: R$ {Valor}. " +
                "Pagamento mantido em TransferenciaEmProgresso, sem nova tentativa automática. REQUER CONCILIAÇÃO com o Asaas.",
                pagamento.Id, pagamento.ValorBruto);

            throw new NegocioException(
                "Não foi possível confirmar se a transferência foi criada no Asaas. O pagamento ficou aguardando " +
                "conciliação e não será transferido de novo automaticamente. Acione o suporte.");
        }

        // Envia e-mail ao prestador sobre falha na transferência. Best-effort: nunca interrompe o fluxo.
        private async Task NotificarFalhaTransferenciaAsync(Pagamento pagamento)
        {
            try
            {
                var contrato = pagamento.ContratoServico;
                if (contrato == null) return;

                var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId);
                if (prestador == null) return;

                await _emailService.EnviarNotificacaoFalhaTransferenciaAsync(
                    prestador.Email,
                    prestador.Nome,
                    contrato.Projeto?.Titulo ?? "Projeto",
                    pagamento.ValorBruto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Falha ao notificar prestador sobre erro na transferência (pagamento {PagamentoId}). E-mail ignorado.",
                    pagamento.Id);
            }
        }

        // Registra no ledger imutável uma falha de validação anterior à transferência
        // (chave PIX inválida ou prestador inapto). O valor NÃO sai do escrow: a entrada
        // tem Valor=0 e o pagamento permanece Retido (a reivindicação atômica ainda não ocorreu).
        private async Task RegistrarFalhaValidacaoLiberacaoAsync(Pagamento pagamento, string motivo, int aprovadoPorId)
        {
            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId       = pagamento.Id,
                Tipo              = TipoEntradaLedger.ValidacaoLiberacaoFalhou,
                Valor             = 0,
                Descricao         = $"Liberação bloqueada na validação: {motivo[..Math.Min(motivo.Length, 200)]}. Valor R$ {pagamento.ValorBruto:F2} permanece retido.",
                ReferenciaExterna = null,
                CriadoPorId       = aprovadoPorId
            });

            try { await _repo.SaveChangesAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao registrar entrada de validação no ledger (pagamento {PagamentoId}).", pagamento.Id);
            }
        }

        // Sanitiza a descrição enviada ao Asaas, respeitando o limite de 140 caracteres.
        private string TruncateDescricaoAsaas(string descricao, Guid pagamentoId)
        {
            if (descricao.Length <= 140) return descricao;
            var truncada = descricao[..137] + "...";
            // Só o tamanho: a descrição inclui texto do usuário (título do projeto) e não vai para o log.
            _logger.LogInformation(
                "Descrição da transferência truncada para 140 chars (pagamento {PagamentoId}). Tamanho original: {TamanhoOriginal}",
                pagamentoId, descricao.Length);
            return truncada;
        }

        private static string MascararChavePix(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return "****";
            return chave.Length <= 4 ? new string('*', chave.Length) : new string('*', chave.Length - 4) + chave[^4..];
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LIBERAR PAGAMENTO (aprovação manual pelo contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoResumoDto> LiberarPagamentoAsync(
            Guid pagamentoId, int contratanteId, string? observacao, string? ip, string? userAgent)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            if (pagamento.ContratoServico?.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode liberar o pagamento.");

            if (pagamento.Status == StatusPagamento.EmDisputa)
                throw new NegocioException("Não é possível liberar um pagamento com disputa aberta.");

            // Reservado para estorno por outra operação (cancelamento/estorno): disputa perdida → 409.
            if (pagamento.Status == StatusPagamento.EstornoEmProgresso)
                throw new ConflitoException("O pagamento está com um reembolso em processamento e não pode ser liberado. Atualize a página.");

            if (pagamento.Status != StatusPagamento.Retido)
                throw new NegocioException($"O pagamento não pode ser liberado pois está com status '{pagamento.Status}'. Somente pagamentos Retidos podem ser liberados.");

            // ── (#9) Trilha de auditoria da ação sensível ───────────────────────
            // Aprovar a liberação move dinheiro. Registra QUEM, QUANDO, IP e dispositivo
            // antes de transferir — base para investigar aprovações fraudulentas.
            await RegistrarAuditoriaLiberacaoAsync(pagamento, contratanteId, ip, userAgent);

            await ExecutarLiberacaoAsync(pagamento, contratanteId, observacao);

            // Perdeu a reivindicação atômica (job, outra liberação ou um estorno reservado chegou
            // antes): nada foi transferido por esta chamada — não responde como se tivesse liberado.
            if (pagamento.Status == StatusPagamento.Retido)
                throw new ConflitoException("O pagamento está sendo processado por outra operação. Atualize a página.");

            return MapearParaResumoDto(pagamento);
        }

        // Audita a solicitação de liberação em duas trilhas imutáveis:
        // 1) AuditLog (Marco Civil — ação "pagamento_liberado", usuário, IP, data);
        // 2) Ledger financeiro (LiberacaoSolicitada — também captura o dispositivo/UserAgent).
        private async Task RegistrarAuditoriaLiberacaoAsync(Pagamento pagamento, int contratanteId, string? ip, string? userAgent)
        {
            var ipSeguro = string.IsNullOrWhiteSpace(ip) ? "desconhecido" : ip.Trim();
            var dispositivo = string.IsNullOrWhiteSpace(userAgent)
                ? "desconhecido"
                : userAgent.Trim()[..Math.Min(userAgent.Trim().Length, 300)];

            try
            {
                await _auditRepo.RegistrarAsync(contratanteId, "pagamento_liberado", ipSeguro);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gravar AuditLog da liberação (pagamento {PagamentoId}).", pagamento.Id);
            }

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId       = pagamento.Id,
                Tipo              = TipoEntradaLedger.LiberacaoSolicitada,
                Valor             = 0,
                Descricao         = $"Liberação solicitada pelo contratante {contratanteId}. IP: {ipSeguro} | Dispositivo: {dispositivo}",
                ReferenciaExterna = null,
                CriadoPorId       = contratanteId
            });

            try { await _repo.SaveChangesAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gravar entrada de auditoria de liberação no ledger (pagamento {PagamentoId}).", pagamento.Id);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LIBERAR AUTOMATICAMENTE (chamado pelo BackgroundService)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task LiberarAutomaticamenteAsync(Guid pagamentoId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            if (pagamento.Status != StatusPagamento.Retido)
            {
                _logger.LogInformation("Liberação automática ignorada: pagamento {PagamentoId} em status {Status}.", pagamentoId, pagamento.Status);
                return;
            }

            _logger.LogInformation("Iniciando liberação automática para pagamento {PagamentoId}.", pagamentoId);
            await ExecutarLiberacaoAsync(pagamento, 0, "Liberação automática por prazo atingido.");
        }

        private async Task ExecutarLiberacaoAsync(Pagamento pagamento, int aprovadoPorId, string? observacao)
        {
            // Fast-path (otimização): evita carregar prestador se claramente não é liberável.
            if (pagamento.Status != StatusPagamento.Retido)
            {
                _logger.LogWarning(
                    "Liberação ignorada: pagamento {PagamentoId} já está em {Status} (transferência não reiniciada).",
                    pagamento.Id, pagamento.Status);
                return;
            }

            var contrato = pagamento.ContratoServico
                ?? throw new NegocioException("Contrato do pagamento não encontrado.");

            var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado.");

            // ── (#8) Prestador apto? ────────────────────────────────────────────
            // Antes de transferir: se o prestador está bloqueado/suspenso, o valor
            // NÃO é liberado — permanece retido para análise. Verificação aplicada a
            // TODAS as vias de liberação (manual, automática, disputa, reprocessamento).
            if (prestador.Status != StatusUsuario.Active)
            {
                await RegistrarFalhaValidacaoLiberacaoAsync(pagamento,
                    $"Prestador {prestador.Id} não está apto (status {prestador.Status})", aprovadoPorId);

                _logger.LogWarning(
                    "Liberação bloqueada: prestador {PrestadorId} do pagamento {PagamentoId} está {Status}. Valor retido para análise.",
                    prestador.Id, pagamento.Id, prestador.Status);

                throw new NegocioException(
                    "A conta do prestador não está apta a receber transferências no momento. " +
                    "A liberação foi suspensa e o valor permanece retido até a regularização.");
            }

            if (prestador.ContaBancaria == null || string.IsNullOrWhiteSpace(prestador.ContaBancaria.PixChave))
                throw new NegocioException("Prestador não possui chave PIX cadastrada. Solicite que o prestador cadastre sua conta bancária antes da liberação.");

            // A chave PIX é armazenada criptografada (AES-256). Descriptografa apenas
            // no momento da transferência; nunca é exposta em respostas de API.
            string chavePix;
            try
            {
                chavePix = DataProtector.Decrypt(prestador.ContaBancaria.PixChave);
            }
            catch
            {
                throw new NegocioException("Chave PIX do prestador inválida ou corrompida. Solicite o recadastro dos dados bancários.");
            }
            var tipoChavePix = MapearTipoPix(prestador.ContaBancaria.TipoPix);

            // ── (#7) Valida o FORMATO da chave PIX antes de chamar o gateway ────
            // CPF/CNPJ (dígitos verificadores), e-mail, telefone (E.164) e EVP (UUID).
            // Se inválida, não há transferência — o valor permanece retido até correção.
            try
            {
                ChavePixValidator.ValidarENormalizar(prestador.ContaBancaria.TipoPix, chavePix);
            }
            catch (NegocioException)
            {
                await RegistrarFalhaValidacaoLiberacaoAsync(pagamento,
                    $"Chave PIX do prestador inválida (tipo {prestador.ContaBancaria.TipoPix})", aprovadoPorId);

                _logger.LogWarning(
                    "Liberação bloqueada: chave PIX do prestador {PrestadorId} (tipo {Tipo}) é inválida. Pagamento {PagamentoId} permanece retido.",
                    prestador.Id, prestador.ContaBancaria.TipoPix, pagamento.Id);

                throw new NegocioException(
                    "A chave PIX do prestador é inválida. A liberação foi suspensa e o valor permanece retido " +
                    "até que o prestador corrija seus dados bancários.");
            }

            // ── Montagem e sanitização da descrição (limite Asaas: 140 chars) ──
            var descricao = TruncateDescricaoAsaas(
                $"Liberação contrato - {contrato.Projeto?.Titulo ?? "Projeto"}"
                + (observacao != null ? $" | {observacao}" : ""),
                pagamento.Id);

            // ── Reivindicação atômica (anti-duplicação) ────────────────────────
            // Transição Retido → TransferenciaEmProgresso no banco, condicional. Se outro
            // processo (ex.: liberação automática) já reivindicou, perdemos a corrida e saímos
            // SEM criar uma segunda transferência. Garante "liberação única" mesmo concorrente.
            if (!await _repo.TryMarcarTransferenciaEmProgressoAsync(pagamento.Id))
            {
                _logger.LogWarning(
                    "Liberação concorrente detectada: pagamento {PagamentoId} já reivindicado por outro processo. Nada a fazer.",
                    pagamento.Id);
                return;
            }
            // Sincroniza a entidade rastreada com o UPDATE atômico já aplicado no banco.
            pagamento.Status = StatusPagamento.TransferenciaEmProgresso;

            // ── Cria transferência PIX no Asaas ────────────────────────────────
            AsaasTransferenciaResponse transferencia;
            try
            {
                transferencia = await _gateway.CriarTransferenciaPixAsync(new AsaasTransferenciaPixRequest(
                    Valor: pagamento.ValorBruto,   // Prestador recebe 100% do valor pago
                    ChavePix: chavePix,
                    TipoChavePix: tipoChavePix,
                    Descricao: descricao,
                    // Liga a transferência a este pagamento no Asaas (conciliação).
                    ReferenciaExterna: pagamento.Id.ToString()
                ));
            }
            catch (AsaasIntegracaoException asaasEx)
            {
                // Só o resumo da resposta (códigos de erro), nunca o corpo. A mensagem distingue
                // "o Asaas recusou" de "não se sabe se foi criada".
                _logger.LogError(asaasEx,
                    (asaasEx.TipoErro == AsaasErroTipo.ResultadoIncerto
                        ? "Criação da transferência PIX sem resposta conclusiva do Asaas (resultado incerto). "
                        : "Asaas recusou a criação da transferência PIX. ") +
                    "PagamentoId: {PagamentoId}, ContratoId: {ContratoId}, PrestadorId: {PrestadorId}, " +
                    "Valor: R$ {Valor}, TipoPix: {TipoPix}, Chave: {Chave}, " +
                    "HTTP: {StatusCode}, TipoErro: {TipoErro}, Resposta: {Resumo}",
                    pagamento.Id, contrato.Id, contrato.PrestadorId,
                    pagamento.ValorBruto, tipoChavePix, MascararChavePix(chavePix),
                    (int)asaasEx.StatusCode, asaasEx.TipoErro, asaasEx.ResumoResposta);

                if (asaasEx.TipoErro == AsaasErroTipo.ResultadoIncerto)
                {
                    await RegistrarResultadoIncertoAsync(pagamento, asaasEx.Message, aprovadoPorId);
                    return;
                }

                await TratarFalhaTransferenciaAsync(pagamento, prestador, contrato,
                    asaasEx.TipoErro, asaasEx.Message, aprovadoPorId);
                return;
            }
            catch (Exception ex)
            {
                // Exceção inesperada (falha de serialização, bug, cancelamento...). Não dá
                // para afirmar que o pedido não chegou ao Asaas: resultado INCERTO.
                _logger.LogError(ex,
                    "Exceção inesperada ao criar transferência PIX. " +
                    "PagamentoId: {PagamentoId}, ContratoId: {ContratoId}, PrestadorId: {PrestadorId}, Valor: R$ {Valor}",
                    pagamento.Id, contrato.Id, contrato.PrestadorId, pagamento.ValorBruto);

                await RegistrarResultadoIncertoAsync(pagamento, $"Exceção inesperada: {ex.Message}", aprovadoPorId);
                return;
            }

            pagamento.AsaasTransferenciaId = transferencia.Id;

            // A transferência foi criada no Asaas, mas ainda NÃO está concluída. O valor
            // só é considerado liberado quando o webhook TRANSFER_DONE confirmar.
            // (Status TransferenciaEmProgresso já aplicado na reivindicação atômica acima.)

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.LiberacaoPrestador,
                Valor = 0, // ainda não saiu do escrow — registro informativo da iniciação
                Descricao = $"Transferência PIX ao prestador INICIADA: R$ {pagamento.ValorBruto:F2} (100% do valor). Aguardando confirmação (TRANSFER_DONE). Transferência: {transferencia.Id}",
                ReferenciaExterna = transferencia.Id,
                CriadoPorId = aprovadoPorId
            });

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Transferência iniciada para pagamento {PagamentoId}. Asaas: {TransId}. Aguardando webhook TRANSFER_DONE.",
                pagamento.Id, transferencia.Id);

            // ── Fallback sem webhook ────────────────────────────────────────────
            // Em ambientes onde o webhook TRANSFER_DONE não chega (ex.: localhost sem
            // túnel), finaliza imediatamente. Controlado por configuração; padrão = false
            // (fluxo dirigido por webhook). Sandbox com webhooks ativos deve manter false.
            if (_asaasConfig.ConfirmarTransferenciaImediatamente)
            {
                _logger.LogInformation(
                    "ConfirmarTransferenciaImediatamente=true — finalizando liberação do pagamento {PagamentoId} sem aguardar webhook.",
                    pagamento.Id);
                await FinalizarLiberacaoAsync(pagamento, transferencia.Id, aprovadoPorId);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ABRIR DISPUTA
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<DisputaResumoDto> AbrirDisputaAsync(Guid pagamentoId, int usuarioId, AbrirDisputaDto dto)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            ValidarAcessoPagamento(pagamento, usuarioId);

            if (pagamento.Status != StatusPagamento.Retido)
                throw new NegocioException("Disputas só podem ser abertas para pagamentos em escrow (status Retido).");

            var disputaExistente = await _repo.GetDisputaAtivaAsync(pagamentoId);
            if (disputaExistente != null)
                throw new NegocioException("Já existe uma disputa ativa para este pagamento.");

            if (string.IsNullOrWhiteSpace(dto.Motivo) || dto.Motivo.Length < 20)
                throw new NegocioException("Forneça um motivo detalhado para a disputa (mínimo 20 caracteres).");

            var disputa = new DisputaPagamento
            {
                PagamentoId = pagamentoId,
                AbertoPorId = usuarioId,
                Motivo = dto.Motivo,
                EvidenciasJson = dto.Evidencias?.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(dto.Evidencias)
                    : null
            };

            // Retido → EmDisputa condicional (achado A1): não sobrescreve um estorno reservado nem
            // uma transferência reivindicada no meio do caminho, nem abre duas disputas.
            await using var tx = await _repo.IniciarTransacaoAsync();
            if (!await _repo.TryMarcarEmDisputaAsync(pagamentoId))
                throw new NegocioException("O pagamento mudou de estado e não aceita disputa agora. Atualize a página.");
            pagamento.Status = StatusPagamento.EmDisputa; // igual ao UPDATE condicional

            await _repo.AddDisputaAsync(disputa);
            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamentoId,
                Tipo = TipoEntradaLedger.DisputaAberta,
                Valor = 0,
                Descricao = $"Disputa aberta pelo usuário {usuarioId}: {dto.Motivo[..Math.Min(dto.Motivo.Length, 100)]}",
                CriadoPorId = usuarioId
            });

            await _repo.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogWarning("Disputa aberta para pagamento {PagamentoId} pelo usuário {UsuarioId}.", pagamentoId, usuarioId);

            return MapearDisputaDto(disputa);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RESOLVER DISPUTA (admin)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ResolverDisputaAsync(Guid pagamentoId, Guid disputaId, int adminId, ResolverDisputaDto dto, string ip = "admin")
        {
            if (string.IsNullOrWhiteSpace(dto.NotaResolucao) || dto.NotaResolucao.Trim().Length < 10)
                throw new NegocioException("Informe a justificativa da decisão (mínimo 10 caracteres).");

            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            var disputa = pagamento.Disputas.FirstOrDefault(d => d.Id == disputaId)
                ?? throw new NegocioException("Disputa não encontrada.");

            // Apenas disputas abertas ou em análise podem ser resolvidas; resolvida não reabre.
            if (disputa.Status != StatusDisputa.Aberta && disputa.Status != StatusDisputa.EmAnalise)
                throw new NegocioException("Esta disputa já foi resolvida e não pode ser alterada.");

            var contrato = pagamento.ContratoServico;

            // Estado ANTERIOR (para auditoria)
            var pagamentoAntes = pagamento.Status;
            var contratoAntes = contrato?.Status;

            disputa.Status = dto.FavorContratante
                ? StatusDisputa.ResolvidaAFavorContratante
                : StatusDisputa.ResolvidaAFavorPrestador;
            disputa.ResolvidoPorId = adminId;
            disputa.ResolvidaEm = DateTime.UtcNow;
            disputa.NotaResolucao = dto.NotaResolucao.Trim();

            if (dto.FavorContratante)
            {
                // Estorna o pagamento ao contratante e encerra o contrato sem conclusão.
                await ExecutarEstornoAsync(pagamento, adminId);

                if (contrato != null)
                {
                    contrato.Status = ContratoServicoStatus.Cancelado;
                    contrato.MotivoCancelamento = $"Disputa resolvida a favor do contratante. {dto.NotaResolucao.Trim()}";
                    contrato.CanceladoEm = DateTime.UtcNow;
                }

                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId = pagamentoId,
                    Tipo = TipoEntradaLedger.DisputaResolvidaContratante,
                    Valor = 0,
                    Descricao = $"Disputa resolvida a favor do contratante por admin {adminId}. Estorno iniciado.",
                    CriadoPorId = adminId
                });
            }
            else
            {
                // Libera ao prestador. A liberação usa uma reivindicação atômica que lê o
                // status NO BANCO, portanto é necessário persistir a saída de EmDisputa para
                // Retido ANTES de chamar ExecutarLiberacaoAsync.
                pagamento.Status = StatusPagamento.Retido; // Remove EmDisputa para permitir liberação
                await _repo.SaveChangesAsync();

                await ExecutarLiberacaoAsync(pagamento, adminId, $"Disputa resolvida a favor do prestador. {dto.NotaResolucao.Trim()}");

                if (contrato != null)
                    contrato.Status = ContratoServicoStatus.Encerrado; // contrato concluído

                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId = pagamentoId,
                    Tipo = TipoEntradaLedger.DisputaResolvidaPrestador,
                    Valor = 0,
                    Descricao = $"Disputa resolvida a favor do prestador por admin {adminId}. Liberação iniciada.",
                    CriadoPorId = adminId
                });
            }

            // ── Trilha de auditoria (estado anterior → posterior) ──────────────────
            var favor = dto.FavorContratante ? "contratante" : "prestador";
            if (contrato != null)
            {
                await _repo.AddHistoricoContratoAsync(new HistoricoContrato
                {
                    ContratoServicoId = contrato.Id,
                    Acao = AcaoHistoricoContrato.DisputaResolvida,
                    UsuarioId = adminId,
                    Descricao =
                        $"Disputa resolvida a favor do {favor} pelo administrador {adminId}. " +
                        $"Contrato: {contratoAntes} → {contrato.Status}. " +
                        $"Pagamento: {pagamentoAntes} → {pagamento.Status}. " +
                        $"Justificativa: {dto.NotaResolucao.Trim()}"
                });
            }

            await _auditRepo.RegistrarAsync(adminId, "disputa_resolvida", ip);

            await _repo.SaveChangesAsync();
            _logger.LogInformation(
                "Disputa {DisputaId} do pagamento {PagamentoId} resolvida por admin {AdminId}. Favor contratante: {FavorContratante}",
                disputaId, pagamentoId, adminId, dto.FavorContratante);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SOLICITAR ESTORNO
        // ─────────────────────────────────────────────────────────────────────────
        public async Task SolicitarEstornoAsync(Guid pagamentoId, int contratanteId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            if (pagamento.ContratoServico?.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode solicitar estorno.");

            if (pagamento.Status != StatusPagamento.Aguardando &&
                pagamento.Status != StatusPagamento.Retido)
                throw new NegocioException($"Não é possível estornar um pagamento com status '{pagamento.Status}'.");

            if (pagamento.Status == StatusPagamento.Retido)
            {
                // Verifica se contrato já foi executado (prazo de entrega passou)
                var prazoEntrega = ObterPrazoEntregaDoContrato(pagamento.ContratoServico!);
                if (prazoEntrega.HasValue && prazoEntrega.Value < DateTime.UtcNow)
                    throw new NegocioException("O prazo de entrega já passou. Para disputar o pagamento, abra uma disputa.");

                // Valor em escrow: estorno com reserva (achado A1) — exclui liberação, disputa e
                // cancelamento simultâneos, e nunca deixa liberável um pagamento já estornado.
                if (string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
                    throw new NegocioException("Cobrança no gateway não encontrada para estorno.");
                if (!await _repo.TryReservarEstornoAsync(pagamento.Id))
                    throw new NegocioException("O pagamento está sendo processado por outra operação. Atualize a página e tente novamente.");

                var resultado = await EstornarReservadoAsync(pagamento.Id, contratanteId, "Estorno solicitado pelo contratante");
                if (resultado == ResultadoEstorno.FalhaDefinitiva)
                {
                    try
                    {
                        await using var tx = await _repo.IniciarTransacaoAsync();
                        await ReverterReservaEstornoAsync(pagamento.Id, contratanteId, "Estorno solicitado pelo contratante recusado pelo gateway.");
                        await _repo.SaveChangesAsync();
                        await tx.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        // A exceção abaixo encerra o escopo: nada do que ficou pendente é gravado depois.
                        _logger.LogCritical(ex,
                            "Não foi possível desfazer a reserva de estorno do pagamento {PagamentoId} (nada foi estornado). " +
                            "Pagamento bloqueado em EstornoEmProgresso: REQUER INTERVENÇÃO.", pagamento.Id);
                    }
                    throw new NegocioException("Não foi possível processar o estorno no gateway. Tente novamente.");
                }
                return;
            }

            // Ainda não pago (Aguardando): cancela a cobrança no gateway; não há valor em escrow.
            await ExecutarEstornoAsync(pagamento, contratanteId);
            await _repo.SaveChangesAsync();
        }

        private async Task ExecutarEstornoAsync(Pagamento pagamento, int solicitadoPorId)
        {
            if (string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
                throw new NegocioException("Cobrança no gateway não encontrada para estorno.");

            try
            {
                await _gateway.EstornarCobrancaAsync(pagamento.GatewayPagamentoId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao estornar cobrança {CobrancaId} no Asaas.", pagamento.GatewayPagamentoId);
                throw new NegocioException("Não foi possível processar o estorno no gateway. Tente novamente.");
            }

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.EstornoSolicitado,
                Valor = -pagamento.ValorBruto,
                Descricao = $"Estorno solicitado ao gateway para cobrança {pagamento.GatewayPagamentoId}",
                ReferenciaExterna = pagamento.GatewayPagamentoId,
                CriadoPorId = solicitadoPorId
            });

            _logger.LogInformation("Estorno solicitado para pagamento {PagamentoId}.", pagamento.Id);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers privados
        // ─────────────────────────────────────────────────────────────────────────

        private void ValidarAcessoPagamento(Pagamento pagamento, int usuarioId)
        {
            var contrato = pagamento.ContratoServico;
            if (contrato == null) return;

            if (contrato.ContratanteId != usuarioId && contrato.PrestadorId != usuarioId)
                throw new NegocioException("Você não tem permissão para acessar este pagamento.");
        }

        private static void ValidarAntifraude(decimal valor, int contratanteId)
        {
            if (valor < 5m)
                throw new NegocioException("O valor mínimo para pagamento é R$ 5,00.");

            if (valor > 500_000m)
                throw new NegocioException("O valor máximo por pagamento é R$ 500.000,00. Entre em contato para transações acima deste valor.");
        }

        // Limite operacional diário (AML). Hard gate: acima do teto, recusa o pagamento
        // e audita a tentativa para análise. Compliance do critério "limites operacionais".
        private async Task ValidarLimiteDiarioAsync(int contratanteId, decimal valorBruto)
        {
            var totalHoje = await _repo.GetTotalIniciadoNoDiaPorContratanteAsync(contratanteId, DateTime.UtcNow);

            if (totalHoje + valorBruto > LimiteDiarioContratante)
            {
                _logger.LogWarning(
                    "Limite diário excedido: contratante {ContratanteId} já comprometeu R$ {Total} hoje; " +
                    "nova tentativa de R$ {Valor} excederia o teto de R$ {Teto}. Pagamento bloqueado para análise.",
                    contratanteId, totalHoje, valorBruto, LimiteDiarioContratante);

                // Trilha de auditoria da tentativa bloqueada (Marco Civil / compliance).
                try
                {
                    await _auditRepo.RegistrarAsync(contratanteId, "pagamento_bloqueado_limite_diario", string.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao auditar bloqueio por limite diário do contratante {ContratanteId}.", contratanteId);
                }

                throw new NegocioException(
                    $"Limite diário de R$ {LimiteDiarioContratante:N2} em pagamentos atingido. " +
                    "Por segurança, esta transação requer análise adicional. Entre em contato com o suporte.");
            }
        }

        private static decimal ExtrairValorDoContrato(ContratoServico contrato)
        {
            try
            {
                using var doc = JsonDocument.Parse(contrato.ConteudoJson);
                if (doc.RootElement.TryGetProperty("pagamento", out var pag) ||
                    doc.RootElement.TryGetProperty("Pagamento", out pag))
                {
                    if (pag.TryGetProperty("valorTotal", out var vt) ||
                        pag.TryGetProperty("ValorTotal", out vt))
                        return vt.GetDecimal();
                }
            }
            catch { }

            return 0m;
        }

        private static DateTime? ObterPrazoEntregaDoContrato(ContratoServico contrato)
        {
            try
            {
                using var doc = JsonDocument.Parse(contrato.ConteudoJson);
                if (doc.RootElement.TryGetProperty("prazo", out var prazo) ||
                    doc.RootElement.TryGetProperty("Prazo", out prazo))
                {
                    if (prazo.TryGetProperty("dataTermino", out var dt) ||
                        prazo.TryGetProperty("DataTermino", out dt))
                        return DatasUtc.Instante(dt.GetDateTime());
                }
            }
            catch { }

            return null;
        }

        private static string MapearTipoPix(Domain.Enums.TipoPix tipo) => tipo switch
        {
            Domain.Enums.TipoPix.CPF => "CPF",
            Domain.Enums.TipoPix.CNPJ => "CNPJ",
            Domain.Enums.TipoPix.Email => "EMAIL",
            Domain.Enums.TipoPix.Telefone => "PHONE",
            Domain.Enums.TipoPix.Aleatoria => "EVP",
            _ => "EMAIL"
        };

        private async Task NotificarPagamentoConfirmadoAsync(Pagamento pagamento)
        {
            var contrato = pagamento.ContratoServico;
            if (contrato == null) return;

            try
            {
                var contratante = await _contratanteRepo.GetCompletoAsync(contrato.ContratanteId);
                var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId);
                var titulo = contrato.Projeto?.Titulo ?? "Projeto";

                if (contratante != null)
                    await _emailService.EnviarNotificacaoPagamentoConfirmadoAsync(
                        contratante.Email, contratante.Nome, titulo, pagamento.ValorBruto);

                if (prestador != null)
                    await _emailService.EnviarNotificacaoPagamentoEmEscrowAsync(
                        prestador.Email, prestador.Nome, titulo, pagamento.ValorBruto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar e-mails de pagamento confirmado.");
            }
        }

        private static PagamentoPixDto MapearParaPixDto(Pagamento p) => new()
        {
            PagamentoId = p.Id,
            Status = p.Status,
            ValorBruto = p.ValorBruto,
            PixPayload = p.PixQrCodePayload,
            PixQrCodeImagem = p.PixQrCodeImagem,
            PixExpiracao = p.PixQrCodeExpiracao,
            CriadoEm = p.CriadoEm,
            LiberacaoAutomaticaEm = p.LiberacaoAutomaticaEm
        };

        private static PagamentoResumoDto MapearParaResumoDto(Pagamento p) => new()
        {
            Id = p.Id,
            ContratoServicoId = p.ContratoServicoId,
            Status = p.Status,
            ValorBruto = p.ValorBruto,
            Metodo = p.Metodo,
            CriadoEm = p.CriadoEm,
            PagoEm = p.PagoEm,
            LiberadoEm = p.LiberadoEm,
            LiberacaoAutomaticaEm = p.LiberacaoAutomaticaEm,
            EmDisputa = p.Status == StatusPagamento.EmDisputa
        };

        private static PagamentoDetalheDto MapearParaDetalheDto(Pagamento p) => new()
        {
            Id = p.Id,
            ContratoServicoId = p.ContratoServicoId,
            Status = p.Status,
            ValorBruto = p.ValorBruto,
            TaxaGateway = p.TaxaGateway,   // informativo/auditoria — não participa de cálculos
            Metodo = p.Metodo,
            CriadoEm = p.CriadoEm,
            PagoEm = p.PagoEm,
            LiberadoEm = p.LiberadoEm,
            LiberacaoAutomaticaEm = p.LiberacaoAutomaticaEm,
            EmDisputa = p.Status == StatusPagamento.EmDisputa,
            PixPayload = p.PixQrCodePayload,
            PixQrCodeImagem = p.PixQrCodeImagem,
            PixExpiracao = p.PixQrCodeExpiracao,
            AsaasCobrancaId = p.GatewayPagamentoId,
            StatusGateway = p.StatusGateway,
            Ledger = p.Ledger.OrderBy(l => l.CriadoEm).Select(l => new LedgerEntradaDto
            {
                Id = l.Id,
                Tipo = l.Tipo,
                Valor = l.Valor,
                Descricao = l.Descricao,
                ReferenciaExterna = l.ReferenciaExterna,
                CriadoEm = l.CriadoEm
            }).ToList(),
            Disputas = p.Disputas.Select(MapearDisputaDto).ToList()
        };

        private static DisputaResumoDto MapearDisputaDto(DisputaPagamento d) => new()
        {
            Id = d.Id,
            Status = d.Status,
            Motivo = d.Motivo,
            AbertoEm = d.AbertoEm,
            ResolvidaEm = d.ResolvidaEm,
            NotaResolucao = d.NotaResolucao
        };

        // ─────────────────────────────────────────────────────────────────────────
        // SIMULAR CONFIRMAÇÃO (sandbox/localhost — substitui webhook PAYMENT_RECEIVED)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoResumoDto> SimularConfirmacaoAsync(Guid pagamentoId, int usuarioId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            ValidarAcessoPagamento(pagamento, usuarioId);

            if (pagamento.Status != StatusPagamento.Aguardando)
                throw new NegocioException($"Simulação indisponível: pagamento está com status '{pagamento.Status}'. Apenas pagamentos Aguardando podem ser simulados.");

            if (string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
                throw new NegocioException("Cobrança no gateway não encontrada para simulação.");

            // 1. Chama o endpoint receiveInCash do Asaas Sandbox para confirmar o pagamento
            try
            {
                await _gateway.SimularPagamentoAsync(pagamento.GatewayPagamentoId, pagamento.ValorBruto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao simular pagamento {PagamentoId} no Asaas Sandbox.", pagamentoId);
                throw new NegocioException("Não foi possível simular o pagamento no Asaas Sandbox. Verifique se está usando a API Key de sandbox.");
            }

            // 2. Processa localmente (equivalente ao webhook PAYMENT_RECEIVED)
            await ProcessarPagamentoPagoAsync(pagamento);
            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Simulação de confirmação concluída para pagamento {PagamentoId}. Status: {Status}",
                pagamentoId, pagamento.Status);

            return MapearParaResumoDto(pagamento);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SINCRONIZAR STATUS (polling — alternativa ao webhook em localhost)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoResumoDto> SincronizarStatusAsync(Guid pagamentoId, int usuarioId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            ValidarAcessoPagamento(pagamento, usuarioId);

            if (string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
                return MapearParaResumoDto(pagamento);

            // Consulta status atual no Asaas
            string statusGateway;
            try
            {
                statusGateway = await _gateway.ObterStatusCobrancaAsync(pagamento.GatewayPagamentoId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao sincronizar status do pagamento {PagamentoId}.", pagamentoId);
                return MapearParaResumoDto(pagamento);
            }

            pagamento.StatusGateway = statusGateway;

            // Se Asaas confirma recebimento mas nosso DB ainda não registrou, atualiza
            if ((statusGateway == "RECEIVED" || statusGateway == "CONFIRMED") &&
                pagamento.Status == StatusPagamento.Aguardando)
            {
                await ProcessarPagamentoPagoAsync(pagamento);
                _logger.LogInformation(
                    "Status sincronizado via polling: pagamento {PagamentoId} agora Retido (gateway: {Status}).",
                    pagamentoId, statusGateway);
            }

            await _repo.SaveChangesAsync();
            return MapearParaResumoDto(pagamento);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ESTORNO COM RESERVA (cancelamento de contrato e estorno solicitado) — achado A1
        // ─────────────────────────────────────────────────────────────────────────
        // Mesmo padrão da liberação: (1) reserva condicional no banco, Retido → EstornoEmProgresso,
        // que exclui a reivindicação da liberação (Retido → TransferenciaEmProgresso), a disputa e
        // outro estorno; (2) chamada ao Asaas FORA de transação e sem lock; (3) desfecho:
        //   • aceito        → Estornado + ledger, na mesma transação;
        //   • aceito, mas a gravação falhou → continua EstornoEmProgresso (bloqueado) até o webhook;
        //   • incerto       → continua EstornoEmProgresso, marcado para conciliação, sem retentar;
        //   • recusa comprovada → o chamador desfaz a reserva (volta a Retido).
        // Um pagamento cujo estorno PODE ter acontecido nunca volta a Retido.

        /// <summary>Marca em <c>Pagamento.StatusGateway</c> de um estorno com resultado incerto.</summary>
        public const string MarcadorEstornoIncerto = "ESTORNO_INCERTO";

        private static string? BloqueioDeCancelamento(StatusPagamento status) => status switch
        {
            StatusPagamento.TransferenciaEmProgresso =>
                "O pagamento deste contrato está em transferência ao prestador. O contrato não pode ser cancelado agora.",
            StatusPagamento.Liberado =>
                "O pagamento deste contrato já foi liberado ao prestador e o contrato não pode mais ser cancelado. Fale com o suporte.",
            StatusPagamento.FalhaTransferencia =>
                "Há uma transferência ao prestador pendente de análise neste contrato. Fale com o suporte antes de cancelar.",
            StatusPagamento.EmDisputa =>
                "Há uma disputa aberta para o pagamento deste contrato. Aguarde a decisão antes de cancelar.",
            StatusPagamento.EstornoEmProgresso =>
                "Já existe um reembolso em processamento para este contrato.",
            _ => null
        };

        public async Task<Guid?> ReservarEstornoParaCancelamentoAsync(Guid contratoServicoId)
        {
            var vigentes = await _repo.ListarVigentesPorContratoAsync(contratoServicoId);

            foreach (var p in vigentes)
                if (BloqueioDeCancelamento(p.Status) is string mensagem)
                    throw new NegocioException(mensagem);

            var retidos = vigentes.Where(p => p.Status == StatusPagamento.Retido).ToList();
            if (retidos.Count == 0)
                return null; // sem valor em escrow (sem pagamento, ou ainda não pago): nada a estornar

            if (retidos.Count > 1)
                throw new NegocioException("Há mais de um pagamento retido neste contrato. Fale com o suporte para cancelar.");

            var pagamento = retidos[0];
            if (string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
                throw new NegocioException("O pagamento deste contrato não tem cobrança no gateway para reembolsar. Fale com o suporte.");

            // Retido → EstornoEmProgresso. Quem perder a corrida (liberação manual, job, disputa)
            // não faz nada; se o cancelamento perder, ele é recusado e nada muda.
            if (!await _repo.TryReservarEstornoAsync(pagamento.Id))
                throw new NegocioException("O pagamento deste contrato está sendo processado por outra operação. Tente novamente em instantes.");

            return pagamento.Id;
        }

        public async Task<ResultadoEstorno> EstornarReservadoAsync(Guid pagamentoId, int usuarioId, string motivo)
        {
            var status = await _repo.ObterStatusAsync(pagamentoId);
            if (status != StatusPagamento.EstornoEmProgresso)
                throw new InvalidOperationException(
                    $"Estorno sem reserva: o pagamento {pagamentoId} está {status?.ToString() ?? "inexistente"}.");

            // Só dados imutáveis (cobrança e valor): o status em memória pode estar desatualizado.
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new InvalidOperationException($"Pagamento {pagamentoId} não encontrado.");
            var cobranca = pagamento.GatewayPagamentoId;
            var valor = pagamento.ValorBruto;

            if (string.IsNullOrWhiteSpace(cobranca))
            {
                _logger.LogWarning("Estorno do pagamento {PagamentoId} sem cobrança no gateway: nada foi estornado.", pagamentoId);
                return ResultadoEstorno.FalhaDefinitiva;
            }

            // ── Fase 2: Asaas, fora de transação e sem lock ────────────────────
            try
            {
                await _gateway.EstornarCobrancaAsync(cobranca, null);
            }
            catch (AsaasIntegracaoException ex) when (ex.TipoErro != AsaasErroTipo.ResultadoIncerto)
            {
                _logger.LogWarning(ex,
                    "Asaas recusou o estorno do pagamento {PagamentoId} ({TipoErro}): nada foi estornado.",
                    pagamentoId, ex.TipoErro);
                return ResultadoEstorno.FalhaDefinitiva;
            }
            catch (Exception ex)
            {
                // Rede, timeout, 5xx ou qualquer erro não classificado: o estorno PODE ter
                // acontecido. O pagamento fica EstornoEmProgresso (nada o libera) até a
                // confirmação pelo webhook PAYMENT_REFUNDED ou a conciliação.
                _logger.LogError(ex,
                    "Estorno do pagamento {PagamentoId} com RESULTADO INCERTO no Asaas. Pagamento mantido em " +
                    "EstornoEmProgresso, sem nova tentativa automática. REQUER CONCILIAÇÃO com o Asaas.",
                    pagamentoId);
                var registroIncerto = new LedgerFinanceiro
                {
                        PagamentoId       = pagamentoId,
                        Tipo              = TipoEntradaLedger.EstornoSolicitado,
                        Valor             = 0, // não se sabe se o valor saiu
                        Descricao         = $"[RESULTADO INCERTO] {motivo}: estorno de R$ {valor:F2} sem resposta conclusiva do gateway. " +
                                            "Pagamento bloqueado até a confirmação do gateway ou conciliação.",
                        ReferenciaExterna = cobranca,
                        CriadoPorId       = usuarioId
                };
                try
                {
                    await using var tx = await _repo.IniciarTransacaoAsync();
                    await _repo.MarcarEstornoIncertoAsync(pagamentoId, MarcadorEstornoIncerto);
                    await _repo.AddLedgerAsync(registroIncerto);
                    await _repo.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (Exception regEx)
                {
                    _repo.Descartar(registroIncerto);
                    _logger.LogError(regEx, "Não foi possível marcar o estorno incerto do pagamento {PagamentoId} (o pagamento continua bloqueado).", pagamentoId);
                }
                return ResultadoEstorno.Incerto;
            }

            // ── Fase 3: o Asaas aceitou — status e ledger na mesma transação ──
            var registroEstorno = new LedgerFinanceiro
            {
                PagamentoId       = pagamentoId,
                Tipo              = TipoEntradaLedger.EstornoSolicitado,
                Valor             = -valor,
                Descricao         = $"{motivo}: R$ {valor:F2}.",
                ReferenciaExterna = cobranca,
                CriadoPorId       = usuarioId
            };
            try
            {
                await using var tx = await _repo.IniciarTransacaoAsync();
                var concluiu = await _repo.TryConcluirEstornoAsync(pagamentoId, DateTime.UtcNow);
                await _repo.AddLedgerAsync(registroEstorno);
                await _repo.SaveChangesAsync();
                await tx.CommitAsync();

                if (!concluiu)
                    _logger.LogInformation("Estorno do pagamento {PagamentoId} já confirmado pelo gateway antes do registro.", pagamentoId);
                _logger.LogInformation("Estorno processado: pagamento {PagamentoId}, valor {Valor}.", pagamentoId, valor);
                return ResultadoEstorno.Estornado;
            }
            catch (Exception ex)
            {
                _repo.Descartar(registroEstorno);
                _logger.LogCritical(ex,
                    "Estorno do pagamento {PagamentoId} FEITO no Asaas, mas não registrado no banco. O pagamento continua " +
                    "EstornoEmProgresso (não pode ser liberado) até o webhook PAYMENT_REFUNDED ou a conciliação.",
                    pagamentoId);
                return ResultadoEstorno.EstornadoSemRegistro;
            }
        }

        public async Task<bool> ReverterReservaEstornoAsync(Guid pagamentoId, int usuarioId, string motivo)
        {
            var reverteu = await _repo.TryReverterReservaEstornoAsync(pagamentoId);
            if (reverteu)
            {
                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId = pagamentoId,
                    Tipo        = TipoEntradaLedger.EstornoSolicitado,
                    Valor       = 0, // nada foi estornado
                    Descricao   = $"[ESTORNO RECUSADO] {motivo} Nada foi estornado; o valor voltou a ficar retido.",
                    CriadoPorId = usuarioId
                });
            }
            else
            {
                _logger.LogError("Reserva de estorno do pagamento {PagamentoId} não pôde ser desfeita: o status já não era EstornoEmProgresso.", pagamentoId);
            }
            return reverteu;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // REPROCESSAR TRANSFERÊNCIA (uso exclusivo de administradores)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoResumoDto> ReprocessarTransferenciaAsync(Guid pagamentoId, int adminId)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            if (pagamento.Status != StatusPagamento.FalhaTransferencia)
                throw new NegocioException(
                    $"Somente pagamentos com status FalhaTransferencia podem ser reprocessados. Status atual: {pagamento.Status}.");

            _logger.LogWarning(
                "Reprocessamento manual solicitado por admin {AdminId} para pagamento {PagamentoId}. " +
                "Transferência anterior: {TransfIdAnterior}, Valor: R$ {Valor}.",
                adminId, pagamentoId, pagamento.AsaasTransferenciaId, pagamento.ValorBruto);

            // Registra a intenção de reprocessamento no ledger antes de alterar o status.
            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId       = pagamentoId,
                Tipo              = TipoEntradaLedger.LiberacaoPrestador,
                Valor             = 0,
                Descricao         = $"[REPROCESSAMENTO ADMIN] Admin {adminId} solicitou nova tentativa de transferência. Transferência anterior: {pagamento.AsaasTransferenciaId ?? "N/A"}",
                ReferenciaExterna = null,
                CriadoPorId       = adminId
            });

            // Reverte para Retido para que ExecutarLiberacaoAsync possa processar.
            pagamento.Status             = StatusPagamento.Retido;
            pagamento.AsaasTransferenciaId = null; // limpa a referência da transferência falha
            pagamento.StatusGateway      = null;
            await _repo.SaveChangesAsync();

            // Reutiliza o fluxo completo de liberação.
            await ExecutarLiberacaoAsync(pagamento, adminId,
                $"Reprocessamento manual por admin {adminId}");

            return MapearParaResumoDto(pagamento);
        }
    }
}
