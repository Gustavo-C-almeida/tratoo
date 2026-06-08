namespace Tratoo.API.Requests
{
    public record AtualizarPerfilContratanteRequest(
        string? Descricao,
        string? SiteUrl,
        string? LinkedinUrl,
        string? EmailContato,
        string? Telefone,
        bool ExibirIdade,
        string? Segmento,
        string? NomeEmpresa,
        string? Disponibilidade,
        List<string>? IdiomasAceitos,
        string? TamanhoEquipe,
        string? PorQueTrabalharComigo
    );
}
