namespace Tratoo.API.Requests
{
    public record ResetarSenhaRequest(string Email, string Codigo, string NovaSenha, string ConfirmarNovaSenha);
}
