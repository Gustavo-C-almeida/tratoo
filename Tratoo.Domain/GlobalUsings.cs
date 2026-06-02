// Namespaces de feature expostos globalmente ao projeto.
// Preserva a visibilidade que as antigas pastas técnicas (Services/Repositories/DTOs)
// já forneciam de forma agregada, evitando dezenas de `using` por arquivo após a
// reorganização por domínio de negócio (Feature Folders).
global using Tratoo.Domain.Features.Auth;
global using Tratoo.Domain.Features.Avaliacoes;
global using Tratoo.Domain.Features.Contratos;
global using Tratoo.Domain.Features.IA;
global using Tratoo.Domain.Features.Infrastructure;
global using Tratoo.Domain.Features.Mensagens;
global using Tratoo.Domain.Features.Pagamentos;
global using Tratoo.Domain.Features.Perfis;
global using Tratoo.Domain.Features.Projetos;
global using Tratoo.Domain.Features.Propostas;
global using Tratoo.Domain.Features.Shared;
global using Tratoo.Domain.Features.Storage;
