namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Snapshot imutável dos dados das partes, congelado no momento da primeira assinatura.
    /// Garante que alterações posteriores em Usuario/UserIdentity não afetem o contrato assinado.
    /// Nunca deve ser atualizado após a criação.
    /// </summary>
    public class ContratoSnapshot
    {
        public int Id { get; set; }

        public Guid ContratoId { get; set; }
        public ContratoServico Contrato { get; set; } = null!;

        /// <summary>JSON com nome, CPF/CNPJ (mascarado), e-mail e endereço do contratante no momento da assinatura.</summary>
        public string DadosContratante { get; set; } = "{}";

        /// <summary>JSON com nome, CPF/CNPJ (mascarado), e-mail e endereço do prestador no momento da assinatura.</summary>
        public string DadosPrestador { get; set; } = "{}";

        /// <summary>Cópia do ConteudoJson congelado — o contrato exato que foi assinado.</summary>
        public string ConteudoFinal { get; set; } = "{}";

        public DateTime CongeladoEm { get; set; } = DateTime.UtcNow;
    }
}
