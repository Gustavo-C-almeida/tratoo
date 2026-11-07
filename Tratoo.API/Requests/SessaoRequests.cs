namespace Tratoo.API.Requests
{
    /// <summary>Reautenticação: primeiro a senha; com MFA ativo (ou Admin), depois o código.</summary>
    public record ReautenticarRequest(string? Senha, string? Codigo);

    public record AlterarSenhaRequest(string NovaSenha, string ConfirmarNovaSenha);

    public record AlterarMfaRequest(bool Ativo);
}
