using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Pagamentos
{
    public class PagamentoService : IPagamentoService
    {
        // ── Taxa padrão da plataforma Tratoo ────────────────────────────────────
        private const decimal TaxaPlataformaPercentual = 0.10m; // 10%

        // ── Prazo de carência para liberação automática após prazo de entrega ───
        private const int DiasCarenciaLiberacaoAutomatica = 7;

        private readonly IPagamentoRepository _repo;
        private readonly IContratoServicoRepository _contratoRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IContratanteRepository _contratanteRepo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly IAsaasGatewayService _gateway;
        private readonly IEmailService _emailService;
        private readonly IAvaliacaoService _avaliacaoService;
        private readonly ILogger<PagamentoService> _logger;

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

            // ── Calcula taxas ──────────────────────────────────────────────────
            var taxaPlataforma = Math.Round(valorBruto * TaxaPlataformaPercentual, 2);

            // ── Cria registro de pagamento ─────────────────────────────────────
            var prazoEntrega = ObterPrazoEntregaDoContrato(contrato);
            var liberacaoAutomatica = prazoEntrega?.AddDays(DiasCarenciaLiberacaoAutomatica);

            var pagamento = new Pagamento
            {
                ContratoServicoId = contratoServicoId,
                ValorBruto = valorBruto,
                TaxaPlataforma = taxaPlataforma,
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
            // Parseia o payload para extrair o ID da cobrança
            string? asaasCobrancaId = null;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("payment", out var paymentEl))
                    asaasCobrancaId = paymentEl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao parsear payload de webhook tipo {TipoEvento}", tipoEvento);
                return;
            }

            if (string.IsNullOrWhiteSpace(asaasCobrancaId))
            {
                _logger.LogWarning("Webhook {TipoEvento} sem ID de cobrança — ignorado.", tipoEvento);
                return;
            }

            // ── Idempotência: verifica se já foi processado ────────────────────
            var chaveIdempotencia = $"{tipoEvento}_{asaasCobrancaId}";
            if (await _repo.WebhookJaProcessadoAsync(chaveIdempotencia))
            {
                _logger.LogInformation("Webhook {Chave} já processado — ignorado (idempotência).", chaveIdempotencia);
                return;
            }

            // ── Registra o webhook recebido ────────────────────────────────────
            var webhookLog = new WebhookLog
            {
                ChaveIdempotencia = chaveIdempotencia,
                TipoEvento = tipoEvento,
                AsaasCobrancaId = asaasCobrancaId,
                PayloadJson = payloadJson,
                RecebidoEm = DateTime.UtcNow
            };

            await _repo.AddWebhookLogAsync(webhookLog);

            // ── Localiza o pagamento no sistema ───────────────────────────────
            var pagamento = await _repo.GetByGatewayIdAsync(asaasCobrancaId);
            if (pagamento == null)
            {
                _logger.LogWarning("Webhook para cobrança {CobrancaId} sem pagamento correspondente no sistema.", asaasCobrancaId);
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

            await _repo.SaveChangesAsync();
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
                    _logger.LogError("Transferência ao prestador FALHOU para pagamento {PagamentoId}.", pagamento.Id);
                    // Mantém status Retido para reprocessamento manual
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
                Valor = pagamento.ValorLiquidoPrestador,
                Descricao = $"Valor em escrow. Prestador receberá R$ {pagamento.ValorLiquidoPrestador:F2}",
                ReferenciaExterna = pagamento.GatewayPagamentoId,
                CriadoPorId = 0
            });

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.TaxaPlataformaCobrada,
                Valor = -pagamento.TaxaPlataforma,
                Descricao = $"Taxa plataforma Tratoo 10%: R$ {pagamento.TaxaPlataforma:F2}",
                ReferenciaExterna = pagamento.GatewayPagamentoId,
                CriadoPorId = 0
            });

            _logger.LogInformation(
                "Pagamento {PagamentoId} confirmado. Valor bruto: R$ {Bruto}, Taxa: R$ {Taxa}, Líquido prestador: R$ {Liquido}",
                pagamento.Id, pagamento.ValorBruto, pagamento.TaxaPlataforma, pagamento.ValorLiquidoPrestador);

            // Notifica partes do contrato
            await NotificarPagamentoConfirmadoAsync(pagamento);
        }

        private async Task ProcessarEstornoConfirmadoAsync(Pagamento pagamento)
        {
            pagamento.Status = StatusPagamento.Estornado;
            pagamento.EstornadoEm = DateTime.UtcNow;
            pagamento.StatusGateway = "REFUNDED";

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.EstornoConcluido,
                Valor = -pagamento.ValorBruto,
                Descricao = $"Estorno confirmado pelo gateway. Valor devolvido: R$ {pagamento.ValorBruto:F2}",
                ReferenciaExterna = pagamento.GatewayPagamentoId,
                CriadoPorId = 0
            });

            _logger.LogInformation("Estorno confirmado para pagamento {PagamentoId}.", pagamento.Id);
        }

        private async Task ProcessarTransferenciaConcluidaAsync(Pagamento pagamento, string payloadJson)
        {
            if (pagamento.Status == StatusPagamento.Liberado)
            {
                _logger.LogInformation("Transferência para pagamento {PagamentoId} já registrada — evento duplicado.", pagamento.Id);
                return;
            }

            // Tenta extrair o ID da transferência do payload
            string? transferId = null;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("transfer", out var t))
                    transferId = t.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            }
            catch { }

            pagamento.Status = StatusPagamento.Liberado;
            pagamento.LiberadoEm = DateTime.UtcNow;
            if (transferId != null) pagamento.AsaasTransferenciaId = transferId;

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.LiberacaoPrestador,
                Valor = -pagamento.ValorLiquidoPrestador,
                Descricao = $"Transferência PIX ao prestador concluída: R$ {pagamento.ValorLiquidoPrestador:F2}",
                ReferenciaExterna = transferId ?? pagamento.AsaasTransferenciaId,
                CriadoPorId = 0
            });

            _logger.LogInformation(
                "Transferência concluída para pagamento {PagamentoId}. Prestador recebeu R$ {Valor}",
                pagamento.Id, pagamento.ValorLiquidoPrestador);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LIBERAR PAGAMENTO (aprovação manual pelo contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PagamentoResumoDto> LiberarPagamentoAsync(Guid pagamentoId, int contratanteId, string? observacao)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            if (pagamento.ContratoServico?.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode liberar o pagamento.");

            if (pagamento.Status != StatusPagamento.Retido)
                throw new NegocioException($"O pagamento não pode ser liberado pois está com status '{pagamento.Status}'. Somente pagamentos Retidos podem ser liberados.");

            if (pagamento.Status == StatusPagamento.EmDisputa)
                throw new NegocioException("Não é possível liberar um pagamento com disputa aberta.");

            await ExecutarLiberacaoAsync(pagamento, contratanteId, observacao);
            return MapearParaResumoDto(pagamento);
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
            var contrato = pagamento.ContratoServico
                ?? throw new NegocioException("Contrato do pagamento não encontrado.");

            var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado.");

            if (prestador.ContaBancaria == null || string.IsNullOrWhiteSpace(prestador.ContaBancaria.PixChave))
                throw new NegocioException("Prestador não possui chave PIX cadastrada. Solicite que o prestador cadastre sua conta bancária antes da liberação.");

            var chavePix = prestador.ContaBancaria.PixChave;
            var tipoChavePix = MapearTipoPix(prestador.ContaBancaria.TipoPix);
            var descricao = $"Liberação contrato - {contrato.Projeto?.Titulo ?? "Projeto"}"
                + (observacao != null ? $" | {observacao}" : "");

            // ── Cria transferência PIX no Asaas ────────────────────────────────
            AsaasTransferenciaResponse transferencia;
            try
            {
                transferencia = await _gateway.CriarTransferenciaPixAsync(new AsaasTransferenciaPixRequest(
                    Valor: pagamento.ValorLiquidoPrestador,
                    ChavePix: chavePix,
                    TipoChavePix: tipoChavePix,
                    Descricao: descricao
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao criar transferência PIX para pagamento {PagamentoId}", pagamento.Id);
                throw new NegocioException("Não foi possível realizar a transferência ao prestador. Tente novamente.");
            }

            pagamento.AsaasTransferenciaId = transferencia.Id;

            // Em localhost/sandbox não há webhook TRANSFER_DONE — marcamos como Liberado
            // imediatamente após a transferência ser criada com sucesso.
            pagamento.Status = StatusPagamento.Liberado;
            pagamento.LiberadoEm = DateTime.UtcNow;

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.LiberacaoPrestador,
                Valor = -pagamento.ValorLiquidoPrestador,
                Descricao = $"Transferência PIX ao prestador: R$ {pagamento.ValorLiquidoPrestador:F2}. Transferência: {transferencia.Id}",
                ReferenciaExterna = transferencia.Id,
                CriadoPorId = aprovadoPorId
            });

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Liberação iniciada para pagamento {PagamentoId}. Transferência Asaas: {TransId}",
                pagamento.Id, transferencia.Id);

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

            // Notifica prestador
            try
            {
                await _emailService.EnviarNotificacaoLiberacaoAsync(
                    prestador.Email,
                    prestador.Nome,
                    pagamento.ValorLiquidoPrestador,
                    contrato.Projeto?.Titulo ?? "Projeto");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar e-mail de liberação para prestador.");
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

            pagamento.Status = StatusPagamento.EmDisputa;

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

            _logger.LogWarning("Disputa aberta para pagamento {PagamentoId} pelo usuário {UsuarioId}.", pagamentoId, usuarioId);

            return MapearDisputaDto(disputa);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RESOLVER DISPUTA (admin)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ResolverDisputaAsync(Guid pagamentoId, Guid disputaId, int adminId, ResolverDisputaDto dto)
        {
            var pagamento = await _repo.GetByIdAsync(pagamentoId)
                ?? throw new NegocioException("Pagamento não encontrado.");

            var disputa = pagamento.Disputas.FirstOrDefault(d => d.Id == disputaId)
                ?? throw new NegocioException("Disputa não encontrada.");

            if (disputa.Status != StatusDisputa.Aberta && disputa.Status != StatusDisputa.EmAnalise)
                throw new NegocioException("Esta disputa já foi resolvida.");

            disputa.Status = dto.FavorContratante
                ? StatusDisputa.ResolvidaAFavorContratante
                : StatusDisputa.ResolvidaAFavorPrestador;
            disputa.ResolvidoPorId = adminId;
            disputa.ResolvidaEm = DateTime.UtcNow;
            disputa.NotaResolucao = dto.NotaResolucao;

            if (dto.FavorContratante)
            {
                // Estorna o pagamento ao contratante
                await ExecutarEstornoAsync(pagamento, adminId);

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
                // Libera ao prestador
                pagamento.Status = StatusPagamento.Retido; // Remove EmDisputa para permitir liberação
                await ExecutarLiberacaoAsync(pagamento, adminId, $"Disputa resolvida a favor do prestador. {dto.NotaResolucao}");

                await _repo.AddLedgerAsync(new LedgerFinanceiro
                {
                    PagamentoId = pagamentoId,
                    Tipo = TipoEntradaLedger.DisputaResolvidaPrestador,
                    Valor = 0,
                    Descricao = $"Disputa resolvida a favor do prestador por admin {adminId}. Liberação iniciada.",
                    CriadoPorId = adminId
                });
            }

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
            }

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
                        return dt.GetDateTime();
                }
            }
            catch { }

            return null;
        }

        private static string MapearTipoPix(Domain.Enums.TipoPix tipo) => tipo switch
        {
            Domain.Enums.TipoPix.CPF => "CPF",
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
                        prestador.Email, prestador.Nome, titulo, pagamento.ValorLiquidoPrestador);
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
            TaxaPlataforma = p.TaxaPlataforma,
            ValorLiquidoPrestador = p.ValorLiquidoPrestador,
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
            TaxaPlataforma = p.TaxaPlataforma,
            ValorLiquidoPrestador = p.ValorLiquidoPrestador,
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
            TaxaPlataforma = p.TaxaPlataforma,
            ValorLiquidoPrestador = p.ValorLiquidoPrestador,
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
        // ESTORNO POR CANCELAMENTO DE CONTRATO (taxa de 5%)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task EstornarCancelamentoContratoAsync(Guid contratoServicoId, int usuarioId)
        {
            var pagamento = await _repo.GetByContratoServicoIdAsync(contratoServicoId);

            // Se não há pagamento retido em escrow, não há ação financeira
            if (pagamento == null || pagamento.Status != StatusPagamento.Retido)
                return;

            if (string.IsNullOrWhiteSpace(pagamento.GatewayPagamentoId))
            {
                _logger.LogWarning(
                    "Cancelamento do contrato {ContratoId}: pagamento {PagamentoId} sem ID de gateway — estorno ignorado.",
                    contratoServicoId, pagamento.Id);
                return;
            }

            // Taxa administrativa de 5% sobre o valor bruto
            var taxa = Math.Round(pagamento.ValorBruto * 0.05m, 2);
            var reembolso = pagamento.ValorBruto - taxa;

            try
            {
                // Estorno parcial de 95% via gateway (Asaas suporta valorParcial)
                await _gateway.EstornarCobrancaAsync(pagamento.GatewayPagamentoId, reembolso);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Falha ao estornar cobrança {CobrancaId} no gateway (cancelamento contrato {ContratoId}).",
                    pagamento.GatewayPagamentoId, contratoServicoId);
                throw new NegocioException("Não foi possível processar o reembolso no gateway. Tente novamente.");
            }

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.EstornoSolicitado,
                Valor = -reembolso,
                Descricao = $"Reembolso por cancelamento de contrato: 95% do valor bruto (R$ {reembolso:F2}).",
                CriadoPorId = usuarioId
            });

            await _repo.AddLedgerAsync(new LedgerFinanceiro
            {
                PagamentoId = pagamento.Id,
                Tipo = TipoEntradaLedger.TaxaPlataformaCobrada,
                Valor = taxa,
                Descricao = $"Taxa administrativa de cancelamento: 5% do valor bruto (R$ {taxa:F2}).",
                CriadoPorId = usuarioId
            });

            pagamento.Status = StatusPagamento.Estornado;

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Estorno por cancelamento processado: pagamento {PagamentoId}, reembolso {Reembolso}, taxa {Taxa}.",
                pagamento.Id, reembolso, taxa);
        }
    }
}
