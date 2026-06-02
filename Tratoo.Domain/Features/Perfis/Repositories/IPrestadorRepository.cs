using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.Perfis
{
    public interface IPrestadorRepository
    {
        Task<Prestador?> GetByIdAsync(int id);

        Task<Prestador?> GetCompletoAsync(int id);

        Task UpdateAsync(Prestador prestador);

        Task SaveAsync();
    }


}
