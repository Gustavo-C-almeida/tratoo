using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Ações de segurança da própria conta, feitas por um usuário já autenticado e
    /// reautenticado (a API exige <c>reauth</c> recente antes de chamar). A revogação das
    /// sessões que essas ações provocam fica na API, que é quem conhece as sessões.
    /// </summary>
    public interface IContaService
    {
        /// <summary>Confere a senha do usuário (BCrypt). Não é o login: não equaliza tempo nem cria sessão.</summary>
        Task<Usuario?> ConferirSenhaAsync(int usuarioId, string senha);

        Task AlterarSenhaAsync(int usuarioId, string novaSenha, string confirmarNovaSenha, string ip);

        /// <summary>Liga ou desliga o MFA por e-mail. Admin não pode desligar (MFA obrigatório).</summary>
        Task AlterarMfaAsync(int usuarioId, bool ativo, string ip);
    }

    public class ContaService : IContaService
    {
        private readonly IUsuarioRepository _usuarios;
        private readonly IAuditLogRepository _audit;

        public ContaService(IUsuarioRepository usuarios, IAuditLogRepository audit)
        {
            _usuarios = usuarios;
            _audit = audit;
        }

        public async Task<Usuario?> ConferirSenhaAsync(int usuarioId, string senha)
        {
            var usuario = await _usuarios.ObterPorIdAsync(usuarioId);
            if (usuario is null || usuario.Status != StatusUsuario.Active)
                return null;

            return PasswordHasher.Verify(senha, usuario.SenhaHash) ? usuario : null;
        }

        public async Task AlterarSenhaAsync(int usuarioId, string novaSenha, string confirmarNovaSenha, string ip)
        {
            LoginValidator.ValidarNovaSenha(novaSenha, confirmarNovaSenha);

            var usuario = await ObterAtivoAsync(usuarioId);
            if (PasswordHasher.Verify(novaSenha, usuario.SenhaHash))
                throw new NegocioException("A nova senha precisa ser diferente da atual.");

            usuario.SenhaHash = PasswordHasher.Hash(novaSenha);
            await _usuarios.AtualizarAsync(usuario);
            await _audit.RegistrarAsync(usuarioId, "senha_alterada", ip);
        }

        public async Task AlterarMfaAsync(int usuarioId, bool ativo, string ip)
        {
            var usuario = await ObterAtivoAsync(usuarioId);
            if (!ativo && usuario.IsAdmin)
                throw new NegocioException("O MFA é obrigatório para administradores.");
            if (usuario.MFA == ativo)
                throw new NegocioException(ativo ? "O MFA já está ativo." : "O MFA já está desativado.");

            usuario.MFA = ativo;
            await _usuarios.AtualizarAsync(usuario);
            await _audit.RegistrarAsync(usuarioId, ativo ? "mfa_ativado" : "mfa_desativado", ip);
        }

        private async Task<Usuario> ObterAtivoAsync(int usuarioId)
        {
            var usuario = await _usuarios.ObterPorIdAsync(usuarioId);
            if (usuario is null || usuario.Status != StatusUsuario.Active)
                throw new NegocioException("Conta indisponível.");
            return usuario;
        }
    }
}
