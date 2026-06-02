using System.ComponentModel.DataAnnotations;
using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    public enum TipoUsuario
    {
        Prestador,
        Contratante
    }

    public abstract class Usuario
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Nome { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string SenhaHash { get; set; } = string.Empty;

        [Required]
        public TipoUsuario TipoUsuario { get; set; }

        /// <summary>
        /// Pending → e-mail não confirmado.
        /// Active  → e-mail confirmado, pode fazer login.
        /// Blocked → conta bloqueada ou excluída (LGPD).
        /// </summary>
        public StatusUsuario Status { get; set; } = StatusUsuario.Pending;

        public bool MFA { get; set; } = false;

        public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

        public bool PerfilMinimoCompleto { get; protected set; }

        /// <summary>
        /// Definido como true após CPF/CNPJ ser validado e persistido no UserIdentity.
        /// Não armazena o documento — apenas sinaliza que a identidade foi verificada.
        /// </summary>
        public bool IdentidadeVerificada { get; set; }

        // ── Campos compartilhados entre Prestador e Contratante ───────────────

        /// <summary>PF (CPF) ou PJ (CNPJ). Definido no onboarding.</summary>
        public TipoPessoa? TipoPessoa { get; set; }

        /// <summary>Endereço de operação — sede para PJ, comercial/residencial para PF.</summary>
        public Endereco? Endereco { get; set; }

        /// <summary>Telefone de contato (nunca exposto em APIs públicas).</summary>
        public string? Telefone { get; set; }

        /// <summary>Quando true, nenhuma avaliação é exibida publicamente.</summary>
        public bool AvaliacoesPrivado { get; set; } = false;

        public abstract void VerificarPerfilMinimo();
    }
}
