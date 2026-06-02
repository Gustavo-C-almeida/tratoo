namespace Tratoo.API.Requests
{
    /// <summary>
    /// Payload para assinar digitalmente um contrato.
    /// Confirma que o usuário leu e aceita o conteúdo exibido na tela.
    /// </summary>
    public class AssinarContratoRequest
    {
        /// <summary>Confirmação explícita de que o usuário leu o contrato. Deve ser true.</summary>
        public bool Confirmo { get; set; }
    }
}
