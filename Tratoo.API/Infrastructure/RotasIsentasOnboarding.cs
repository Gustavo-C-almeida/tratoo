namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Rotas que a guarda de onboarding não pode bloquear.
    ///
    /// A guarda devolve 403 para usuário autenticado cujo perfil mínimo não está
    /// completo. Como o cookie JWT vai em TODA requisição same-origin, rotas
    /// públicas também precisam constar aqui — senão um cookie remanescente
    /// bloqueia fluxo que nem exige autenticação.
    ///
    /// Extraído de Program.cs sem alteração de comportamento: a lista e a
    /// semântica de comparação são as mesmas. O motivo de virar tipo próprio é
    /// poder testar a isenção de verdade, em vez de reimplementar a condição no
    /// teste (o que verificaria uma cópia, não a regra em produção).
    /// </summary>
    public static class RotasIsentasOnboarding
    {
        public static bool EhIsenta(string? caminho)
        {
            var path = caminho ?? string.Empty;

            return
                // Sondas de liveness/readiness — precisam responder mesmo com
                // cookie de perfil incompleto, senão a plataforma interpretaria o
                // 403 como aplicação doente.
                path.StartsWith(HealthCheckSetup.PrefixoRotas, StringComparison.OrdinalIgnoreCase) ||
                // Endpoint de dados do usuário atual (consultado pelo guard do frontend)
                path.Equals("/api/me", StringComparison.OrdinalIgnoreCase) ||
                // Fluxo de onboarding
                path.StartsWith("/usuarios/onboarding", StringComparison.OrdinalIgnoreCase) ||
                // Logout (deve sempre funcionar)
                path.StartsWith("/usuarios/logout", StringComparison.OrdinalIgnoreCase) ||
                // Login e MFA — o cookie pode estar presente mas o usuário quer trocar de conta
                // ou o token expirou e ele precisa se reautenticar
                path.StartsWith("/usuarios/login", StringComparison.OrdinalIgnoreCase) ||
                // Cadastro e confirmação de e-mail — fluxo público, cookie não deve bloquear
                path.StartsWith("/usuarios/cadastro", StringComparison.OrdinalIgnoreCase) ||
                // Redefinição de senha — fluxo público, usuário pode ter cookie expirado/inválido
                path.StartsWith("/usuarios/senha/resetar", StringComparison.OrdinalIgnoreCase) ||
                // Swagger (dev/teste)
                path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
                // CEP — usado no próprio onboarding para buscar endereço
                path.StartsWith("/api/cep/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
