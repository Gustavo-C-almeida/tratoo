using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models.Prestador
{
    public class DisponibilidadeHorario
    {
        public int Id { get; set; }

        // Relacionamento
        public int PrestadorId { get; set; }
        public Prestador Prestador { get; set; }

        // Dia da semana
        public DayOfWeek DiaSemana { get; set; }

        // Horários
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraFim { get; set; }

        // Controle
        public bool Ativo { get; set; } = true;
    }
}
