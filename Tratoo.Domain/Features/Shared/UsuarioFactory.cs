using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.Shared
{
    public static class UsuarioFactory
    {
        public static Usuario Criar(DadosCadastroPendente dados)
        {
            Usuario usuario = dados.Tipo.ToLower() switch
            {
                "prestador" => new Prestador
                {
                    Nome = dados.Nome,
                    Email = dados.Email,
                    MFA = dados.MFA,
                    TipoUsuario = TipoUsuario.Prestador
                },

                "contratante" => new Contratante
                {
                    Nome = dados.Nome,
                    Email = dados.Email,
                    MFA = dados.MFA,
                    TipoUsuario = TipoUsuario.Contratante
                },

                _ => throw new ArgumentException($"Tipo de usuário inválido: '{dados.Tipo}'")
            };

            usuario.SenhaHash = dados.SenhaHash;
            usuario.Status = StatusUsuario.Active;       // e-mail acabou de ser confirmado
            usuario.DataCadastro = DateTime.UtcNow;

            return usuario;
        }
    }
}
