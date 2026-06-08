namespace Tratoo.Domain.Enums
{
    // Categorias amplas do marketplace. Persistidas como string (HasConversion<string>),
    // portanto novos valores podem ser adicionados sem migration. Os nomes existentes
    // (TI, Redacao, Juridico…) são mantidos para compatibilidade com dados já gravados;
    // o rótulo de exibição fica no frontend (ex.: "TI" → "Desenvolvimento de Software").
    public enum CategoriaProjet
    {
        TI,           // Desenvolvimento de Software
        Design,       // Design & UX/UI
        Marketing,    // Marketing Digital
        Redacao,      // Redação & Conteúdo
        Video,        // Edição de Vídeo
        Dados,        // Dados, BI & Planilhas
        Traducao,     // Tradução
        Suporte,      // Suporte Técnico & Assistência Virtual
        Consultoria,  // Consultoria
        Juridico,     // Jurídico
        Outros
    }
}
