using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Auth
{
    public class ExclusaoContaService : IExclusaoContaService
    {
        private readonly IUsuarioRepository _usuarioRepo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly IAuditLogRepository _auditRepo;

        public ExclusaoContaService(
            IUsuarioRepository usuarioRepo,
            IIdentidadeRepository identidadeRepo,
            IAuditLogRepository auditRepo)
        {
            _usuarioRepo = usuarioRepo;
            _identidadeRepo = identidadeRepo;
            _auditRepo = auditRepo;
        }

        /// <summary>
        /// Executa a exclusão de dados pessoais conforme LGPD Art. 18:
        /// 1. Muda Status para Blocked
        /// 2. Substitui nome e e-mail por "[removido]"
        /// 3. Remove UserIdentity (CPF/CNPJ)
        /// 4. Contratos são preservados (obrigação legal)
        /// </summary>
        public async Task ExcluirAsync(ExcluirContaDTO dto)
        {
            var usuario = await _usuarioRepo.ObterPorIdAsync(dto.UserId)
                ?? throw new NegocioException("Usuário não encontrado");

            if (usuario.Status == StatusUsuario.Blocked)
                throw new NegocioException("Conta já excluída");

            // Registra auditoria antes de apagar os dados
            await _auditRepo.RegistrarAsync(dto.UserId, "conta_excluida", dto.Ip);

            // Remove dados de identidade (CPF/CNPJ)
            var identity = await _identidadeRepo.ObterPorUserIdAsync(dto.UserId);
            if (identity is not null)
                await _identidadeRepo.RemoverAsync(identity);

            // Anonimiza dados pessoais
            usuario.Nome = "[removido]";
            usuario.Email = $"[removido]-{dto.UserId}";   // garante unicidade no índice
            usuario.Status = StatusUsuario.Blocked;

            await _usuarioRepo.AtualizarAsync(usuario);
        }
    }
}
