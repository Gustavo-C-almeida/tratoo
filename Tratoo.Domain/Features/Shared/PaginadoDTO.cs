namespace Tratoo.Domain.Features.Shared
{
    public class PaginadoDTO<T>
    {
        public List<T> Itens { get; set; } = new();
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int TotalPaginas { get; set; }
    }
}
