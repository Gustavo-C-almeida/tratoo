using System.Text.Json;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Pagamentos
{
    /// <summary>
    /// Orquestra a administração de disputas (área Admin): listagem com filtros,
    /// detalhe completo e resolução. A resolução delega a IPagamentoService, que
    /// mantém a consistência entre disputa, pagamento e contrato + auditoria.
    /// </summary>
    public class AdminDisputaService
    {
        private readonly IPagamentoRepository _repo;
        private readonly IPagamentoService _pagamentoService;

        public AdminDisputaService(IPagamentoRepository repo, IPagamentoService pagamentoService)
        {
            _repo = repo;
            _pagamentoService = pagamentoService;
        }

        public async Task<List<DisputaAdminResumoDto>> ListarAsync(
            StatusDisputa? status, DateTime? de, DateTime? ate,
            int? contratanteId, int? prestadorId, int? projetoId)
        {
            var disputas = await _repo.GetTodasDisputasAsync(status, de, ate, contratanteId, prestadorId, projetoId);
            return disputas.Select(MapResumo).ToList();
        }

        public async Task<DisputaAdminDetalheDto> ObterDetalheAsync(Guid disputaId)
        {
            var d = await _repo.GetDisputaComContextoAsync(disputaId)
                ?? throw new NegocioException("Disputa não encontrada.");

            var contrato = d.Pagamento.ContratoServico;

            var historico = contrato != null
                ? await _repo.GetHistoricoContratoAsync(contrato.Id)
                : new List<Tratoo.Domain.Models.HistoricoContrato>();

            var resumo = MapResumo(d);

            return new DisputaAdminDetalheDto
            {
                DisputaId = resumo.DisputaId,
                PagamentoId = resumo.PagamentoId,
                Status = resumo.Status,
                AbertoEm = resumo.AbertoEm,
                ResolvidaEm = resumo.ResolvidaEm,
                Motivo = resumo.Motivo,
                ProjetoId = resumo.ProjetoId,
                ProjetoTitulo = resumo.ProjetoTitulo,
                ContratanteId = resumo.ContratanteId,
                ContratanteNome = resumo.ContratanteNome,
                PrestadorId = resumo.PrestadorId,
                PrestadorNome = resumo.PrestadorNome,
                ValorNegociado = resumo.ValorNegociado,

                AbertoPorId = d.AbertoPorId,
                NotaResolucao = d.NotaResolucao,
                ResolvidoPorId = d.ResolvidoPorId,
                Descricao = d.Motivo,
                Evidencias = ParseEvidencias(d.EvidenciasJson),

                StatusPagamento = d.Pagamento.Status,
                StatusContrato = contrato?.Status,
                ProjetoDescricao = contrato?.Projeto?.Descricao ?? string.Empty,
                ContratoId = contrato?.Id,
                ContratoCriadoEm = contrato?.CriadoEm,

                HistoricoContrato = historico.Select(h => new HistoricoEventoDto
                {
                    Acao = h.Acao.ToString(),
                    Descricao = h.Descricao,
                    UsuarioId = h.UsuarioId,
                    DataEvento = h.DataEvento
                }).ToList(),

                Ledger = d.Pagamento.Ledger
                    .OrderBy(l => l.CriadoEm)
                    .Select(l => new LedgerEntradaDto
                    {
                        Id = l.Id,
                        Tipo = l.Tipo,
                        Valor = l.Valor,
                        Descricao = l.Descricao,
                        ReferenciaExterna = l.ReferenciaExterna,
                        CriadoEm = l.CriadoEm
                    }).ToList()
            };
        }

        public async Task ResolverAsync(Guid disputaId, int adminId, ResolverDisputaAdminRequest req, string ip)
        {
            var d = await _repo.GetDisputaComContextoAsync(disputaId)
                ?? throw new NegocioException("Disputa não encontrada.");

            await _pagamentoService.ResolverDisputaAsync(
                d.PagamentoId, disputaId, adminId,
                new ResolverDisputaDto(req.FavorContratante, req.NotaResolucao),
                ip);
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static DisputaAdminResumoDto MapResumo(DisputaPagamento d)
        {
            var contrato = d.Pagamento.ContratoServico;
            return new DisputaAdminResumoDto
            {
                DisputaId = d.Id,
                PagamentoId = d.PagamentoId,
                Status = d.Status,
                AbertoEm = DateTime.SpecifyKind(d.AbertoEm, DateTimeKind.Utc),
                ResolvidaEm = d.ResolvidaEm.HasValue
                    ? DateTime.SpecifyKind(d.ResolvidaEm.Value, DateTimeKind.Utc) : null,
                Motivo = d.Motivo,
                ProjetoId = contrato?.ProjetoId ?? 0,
                ProjetoTitulo = contrato?.Projeto?.Titulo ?? "—",
                ContratanteId = contrato?.ContratanteId ?? 0,
                ContratanteNome = contrato?.Contratante?.Nome ?? "—",
                PrestadorId = contrato?.PrestadorId ?? 0,
                PrestadorNome = contrato?.Prestador?.Nome ?? "—",
                ValorNegociado = d.Pagamento.ValorBruto
            };
        }

        private static List<string> ParseEvidencias(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
            catch { return []; }
        }
    }
}
