using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Features.Auth
{
    public record ConfirmarCadastroUserDTO
    {
        public string Email { get; init; }
        public string Codigo { get; init; }
    }
}
