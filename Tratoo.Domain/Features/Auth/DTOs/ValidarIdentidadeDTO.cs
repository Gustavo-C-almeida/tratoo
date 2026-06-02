namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Etapa 3 do cadastro: validação de CPF (PF) ou CNPJ (PJ).
    /// Disparada na primeira tentativa de criar proposta ou gerar contrato.
    /// </summary>
    public class ValidarIdentidadeDTO
    {
        public int UserId { get; set; }

        /// <summary>CPF (11 dígitos) ou CNPJ (14 dígitos) — sem formatação.</summary>
        public string CpfCnpj { get; set; } = string.Empty;

        /// <summary>Nome completo (PF) ou Razão Social (PJ).</summary>
        public string NomeLegal { get; set; } = string.Empty;

        /// <summary>CPF do representante legal — obrigatório para PJ.</summary>
        public string? CpfRepresentanteLegal { get; set; }

        /// <summary>Nome completo do representante legal — obrigatório para PJ.</summary>
        public string? NomeRepresentanteLegal { get; set; }

        /// <summary>Cargo do representante legal. Opcional.</summary>
        public string? CargoRepresentanteLegal { get; set; }

        /// <summary>E-mail do representante legal. Opcional.</summary>
        public string? EmailRepresentanteLegal { get; set; }

        /// <summary>Telefone/WhatsApp do representante legal. Opcional.</summary>
        public string? TelefoneRepresentanteLegal { get; set; }

        /// <summary>Data de nascimento — obrigatório para PF.</summary>
        public DateOnly? DataNascimento { get; set; }

        /// <summary>Exibir idade publicamente no perfil (PF).</summary>
        public bool ExibirIdade { get; set; } = false;

        /// <summary>IP do cliente — preenchido pela camada de API.</summary>
        public string Ip { get; set; } = string.Empty;
    }
}
