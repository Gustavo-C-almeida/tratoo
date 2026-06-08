using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class PortfolioService
    {
        private readonly IPrestadorRepository _repo;
        private readonly IArquivoStorageService _storage;
        private readonly PrestadorIndexadorService _indexador;

        public PortfolioService(
            IPrestadorRepository repo,
            IArquivoStorageService storage,
            PrestadorIndexadorService indexador)
        {
            _repo = repo;
            _storage = storage;
            _indexador = indexador;
        }

        public async Task<PortfolioDTO> AdicionarAsync(PortfolioDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.LinkExterno) && string.IsNullOrWhiteSpace(dto.ArquivoUrl))
                throw new NegocioException("Informe pelo menos um link externo ou faça upload de um arquivo.");

            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var item = new PortfolioPrestador
            {
                PrestadorId = dto.PrestadorId,
                Titulo      = dto.Titulo,
                Descricao   = dto.Descricao,
                LinkExterno = dto.LinkExterno,
                ArquivoUrl  = dto.ArquivoUrl,
                CriadoEm   = DateTime.UtcNow
            };

            prestador.Portfolio.Add(item);
            prestador.PorcentagemCompleto = PerfilProfissaoPrestadorService.CalcularCompletude(prestador);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);

            dto.Id       = item.Id;
            dto.CriadoEm = item.CriadoEm;
            return dto;
        }

        public async Task EditarAsync(PortfolioDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.LinkExterno) && string.IsNullOrWhiteSpace(dto.ArquivoUrl))
                throw new NegocioException("Informe pelo menos um link externo ou um arquivo.");

            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var item = prestador.Portfolio.FirstOrDefault(p => p.Id == dto.Id)
                ?? throw new NegocioException("Item de portfólio não encontrado");

            // Se o arquivo mudou (substituído ou removido), excluir o antigo do R2
            if (!string.IsNullOrEmpty(item.ArquivoUrl) && item.ArquivoUrl != dto.ArquivoUrl)
                await _storage.ExcluirAsync(item.ArquivoUrl);

            item.Titulo      = dto.Titulo;
            item.Descricao   = dto.Descricao;
            item.LinkExterno = dto.LinkExterno;
            item.ArquivoUrl  = dto.ArquivoUrl;

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task RemoverAsync(int portfolioId, int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var item = prestador.Portfolio.FirstOrDefault(p => p.Id == portfolioId);
            if (item == null) return;

            // Remover arquivo do R2 se existir
            if (!string.IsNullOrEmpty(item.ArquivoUrl))
                await _storage.ExcluirAsync(item.ArquivoUrl);

            prestador.Portfolio.Remove(item);
            prestador.PorcentagemCompleto = PerfilProfissaoPrestadorService.CalcularCompletude(prestador);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(prestadorId);
        }

        private static List<PortfolioDTO> MapearLista(IEnumerable<PortfolioPrestador> items) =>
            items.Select(p => new PortfolioDTO
            {
                Id          = p.Id,
                PrestadorId = p.PrestadorId,
                Titulo      = p.Titulo,
                Descricao   = p.Descricao,
                LinkExterno = p.LinkExterno,
                ArquivoUrl  = p.ArquivoUrl,
                CriadoEm   = p.CriadoEm
            }).ToList();
    }
}
