namespace Tratoo.API.Requests
{
    public record PostCompetenciaRequest(
    string Nome,
    int Nivel
    );
    public record PutCompetenciaRequest(
        int Nivel
    );
}
