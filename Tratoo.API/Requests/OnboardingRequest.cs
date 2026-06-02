namespace Tratoo.API.Requests
{
    /// <summary>
    /// Dados enviados no onboarding pós-login para completar o perfil mínimo.
    /// </summary>
    public record OnboardingRequest(
        /// <summary>"PessoaFisica" ou "PessoaJuridica"</summary>
        string TipoPessoa,

        // ── Documento principal ───────────────────────────────────────────────
        /// <summary>CPF (PF, 11 dígitos) ou CNPJ (PJ, 14 dígitos) — sem formatação.</summary>
        string CpfCnpj,

        /// <summary>Nome completo (PF) ou Razão Social (PJ).</summary>
        string NomeLegal,

        // ── PF — data de nascimento ───────────────────────────────────────────
        /// <summary>Data de nascimento. Obrigatório para Pessoa Física.</summary>
        DateOnly? DataNascimento,

        /// <summary>Exibir a idade publicamente no perfil. Opcional, padrão false.</summary>
        bool ExibirIdade,

        // ── PJ — identificação da empresa ─────────────────────────────────────
        /// <summary>Nome fantasia. Opcional para PJ.</summary>
        string? NomeEmpresa,

        /// <summary>Segmento de atuação. Obrigatório para PJ.</summary>
        string? Segmento,

        /// <summary>Inscrição Estadual. Opcional para PJ.</summary>
        string? InscricaoEstadual,

        /// <summary>Inscrição Municipal. Opcional para PJ.</summary>
        string? InscricaoMunicipal,

        /// <summary>Data de abertura da empresa. Opcional para PJ.</summary>
        DateOnly? DataAbertura,

        // ── PJ — representante legal ──────────────────────────────────────────
        /// <summary>Nome completo do representante legal. Obrigatório para PJ.</summary>
        string? NomeRepresentanteLegal,

        /// <summary>CPF do representante legal (11 dígitos, sem formatação). Obrigatório para PJ.</summary>
        string? CpfRepresentanteLegal,

        /// <summary>Cargo do representante legal. Opcional para PJ.</summary>
        string? CargoRepresentante,

        /// <summary>E-mail do representante legal. Opcional para PJ.</summary>
        string? EmailRepresentante,

        /// <summary>Telefone/WhatsApp do representante legal. Opcional para PJ.</summary>
        string? TelefoneRepresentante,

        // ── Endereço completo ─────────────────────────────────────────────────
        string Cep,
        string Logradouro,
        string Numero,
        string? Complemento,
        string Bairro,
        string Cidade,
        string Estado
    );
}
