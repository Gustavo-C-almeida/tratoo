namespace Tratoo.Domain.Enums
{
    /// <summary>
    /// Ciclo de vida de uma avaliação bilateral (blind review).
    /// Pendente → Publicada (quando ambos os lados avaliarem ou o prazo de 7 dias expirar).
    /// Admin pode mover Publicada → Oculta sem alterar conteúdo.
    /// </summary>
    public enum StatusAvaliacao
    {
        /// <summary>Criada automaticamente após liberação do pagamento; aguardando envio pelo avaliador.</summary>
        Pendente,
        /// <summary>Ambos os lados avaliaram (ou o prazo expirou) — conteúdo imutável.</summary>
        Publicada,
        /// <summary>Ocultada por admin; não conta para média nem aparece publicamente.</summary>
        Oculta
    }
}
