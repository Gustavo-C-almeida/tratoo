namespace Tratoo.API.Requests
{
    /// <summary>
    /// Payload para assinar digitalmente um contrato.
    /// Requer o OTP enviado por e-mail via POST /api/contratos/{id}/assinar/solicitar-otp.
    /// </summary>
    public class AssinarContratoRequest
    {
        /// <summary>Confirmação explícita de que o usuário leu o contrato. Deve ser true.</summary>
        public bool Confirmo { get; set; }

        /// <summary>Código de 6 dígitos enviado ao e-mail do usuário. Obrigatório.</summary>
        public string Otp { get; set; } = string.Empty;
    }
}
