namespace Tratoo.API.Requests
{
    public record AtualizarPerfilContratanteRequest(
        string? Descricao,
        string? SiteUrl,
        string? LinkedinUrl,
        string? Telefone,
        bool ExibirIdade
    );
}
