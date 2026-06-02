using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace Tratoo.Domain.Features.Pagamentos
{
    public class FinanceiroService
    {
        private readonly TratooContext _db;

        public FinanceiroService(TratooContext db)
        {
            _db = db;
        }

        public async Task CadastrarConta(
            int prestadorId,
            ContaBancariaDTO dto)
        {
            var prestador = await _db.Prestadores
                .Include(p => p.ContaBancaria)
                .FirstOrDefaultAsync(p => p.Id == prestadorId);

            if (prestador == null)
                throw new Exception("Prestador não encontrado");

            string contaProtegida = DataProtector.Encrypt(dto.Conta);

            if (prestador.ContaBancaria == null)
            {
                prestador.ContaBancaria = new ContaBancaria
                {
                    Banco = dto.Banco,
                    Agencia = dto.Agencia,
                    ContaCriptografada = contaProtegida,
                    PixChave = dto.Pix,
                    TipoPix = dto.TipoPix
                };
            }
            else
            {
                prestador.ContaBancaria.Banco = dto.Banco;
                prestador.ContaBancaria.Agencia = dto.Agencia;
                prestador.ContaBancaria.ContaCriptografada = contaProtegida;
                prestador.ContaBancaria.PixChave = dto.Pix;
                prestador.ContaBancaria.TipoPix = dto.TipoPix;
                prestador.ContaBancaria.AtualizadoEm = DateTime.Now;
            }

            await _db.SaveChangesAsync();
        }
    }

}
