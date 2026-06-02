namespace Tratoo.Domain.Enums
{
    /// <summary>
    /// Indica qual parte criou a proposta formal.
    /// Prestador → fluxo tradicional (prestador propõe, contratante aceita).
    /// Contratante → fluxo reverso pós-convite (contratante propõe, prestador aceita).
    /// </summary>
    public enum PropostaSenderType
    {
        Prestador,
        Contratante
    }
}
