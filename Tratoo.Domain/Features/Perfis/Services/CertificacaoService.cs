using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class CertificacaoService
    {
        private readonly IPrestadorRepository _repo;
        private readonly PrestadorIndexadorService _indexador;

        public CertificacaoService(IPrestadorRepository repo, PrestadorIndexadorService indexador)
        {
            _repo = repo;
            _indexador = indexador;
        }

        public async Task<List<CertificacaoDTO>> VisualizarAsync(int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            return prestador.Certificacoes.Select(Mapear).ToList();
        }

        public async Task AdicionarAsync(CertificacaoDTO dto)
        {
            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var cert = new CertificacaoPrestador
            {
                PrestadorId        = dto.PrestadorId,
                Nome               = dto.Nome,
                InstituicaoEmissora = dto.Instituicao,
                DataEmissao        = dto.DataEmissao,
                DataValidade       = dto.DataValidade,
                LinkVerificacao    = dto.LinkVerificacao
            };

            prestador.Certificacoes.Add(cert);
            prestador.PorcentagemCompleto = PerfilProfissaoPrestadorService.CalcularCompletude(prestador);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task EditarAsync(CertificacaoDTO dto)
        {
            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var cert = prestador.Certificacoes.FirstOrDefault(c => c.Id == dto.Id)
                ?? throw new NegocioException("Certificação não encontrada");

            cert.Nome               = dto.Nome;
            cert.InstituicaoEmissora = dto.Instituicao;
            cert.DataEmissao        = dto.DataEmissao;
            cert.DataValidade       = dto.DataValidade;
            cert.LinkVerificacao    = dto.LinkVerificacao;

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task RemoverAsync(int certificacaoId, int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var cert = prestador.Certificacoes.FirstOrDefault(c => c.Id == certificacaoId);

            if (cert == null) return;

            prestador.Certificacoes.Remove(cert);
            prestador.PorcentagemCompleto = PerfilProfissaoPrestadorService.CalcularCompletude(prestador);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(prestadorId);
        }

        private static CertificacaoDTO Mapear(CertificacaoPrestador c) => new()
        {
            Id              = c.Id,
            PrestadorId     = c.PrestadorId,
            Nome            = c.Nome,
            Instituicao     = c.InstituicaoEmissora,
            DataEmissao     = c.DataEmissao,
            DataValidade    = c.DataValidade,
            LinkVerificacao = c.LinkVerificacao
        };
    }
}
