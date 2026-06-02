using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public interface IContratosPdfService
    {
        /// <summary>
        /// Gera o PDF do contrato a partir do snapshot imutável e dos metadados de assinatura.
        /// Retorna um MemoryStream com o conteúdo do PDF pronto para upload.
        /// </summary>
        Task<MemoryStream> GerarAsync(ContratoServico contrato, ContratoSnapshot snapshot);
    }
}
