using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Armazena dados de identidade sensíveis de forma separada do Usuário.
    /// CPF/CNPJ são sempre criptografados em repouso (AES-256) e nunca
    /// aparecem em respostas de API pública — LGPD Art. 46.
    /// </summary>
    public class UserIdentity
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public Usuario Usuario { get; set; } = null!;

        /// <summary>CPF (PF) ou CNPJ (PJ) — criptografado com AES-256.</summary>
        public string CpfCnpjCriptografado { get; set; } = string.Empty;

        /// <summary>Nome completo (PF) ou Razão Social (PJ).</summary>
        public string NomeLegal { get; set; } = string.Empty;

        /// <summary>
        /// Nível 2 = identidade validada; Nível 3 = dados bancários cadastrados.
        /// </summary>
        public NivelVerificacao NivelVerificacao { get; set; } = NivelVerificacao.Identidade;

        /// <summary>Chave PIX — criptografada com AES-256. Apenas prestadores Nível 3.</summary>
        public string? ChavePixCriptografada { get; set; }

        // ── Representante legal (somente PJ) ─────────────────────────────────────
        /// <summary>CPF do representante legal — criptografado com AES-256. Obrigatório para PJ.</summary>
        public string? CpfRepresentanteLegalCriptografado { get; set; }

        /// <summary>Nome completo do representante legal. Obrigatório para PJ.</summary>
        public string? NomeRepresentanteLegal { get; set; }

        /// <summary>Cargo do representante legal na empresa. Opcional.</summary>
        public string? CargoRepresentanteLegal { get; set; }

        /// <summary>E-mail do representante legal. Opcional.</summary>
        public string? EmailRepresentanteLegal { get; set; }

        /// <summary>Telefone/WhatsApp do representante legal. Opcional.</summary>
        public string? TelefoneRepresentanteLegal { get; set; }

        // ── Pessoa Física ─────────────────────────────────────────────────────
        /// <summary>Data de nascimento (PF). Armazenada e nunca exposta em APIs públicas.</summary>
        public DateOnly? DataNascimento { get; set; }

        /// <summary>Consentimento para exibir idade publicamente no perfil (PF).</summary>
        public bool ExibirIdade { get; set; } = false;

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    }
}
