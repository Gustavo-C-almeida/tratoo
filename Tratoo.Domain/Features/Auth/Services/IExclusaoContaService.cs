
namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Implementa o direito ao esquecimento da LGPD (Art. 18, VI).
    /// Contratos NÃO são apagados — a lei exige mantê-los.
    /// </summary>
    public interface IExclusaoContaService
    {
        Task ExcluirAsync(ExcluirContaDTO dto);
    }
}
