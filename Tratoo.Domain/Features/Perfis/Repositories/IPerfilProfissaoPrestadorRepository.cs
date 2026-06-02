using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.Perfis
{
    public interface IPerfilProfissaoPrestadorRepository
    {
        Task<Prestador?> GetCompletoAsync(int id);

        Task SaveAsync();
    }
}
