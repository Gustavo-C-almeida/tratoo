using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.Perfis
{
    public interface IContratanteRepository
    {
        Task<Contratante?> GetByIdAsync(int id);
        Task<Contratante?> GetCompletoAsync(int id);
        Task UpdateAsync(Contratante contratante);
        Task SaveAsync();

        /// <summary>
        /// Retorna métricas de projetos do contratante:
        /// total publicados (Publicado=true), total concluídos e valor médio entre (OrcamentoMin+OrcamentoMax)/2.
        /// </summary>
        Task<(int Total, int Concluidos, decimal? ValorMedio)> GetMetricasProjetosAsync(int contratanteId);

        /// <summary>Retorna os N projetos publicados mais recentes do contratante, ordenados por PublicadoEm desc.</summary>
        Task<List<Projeto>> GetUltimosProjetosAsync(int contratanteId, int quantidade = 5);
    }
}
