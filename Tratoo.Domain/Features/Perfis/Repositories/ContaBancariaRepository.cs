using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Perfis
{
    public class ContaBancariaRepository : IContaBancariaRepository
    {
        private readonly TratooContext _ctx;

        public ContaBancariaRepository(TratooContext ctx) => _ctx = ctx;

        public async Task<ContaBancaria?> GetByPrestadorIdAsync(int prestadorId)
            => await _ctx.ContasBancarias
                .FirstOrDefaultAsync(c => c.PrestadorId == prestadorId);

        public async Task AddAsync(ContaBancaria conta)
            => await _ctx.ContasBancarias.AddAsync(conta);

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
