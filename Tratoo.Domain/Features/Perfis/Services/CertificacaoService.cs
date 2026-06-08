using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class CertificacaoService
    {
        private readonly IPrestadorRepository _repo;
        private readonly IArquivoStorageService _storage;
        private readonly PrestadorIndexadorService _indexador;

        public CertificacaoService(
            IPrestadorRepository repo,
            IArquivoStorageService storage,
            PrestadorIndexadorService indexador)
        {
            _repo = repo;
            _storage = storage;
            _indexador = indexador;
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
                LinkVerificacao    = dto.LinkVerificacao,
                ArquivoUrl         = dto.ArquivoUrl
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

            // Se o arquivo mudou (substituído ou removido), exclui o antigo do R2.
            if (!string.IsNullOrEmpty(cert.ArquivoUrl) && cert.ArquivoUrl != dto.ArquivoUrl)
                await _storage.ExcluirAsync(cert.ArquivoUrl);

            cert.Nome               = dto.Nome;
            cert.InstituicaoEmissora = dto.Instituicao;
            cert.DataEmissao        = dto.DataEmissao;
            cert.DataValidade       = dto.DataValidade;
            cert.LinkVerificacao    = dto.LinkVerificacao;
            cert.ArquivoUrl         = dto.ArquivoUrl;

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task RemoverAsync(int certificacaoId, int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var cert = prestador.Certificacoes.FirstOrDefault(c => c.Id == certificacaoId);

            if (cert == null) return;

            // Remove o arquivo do R2, se existir.
            if (!string.IsNullOrEmpty(cert.ArquivoUrl))
                await _storage.ExcluirAsync(cert.ArquivoUrl);

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
            LinkVerificacao = c.LinkVerificacao,
            ArquivoUrl      = c.ArquivoUrl
        };
    }
}
