
namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Etapa 3 do cadastro: valida CPF (PF) ou CNPJ (PJ) e persiste
    /// os dados de identidade criptografados em UserIdentity.
    /// Deve ser chamado na primeira tentativa de criar proposta ou gerar contrato.
    /// </summary>
    public interface IIdentidadeService
    {
        Task ValidarEPersistirAsync(ValidarIdentidadeDTO dto);
    }
}
