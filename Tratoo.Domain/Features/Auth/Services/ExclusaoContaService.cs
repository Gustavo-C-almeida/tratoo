using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.IA;

namespace Tratoo.Domain.Features.Auth
{
    public class ExclusaoContaService : IExclusaoContaService
    {
        /// <summary>Rótulo exibido publicamente no lugar do nome de uma conta excluída.</summary>
        public const string NomeAnonimizado = "Usuário indisponível";

        private readonly IUsuarioRepository _usuarioRepo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly IAuditLogRepository _auditRepo;
        private readonly PrestadorIndexadorService _prestadorIndexador;

        public ExclusaoContaService(
            IUsuarioRepository usuarioRepo,
            IIdentidadeRepository identidadeRepo,
            IAuditLogRepository auditRepo,
            PrestadorIndexadorService prestadorIndexador)
        {
            _usuarioRepo = usuarioRepo;
            _identidadeRepo = identidadeRepo;
            _auditRepo = auditRepo;
            _prestadorIndexador = prestadorIndexador;
        }

        /// <summary>
        /// Executa a exclusão de conta via Soft Delete (LGPD Art. 18 — direito ao esquecimento):
        /// 1. Marca ExcluidoEm e Status = Blocked (bloqueia autenticação).
        /// 2. Anonimiza nome ("Usuário indisponível") e e-mail; remove foto/logo.
        /// 3. Remove UserIdentity (CPF/CNPJ).
        /// 4. Remove o prestador do índice de busca (não aparece mais em buscas).
        /// 5. NÃO apaga fisicamente o registro nem os históricos (projetos, propostas,
        ///    contratos, pagamentos, avaliações) — preservados por obrigação legal.
        /// </summary>
        public async Task ExcluirAsync(ExcluirContaDTO dto)
        {
            var usuario = await _usuarioRepo.ObterPorIdAsync(dto.UserId)
                ?? throw new NegocioException("Usuário não encontrado");

            if (usuario.ExcluidoEm != null || usuario.Status == StatusUsuario.Blocked)
                throw new NegocioException("Conta já excluída");

            // Registra auditoria antes de anonimizar
            await _auditRepo.RegistrarAsync(dto.UserId, "conta_excluida", dto.Ip);

            // Remove dados de identidade (CPF/CNPJ)
            var identity = await _identidadeRepo.ObterPorUserIdAsync(dto.UserId);
            if (identity is not null)
                await _identidadeRepo.RemoverAsync(identity);

            // Soft Delete + anonimização dos dados pessoais
            usuario.ExcluidoEm = DateTime.UtcNow;
            usuario.Status = StatusUsuario.Blocked;
            usuario.Nome = NomeAnonimizado;
            usuario.Email = $"removido-{dto.UserId}@indisponivel.local"; // mantém unicidade no índice
            usuario.Telefone = null;

            // Remove informações identificáveis específicas do tipo de usuário
            if (usuario is Models.Prestador.Prestador prestador)
            {
                prestador.FotoUrl = null;
                await _prestadorIndexador.RemoverAsync(dto.UserId);
            }
            else if (usuario is Models.Contratante contratante)
            {
                contratante.LogoUrl = null;
            }

            await _usuarioRepo.AtualizarAsync(usuario);
        }
    }
}
