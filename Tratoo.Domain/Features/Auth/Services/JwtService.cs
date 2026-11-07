using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Tratoo.Domain.Features.Auth
{
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        public string Gerar(int usuarioId, string email, string nome, string tipo, bool perfilMinimoCompleto, bool isAdmin, string sid, TimeSpan validade)
        {
            // Sempre a chave ATUAL; o kid dela vai no cabeçalho (ver ChavesJwt).
            var credenciais = new SigningCredentials(ChavesJwt.DeConfiguracao(_config).Atual, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Name, nome),
                new Claim(ClaimTypes.Role, tipo),
                // Armazenado no JWT para evitar consulta ao banco em cada requisição.
                // Re-emitido com valor "true" após o onboarding ser concluído.
                new Claim("perfilCompleto", perfilMinimoCompleto ? "true" : "false"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                // Sessão no servidor: sem ela (revogada, expirada) o token deixa de valer na
                // hora, mesmo antes do "exp". Token sem sid = 401 (força novo login).
                new Claim(JwtRegisteredClaimNames.Sid, sid)
            };

            // Role adicional de administrador (definida apenas via seed/banco).
            // Um admin mantém sua role de origem (Prestador/Contratante) e ganha "Admin".
            if (isAdmin)
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.Add(validade),
                signingCredentials: credenciais
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
