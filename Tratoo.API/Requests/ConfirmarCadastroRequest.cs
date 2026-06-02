using System.ComponentModel.DataAnnotations;

namespace Tratoo.API.Requests
{
    public record ConfirmarCadastroRequest(
        [property: MaxLength(254)] string Email,
        [property: MaxLength(6)]   string Codigo
    );
}
