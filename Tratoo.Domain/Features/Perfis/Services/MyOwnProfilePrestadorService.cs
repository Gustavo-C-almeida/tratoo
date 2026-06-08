using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class MyOwnProfilePrestadorService
    {
        private readonly IPrestadorRepository _repo;
        private readonly IIdentidadeRepository _identidadeRepo;

        public MyOwnProfilePrestadorService(
            IPrestadorRepository repo,
            IIdentidadeRepository identidadeRepo)
        {
            _repo = repo;
            _identidadeRepo = identidadeRepo;
        }

        public async Task<MyOwnProfilePrestadorDTO> VisualizarAsync(int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var identity = await _identidadeRepo.ObterPorUserIdAsync(prestadorId);

            return new MyOwnProfilePrestadorDTO
            {
                Id = prestador.Id,
                Nome = prestador.Nome,
                Email = prestador.Email,

                TituloProfissional  = prestador.TituloProfissional,
                Descricao           = prestador.Descricao,
                FotoUrl             = prestador.FotoUrl,
                EmailContato        = prestador.EmailContato,
                OutrosLinks         = prestador.OutrosLinks,
                PorcentagemCompleto = prestador.PorcentagemCompleto,

                AreaEspecializacao      = prestador.AreaEspecializacao,
                FuncaoExecutada         = prestador.FuncaoExecutada,
                Telefone                = prestador.Telefone,
                LinkedinUrl             = prestador.LinkedinUrl,
                PortfolioUrl            = prestador.PortfolioUrl,
                Disponivel              = prestador.Disponivel,
                DisponibilidadesPrivado = prestador.DisponibilidadesPrivado,
                AvaliacoesPrivado       = prestador.AvaliacoesPrivado,
                LocalizacaoCidade       = prestador.Endereco?.Cidade,
                LocalizacaoEstado       = prestador.Endereco?.Estado,
                NivelVerificacao        = identity?.NivelVerificacao,

                Competencias = prestador.Competencias.Select(c => new CompetenciaDTO
                {
                    Id          = c.Id,
                    PrestadorId = c.PrestadorId,
                    Nome        = c.Nome,
                    Nivel       = c.Nivel
                }).ToList(),

                Certificacoes = prestador.Certificacoes.Select(c => new CertificacaoDTO
                {
                    Id              = c.Id,
                    PrestadorId     = c.PrestadorId,
                    Nome            = c.Nome,
                    Instituicao     = c.InstituicaoEmissora,
                    DataEmissao     = c.DataEmissao,
                    DataValidade    = c.DataValidade,
                    LinkVerificacao = c.LinkVerificacao,
                    ArquivoUrl      = c.ArquivoUrl,
                    Competencias    = c.CompetenciaCertificacoes
                        .Select(cc => new CompetenciaDTO
                        {
                            Id    = cc.Competencia.Id,
                            Nome  = cc.Competencia.Nome,
                            Nivel = cc.Competencia.Nivel
                        }).ToList()
                }).ToList(),

                Experiencias = prestador.Experiencias.Select(e => new ExperienciaDTO
                {
                    Id           = e.Id,
                    PrestadorId  = e.PrestadorId,
                    Cargo        = e.Cargo,
                    Empresa      = e.Empresa,
                    Atividades   = e.Atividades,
                    DataInicio   = e.DataInicio,
                    DataFim      = e.DataFim,
                    EmpregoAtual = e.EmpregoAtual,
                    Local        = e.Local,
                    TipoContrato = e.TipoContrato,
                    Competencias = e.CompetenciaExperiencias
                        .Select(ce => new CompetenciaDTO
                        {
                            Id    = ce.Competencia.Id,
                            Nome  = ce.Competencia.Nome,
                            Nivel = ce.Competencia.Nivel
                        }).ToList()
                }).ToList(),

                Portfolio = prestador.Portfolio.Select(p => new PortfolioDTO
                {
                    Id          = p.Id,
                    PrestadorId = p.PrestadorId,
                    Titulo      = p.Titulo,
                    Descricao   = p.Descricao,
                    LinkExterno = p.LinkExterno,
                    ArquivoUrl  = p.ArquivoUrl,
                    CriadoEm   = p.CriadoEm,
                    Competencias = p.CompetenciaPortfolios
                        .Select(cp => new CompetenciaDTO
                        {
                            Id    = cp.Competencia.Id,
                            Nome  = cp.Competencia.Nome,
                            Nivel = cp.Competencia.Nivel
                        }).ToList()
                }).ToList()
            };
        }
    }
}
