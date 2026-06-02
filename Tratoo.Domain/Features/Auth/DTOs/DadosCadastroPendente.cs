namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Estado temporário do cadastro, mantido em cache até confirmação de e-mail.
    /// CPF/CNPJ não faz parte deste registro — eles são coletados na Etapa 3.
    /// </summary>
    public record DadosCadastroPendente(
        string Nome,
        string Email,
        string Tipo,
        bool MFA,
        string SenhaHash,
        string Ip,
        bool AceitouTermos);
}
