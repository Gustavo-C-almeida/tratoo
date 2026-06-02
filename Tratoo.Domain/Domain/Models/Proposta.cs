using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Models
{

    public class Proposta
    {
        public Guid Id { get; set; }



        // Relacionamentos
        public int PrestadorId { get; set; }
        public Tratoo.Domain.Models.Prestador.Prestador Prestador { get; set; }

        public int ContratanteId { get; set; }
        public Contratante Contratante { get; set; }

        // Conteúdo da proposta
        public string Titulo { get; set; }
        public string Descricao { get; set; }
        public string Entregaveis { get; set; }

        public string? CondicoesGerais { get; set; }

        // Direitos de uso
        public string DireitosUsoImagem { get; set; }

        public string DireitosUsoEntregavel { get; set; }

        // Prazos
        public DateTime PrazoEntrega { get; set; }
        public DateTime? PrazoResposta { get; set; }

        // Status
        public StatusProposta Status { get; set; } = StatusProposta.Pendente;

        // Controle
        public int Versao { get; set; } = 1;

        // Datas
        public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
        public DateTime? RespondidaEm { get; set; }

        public string? MotivoRecusa { get; set; }

        // Contrato gerado após aceite
        public Guid? ContratoId { get; set; }

        // Soft delete
        public bool Ativa { get; set; } = true;

        public string? MotivoCancelamento { get; set; } = null;

        public DateTime? DataCancelamento { get; set; } = null;

        // Histórico de negociações
        public ICollection<PropostaNegociacao> Negociacoes { get; set; } = new List<PropostaNegociacao>();

        public TipoCobranca TipoCobranca { get; set; }

        // Para FIXO
        public decimal? ValorTotal { get; set; }

        // Para HORA
        public decimal? ValorHora { get; set; }
        public int? HorasEstimadas { get; set; }

        // Para MENSAL
        public decimal? ValorMensal { get; set; }
        public int? DuracaoMeses { get; set; }

        public DateTime? AtualizadaEm { get; private set; }

        public decimal Valor { get; private set; }

        public void Recusar(string motivo)
        {
            if (Status != StatusProposta.Pendente && Status != StatusProposta.EmNegociacao)
                throw new InvalidOperationException("Proposta não pode ser recusada.");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new InvalidOperationException("Motivo da recusa é obrigatório.");

            Status = StatusProposta.Recusada;
            MotivoRecusa = motivo.Trim();
            RespondidaEm = DateTime.UtcNow;
        }

        public void Cancelar(string motivo)
        {
            if (Status == StatusProposta.Cancelada)
                throw new InvalidOperationException("Proposta já está cancelada.");

            if (Status == StatusProposta.Aceita)
                throw new InvalidOperationException("Não é possível cancelar uma proposta já aceita.");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new InvalidOperationException("Motivo do cancelamento é obrigatório.");

            Status = StatusProposta.Cancelada;
            MotivoCancelamento = motivo.Trim();
            DataCancelamento = DateTime.UtcNow;
            Ativa = false;
        }

        public void EntrarEmNegociacao()
        {
            if (Status != StatusProposta.Pendente && Status != StatusProposta.EmNegociacao)
                throw new InvalidOperationException("Não é possível negociar esta proposta.");

            Status = StatusProposta.EmNegociacao;
            Versao++;
        }

        public void Aceitar()
        {
            if (Status != StatusProposta.Pendente && Status != StatusProposta.EmNegociacao)
                throw new InvalidOperationException("Proposta não pode ser aceita.");

            Status = StatusProposta.Aceita;
            RespondidaEm = DateTime.UtcNow;
        }

        public void Atualizar()
        {
            AtualizadaEm = DateTime.UtcNow;
        }

        public void CalcularValor()
        {
            switch (TipoCobranca)
            {
                case TipoCobranca.Fixo:
                    if (ValorTotal == null || ValorTotal <= 0)
                        throw new InvalidOperationException("Valor total inválido.");

                    Valor = ValorTotal.Value;
                    break;

                case TipoCobranca.Hora:
                    if (ValorHora == null || ValorHora <= 0)
                        throw new InvalidOperationException("Valor por hora inválido.");

                    if (HorasEstimadas == null || HorasEstimadas <= 0)
                        throw new InvalidOperationException("Horas estimadas inválidas.");

                    Valor = ValorHora.Value * HorasEstimadas.Value;
                    break;

                case TipoCobranca.Mensal:
                    if (ValorMensal == null || ValorMensal <= 0)
                        throw new InvalidOperationException("Valor mensal inválido.");

                    if (DuracaoMeses == null || DuracaoMeses <= 0)
                        throw new InvalidOperationException("Duração inválida.");

                    Valor = ValorMensal.Value * DuracaoMeses.Value;
                    break;

                default:
                    throw new InvalidOperationException("Tipo de cobrança inválido.");
            }
        }
        public void Negociar(
    TipoCobranca? tipoCobranca,
    decimal? valorTotal,
    decimal? valorHora,
    int? horasEstimadas,
    decimal? valorMensal,
    int? duracaoMeses,
    DateTime? novoPrazoEntrega,
    string? condicoesGerais)
        {
            if (Status != StatusProposta.Pendente && Status != StatusProposta.EmNegociacao)
                throw new InvalidOperationException("Não é possível negociar esta proposta.");

            Status = StatusProposta.EmNegociacao;
            Versao++;

            if (novoPrazoEntrega.HasValue)
                PrazoEntrega = novoPrazoEntrega.Value;

            if (!string.IsNullOrWhiteSpace(condicoesGerais))
                CondicoesGerais = condicoesGerais.Trim();

            if (tipoCobranca.HasValue)
                TipoCobranca = tipoCobranca.Value;

            // Atualiza valores conforme tipo
            switch (TipoCobranca)
            {
                case TipoCobranca.Fixo:
                    ValorTotal = valorTotal;
                    break;

                case TipoCobranca.Hora:
                    ValorHora = valorHora;
                    HorasEstimadas = horasEstimadas;
                    break;

                case TipoCobranca.Mensal:
                    ValorMensal = valorMensal;
                    DuracaoMeses = duracaoMeses;
                    break;
            }

            CalcularValor();
        }

    }
    public enum StatusProposta
    {
        Pendente,
        EmNegociacao,
        Aceita,
        Recusada,
        Expirada,
        Cancelada
    }

    public enum TipoCobranca
    {
        Fixo,
        Hora,
        Mensal
    }



}
