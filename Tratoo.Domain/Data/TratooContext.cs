using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.Domain.Models.Prestador;


namespace Tratoo.Domain.Data;
public class TratooContext : DbContext
{
    public TratooContext(DbContextOptions<TratooContext> options)
    : base(options)
    {
    }

    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Prestador> Prestadores { get; set; }
    public DbSet<Contratante> Contratantes { get; set; }

    // Identidade e LGPD
    public DbSet<UserIdentity> UserIdentities { get; set; }
    public DbSet<ConsentLog> ConsentLogs { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Projeto> Projetos { get; set; }
    public DbSet<PropostaProjeto> PropostasProjeto { get; set; }
    public DbSet<PropostaVersao> PropostaVersoes { get; set; }
    public DbSet<MensagemProjeto> MensagensProjeto { get; set; }
    public DbSet<ContratoServico> ContratosServico { get; set; }
    public DbSet<ContratoSnapshot> ContratoSnapshots { get; set; }
    public DbSet<HistoricoAssinatura> HistoricosAssinatura { get; set; }
    public DbSet<Entrega> Entregas { get; set; }
    public DbSet<EntregaAnexo> EntregaAnexos { get; set; }
    public DbSet<EntregaLink> EntregaLinks { get; set; }
    public DbSet<HistoricoContrato> HistoricosContrato { get; set; }
    public DbSet<Pagamento> Pagamentos { get; set; }
    public DbSet<ContaBancaria> ContasBancarias { get; set; }
    public DbSet<LedgerFinanceiro> LedgerFinanceiro { get; set; }
    public DbSet<DisputaPagamento> DisputasPagamento { get; set; }
    public DbSet<WebhookLog> WebhookLogs { get; set; }

    public DbSet<Avaliacao> Avaliacoes { get; set; }
    public DbSet<ReputacaoResumo> ReputacaoResumos { get; set; }

    public DbSet<Competencia> Competencias { get; set; }
    public DbSet<CertificacaoPrestador> CertificacoesPrestador { get; set; }
    public DbSet<ExperienciaPrestador> ExperienciasPrestador { get; set; }
    public DbSet<DisponibilidadeHorario> DisponibilidadesHorario { get; set; }
    public DbSet<PortfolioPrestador> PortfoliosPrestador { get; set; }
    public DbSet<CompetenciaPortfolio> CompetenciaPortfolios { get; set; }

    // Convites de projeto (contratante → prestador)
    public DbSet<ConviteProjeto> ConvitesProjeto { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =======================
        // USUÁRIO (TPH)
        // =======================
        // ── TPT: cada tipo concreto tem sua própria tabela ─────────────────────
        modelBuilder.Entity<Prestador>().ToTable("Prestadores");
        modelBuilder.Entity<Contratante>().ToTable("Contratantes");

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Usuario>()
            .Property(u => u.DataCadastro)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<Usuario>()
            .Property(u => u.Status)
            .HasConversion<string>();

        // Endereco compartilhado — colunas ficam em Usuarios
        modelBuilder.Entity<Usuario>()
            .OwnsOne(u => u.Endereco);

        // =======================
        // USER IDENTITY (LGPD)
        // =======================
        modelBuilder.Entity<UserIdentity>()
            .HasOne(i => i.Usuario)
            .WithOne()
            .HasForeignKey<UserIdentity>(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserIdentity>()
            .HasIndex(i => i.UserId)
            .IsUnique();

        modelBuilder.Entity<UserIdentity>()
            .Property(i => i.NivelVerificacao)
            .HasConversion<int>();

        // =======================
        // CONSENT LOG (LGPD)
        // =======================
        modelBuilder.Entity<ConsentLog>()
            .Property(c => c.Tipo)
            .HasConversion<string>();

        // ConsentLog nunca é deletado em cascata — exigência da LGPD
        modelBuilder.Entity<ConsentLog>()
            .HasIndex(c => c.UserId);

        // =======================
        // AUDIT LOG (Marco Civil Art. 15)
        // =======================
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.UserId, a.DataHora });

        modelBuilder.Entity<Prestador>()
            .Property(p => p.ValorMinimoProjeto)
            .HasPrecision(18, 2);

        // =======================
        // PAGAMENTO
        // =======================
        modelBuilder.Entity<Pagamento>()
            .Property(p => p.ValorBruto)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Pagamento>()
            .Property(p => p.TaxaGateway)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Pagamento>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Pagamento>()
            .Property(p => p.Metodo)
            .HasConversion<string>();

        // ContratoServicoId — FK principal para o novo fluxo
        modelBuilder.Entity<Pagamento>()
            .HasOne(p => p.ContratoServico)
            .WithMany()
            .HasForeignKey(p => p.ContratoServicoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Pagamento>()
            .HasIndex(p => p.ContratoServicoId);

        modelBuilder.Entity<Pagamento>()
            .HasIndex(p => p.GatewayPagamentoId);

        modelBuilder.Entity<Pagamento>()
            .HasIndex(p => p.IdempotencyKey)
            .IsUnique();

        // =======================
        // LEDGER FINANCEIRO
        // =======================
        modelBuilder.Entity<LedgerFinanceiro>()
            .Property(l => l.Valor)
            .HasPrecision(18, 2);

        modelBuilder.Entity<LedgerFinanceiro>()
            .Property(l => l.Tipo)
            .HasConversion<string>();

        modelBuilder.Entity<LedgerFinanceiro>()
            .HasOne(l => l.Pagamento)
            .WithMany(p => p.Ledger)
            .HasForeignKey(l => l.PagamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LedgerFinanceiro>()
            .HasIndex(l => l.PagamentoId);

        // Ledger é imutável — nunca deve ser atualizado
        modelBuilder.Entity<LedgerFinanceiro>()
            .Property(l => l.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        // =======================
        // DISPUTA PAGAMENTO
        // =======================
        modelBuilder.Entity<DisputaPagamento>()
            .Property(d => d.Status)
            .HasConversion<string>();

        modelBuilder.Entity<DisputaPagamento>()
            .HasOne(d => d.Pagamento)
            .WithMany(p => p.Disputas)
            .HasForeignKey(d => d.PagamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisputaPagamento>()
            .HasIndex(d => d.PagamentoId);

        // =======================
        // WEBHOOK LOG
        // =======================
        modelBuilder.Entity<WebhookLog>()
            .HasIndex(w => w.ChaveIdempotencia)
            .IsUnique();

        modelBuilder.Entity<WebhookLog>()
            .HasIndex(w => w.AsaasCobrancaId);

        modelBuilder.Entity<WebhookLog>()
            .Property(w => w.RecebidoEm)
            .HasDefaultValueSql("timezone('utc', now())");


        // =======================
        // DISPONIBILIDADE
        // =======================
        modelBuilder.Entity<DisponibilidadeHorario>()
            .HasOne(d => d.Prestador)
            .WithMany(p => p.Disponibilidades)
            .HasForeignKey(d => d.PrestadorId)
            .OnDelete(DeleteBehavior.Cascade);
        // =======================
        // AVALIAÇÃO BILATERAL (blind review)
        // =======================
        modelBuilder.Entity<Avaliacao>()
            .Property(a => a.Status)
            .HasConversion<int>();

        modelBuilder.Entity<Avaliacao>()
            .Property(a => a.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<Avaliacao>()
            .Property(a => a.Comentario)
            .HasMaxLength(1000);

        modelBuilder.Entity<Avaliacao>()
            .HasOne(a => a.ContratoServico)
            .WithMany()
            .HasForeignKey(a => a.ContratoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Avaliacao>()
            .HasOne(a => a.Avaliador)
            .WithMany()
            .HasForeignKey(a => a.AvaliadorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Avaliacao>()
            .HasOne(a => a.Avaliado)
            .WithMany()
            .HasForeignKey(a => a.AvaliadoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índice composto — garante no máximo 1 slot por (contrato, avaliador)
        modelBuilder.Entity<Avaliacao>()
            .HasIndex(a => new { a.ContratoServicoId, a.AvaliadorId })
            .IsUnique();

        modelBuilder.Entity<Avaliacao>()
            .HasIndex(a => a.AvaliadoId);

        modelBuilder.Entity<Avaliacao>()
            .HasIndex(a => a.Status);

        // =======================
        // REPUTAÇÃO RESUMO
        // =======================
        modelBuilder.Entity<ReputacaoResumo>()
            .HasKey(r => r.UsuarioId);

        modelBuilder.Entity<ReputacaoResumo>()
            .Property(r => r.MediaGeral)
            .HasPrecision(4, 2);

        modelBuilder.Entity<ReputacaoResumo>()
            .HasOne(r => r.Usuario)
            .WithOne()
            .HasForeignKey<ReputacaoResumo>(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Prestador>()
            .HasOne(p => p.ContaBancaria)
            .WithOne(c => c.Prestador)
            .HasForeignKey<ContaBancaria>(c => c.PrestadorId);


        // =======================
        // PROJETO
        // =======================
        modelBuilder.Entity<Projeto>()
            .Property(p => p.OrcamentoMin)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Projeto>()
            .Property(p => p.OrcamentoMax)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Projeto>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Projeto>()
            .Property(p => p.Categoria)
            .HasConversion<string>();

        modelBuilder.Entity<Projeto>()
            .Property(p => p.Visibilidade)
            .HasConversion<string>();

        modelBuilder.Entity<Projeto>()
            .Property(p => p.Idioma)
            .HasConversion<string>();

        modelBuilder.Entity<Projeto>()
            .Property(p => p.NivelFreelancer)
            .HasConversion<string>();

        modelBuilder.Entity<Projeto>()
            .Property(p => p.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<Projeto>()
            .HasOne(p => p.Contratante)
            .WithMany()
            .HasForeignKey(p => p.ContratanteId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Projeto>()
            .HasOne(p => p.FreelancerSelecionado)
            .WithMany()
            .HasForeignKey(p => p.FreelancerSelecionadoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Projeto>()
            .HasIndex(p => p.Status);

        modelBuilder.Entity<Projeto>()
            .HasIndex(p => p.Categoria);

        // =======================
        // PROPOSTA PROJETO (v2)
        // =======================
        modelBuilder.Entity<PropostaProjeto>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<PropostaProjeto>()
            .Property(p => p.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<PropostaProjeto>()
            .HasOne(p => p.Projeto)
            .WithMany(pr => pr.Propostas)
            .HasForeignKey(p => p.ProjetoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PropostaProjeto>()
            .HasOne(p => p.Prestador)
            .WithMany()
            .HasForeignKey(p => p.PrestadorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PropostaProjeto>()
            .HasIndex(p => new { p.PrestadorId, p.ProjetoId });

        modelBuilder.Entity<PropostaProjeto>()
            .HasIndex(p => p.Status);

        // =======================
        // PROPOSTA VERSAO
        // =======================
        modelBuilder.Entity<PropostaVersao>()
            .Property(v => v.ValorTotal)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PropostaVersao>()
            .Property(v => v.Entrada)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PropostaVersao>()
            .Property(v => v.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<PropostaVersao>()
            .HasOne(v => v.Proposta)
            .WithMany(p => p.Versoes)
            .HasForeignKey(v => v.PropostaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PropostaVersao>()
            .HasIndex(v => new { v.PropostaId, v.Versao })
            .IsUnique();

        // =======================
        // MENSAGEM PROJETO (chat)
        // =======================
        modelBuilder.Entity<MensagemProjeto>()
            .Property(m => m.Texto)
            .HasMaxLength(2000);

        modelBuilder.Entity<MensagemProjeto>()
            .Property(m => m.Tipo)
            .HasConversion<string>();

        modelBuilder.Entity<MensagemProjeto>()
            .Property(m => m.EnviadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<MensagemProjeto>()
            .HasOne(m => m.Projeto)
            .WithMany()
            .HasForeignKey(m => m.ProjetoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MensagemProjeto>()
            .HasOne(m => m.Remetente)
            .WithMany()
            .HasForeignKey(m => m.RemetenteId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MensagemProjeto>()
            .HasOne(m => m.PropostaVersao)
            .WithMany()
            .HasForeignKey(m => m.PropostaVersaoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        // Índice composto para isolamento por par e ordenação cronológica
        modelBuilder.Entity<MensagemProjeto>()
            .HasIndex(m => new { m.ProjetoId, m.PrestadorId, m.EnviadoEm });

        // Índice para lista de chats do prestador
        modelBuilder.Entity<MensagemProjeto>()
            .HasIndex(m => new { m.PrestadorId, m.EnviadoEm });

        // =======================
        // CONTRATO SERVICO
        // =======================
        modelBuilder.Entity<ContratoServico>()
            .Property(c => c.Status)
            .HasConversion<string>();

        modelBuilder.Entity<ContratoServico>()
            .Property(c => c.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<ContratoServico>()
            .HasOne(c => c.Projeto)
            .WithMany()
            .HasForeignKey(c => c.ProjetoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContratoServico>()
            .HasOne(c => c.Contratante)
            .WithMany()
            .HasForeignKey(c => c.ContratanteId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContratoServico>()
            .HasOne(c => c.Prestador)
            .WithMany()
            .HasForeignKey(c => c.PrestadorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContratoServico>()
            .HasOne(c => c.Snapshot)
            .WithOne(s => s.Contrato)
            .HasForeignKey<ContratoSnapshot>(s => s.ContratoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ContratoServico>()
            .HasIndex(c => c.PropostaId)
            .IsUnique();

        modelBuilder.Entity<ContratoServico>()
            .HasIndex(c => c.Status);

        modelBuilder.Entity<ContratoServico>()
            .Property(c => c.MotivoCancelamento)
            .HasMaxLength(500);

        modelBuilder.Entity<ContratoServico>()
            .HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(c => c.CanceladoPorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContratoServico>()
            .HasIndex(c => c.CanceladoPorId);

        modelBuilder.Entity<ContratoServico>()
            .Property(c => c.TemplateVersao)
            .HasMaxLength(30);

        modelBuilder.Entity<ContratoSnapshot>()
            .Property(s => s.CongeladoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        // =======================
        // HISTORICO ASSINATURA
        // =======================
        modelBuilder.Entity<HistoricoAssinatura>()
            .Property(h => h.Acao)
            .HasConversion<string>();

        modelBuilder.Entity<HistoricoAssinatura>()
            .Property(h => h.DataEvento)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<HistoricoAssinatura>()
            .Property(h => h.Ip)
            .HasMaxLength(45); // IPv6

        modelBuilder.Entity<HistoricoAssinatura>()
            .Property(h => h.UserAgent)
            .HasMaxLength(500);

        modelBuilder.Entity<HistoricoAssinatura>()
            .HasOne(h => h.Contrato)
            .WithMany()
            .HasForeignKey(h => h.ContratoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<HistoricoAssinatura>()
            .HasIndex(h => h.ContratoId);

        modelBuilder.Entity<HistoricoAssinatura>()
            .HasIndex(h => new { h.ContratoId, h.UsuarioId });

        // =======================
        // PORTFOLIO PRESTADOR
        // =======================
        modelBuilder.Entity<PortfolioPrestador>()
            .HasOne(p => p.Prestador)
            .WithMany(pr => pr.Portfolio)
            .HasForeignKey(p => p.PrestadorId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PortfolioPrestador>()
            .Property(p => p.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        // =======================
        // COMPETENCIA ↔ PORTFOLIO
        // =======================
        modelBuilder.Entity<CompetenciaPortfolio>()
            .HasKey(cp => new { cp.CompetenciaId, cp.PortfolioPrestadorId });

        modelBuilder.Entity<CompetenciaPortfolio>()
            .HasOne(cp => cp.Competencia)
            .WithMany(c => c.CompetenciaPortfolios)
            .HasForeignKey(cp => cp.CompetenciaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetenciaPortfolio>()
            .HasOne(cp => cp.PortfolioPrestador)
            .WithMany(p => p.CompetenciaPortfolios)
            .HasForeignKey(cp => cp.PortfolioPrestadorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetenciaExperiencia>()
            .HasKey(ce => new { ce.CompetenciaId, ce.ExperienciaPrestadorId });

        modelBuilder.Entity<CompetenciaExperiencia>()
            .HasOne(ce => ce.Competencia)
            .WithMany(c => c.CompetenciaExperiencias)
            .HasForeignKey(ce => ce.CompetenciaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetenciaExperiencia>()
            .HasOne(ce => ce.ExperienciaPrestador)
            .WithMany(e => e.CompetenciaExperiencias)
            .HasForeignKey(ce => ce.ExperienciaPrestadorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetenciaCertificacao>()
            .HasKey(cc => new { cc.CompetenciaId, cc.CertificacaoPrestadorId });

        modelBuilder.Entity<CompetenciaCertificacao>()
            .HasOne(cc => cc.Competencia)
            .WithMany(c => c.CompetenciaCertificacoes)
            .HasForeignKey(cc => cc.CompetenciaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetenciaCertificacao>()
            .HasOne(cc => cc.CertificacaoPrestador)
            .WithMany(c => c.CompetenciaCertificacoes)
            .HasForeignKey(cc => cc.CertificacaoPrestadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // =======================
        // CONVITE PROJETO
        // =======================
        modelBuilder.Entity<ConviteProjeto>()
            .Property(c => c.Status)
            .HasConversion<string>();

        modelBuilder.Entity<ConviteProjeto>()
            .Property(c => c.OrcamentoSugerido)
            .HasPrecision(18, 2);

        modelBuilder.Entity<ConviteProjeto>()
            .Property(c => c.MensagemInicial)
            .HasMaxLength(2000);

        modelBuilder.Entity<ConviteProjeto>()
            .Property(c => c.MotivoRecusa)
            .HasMaxLength(500);

        modelBuilder.Entity<ConviteProjeto>()
            .Property(c => c.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<ConviteProjeto>()
            .HasOne(c => c.Projeto)
            .WithMany()
            .HasForeignKey(c => c.ProjetoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ConviteProjeto>()
            .HasOne(c => c.Contratante)
            .WithMany()
            .HasForeignKey(c => c.ContratanteId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ConviteProjeto>()
            .HasOne(c => c.Prestador)
            .WithMany()
            .HasForeignKey(c => c.PrestadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Apenas 1 convite ativo por (prestador, projeto)
        modelBuilder.Entity<ConviteProjeto>()
            .HasIndex(c => new { c.PrestadorId, c.ProjetoId });

        modelBuilder.Entity<ConviteProjeto>()
            .HasIndex(c => c.Status);

        // =======================
        // PROPOSTA PROJETO — SenderType + ConviteId (fluxo reverso)
        // =======================
        modelBuilder.Entity<PropostaProjeto>()
            .Property(p => p.SenderType)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<PropostaProjeto>()
            .HasOne(p => p.Convite)
            .WithMany()
            .HasForeignKey(p => p.ConviteId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // =======================
        // ENTREGA FORMAL
        // =======================
        modelBuilder.Entity<Entrega>()
            .Property(e => e.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Entrega>()
            .Property(e => e.DescricaoEntrega)
            .HasMaxLength(4000);

        modelBuilder.Entity<Entrega>()
            .Property(e => e.Observacoes)
            .HasMaxLength(2000);

        modelBuilder.Entity<Entrega>()
            .Property(e => e.MotivoRejeicao)
            .HasMaxLength(2000);

        modelBuilder.Entity<Entrega>()
            .Property(e => e.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<Entrega>()
            .HasOne(e => e.ContratoServico)
            .WithMany()
            .HasForeignKey(e => e.ContratoServicoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Entrega>()
            .HasIndex(e => e.ContratoServicoId);

        // EntregaAnexo (soft delete)
        modelBuilder.Entity<EntregaAnexo>()
            .Property(a => a.NomeArquivo)
            .HasMaxLength(300);

        modelBuilder.Entity<EntregaAnexo>()
            .Property(a => a.ChaveR2)
            .HasMaxLength(500);

        modelBuilder.Entity<EntregaAnexo>()
            .Property(a => a.TipoArquivo)
            .HasMaxLength(20);

        modelBuilder.Entity<EntregaAnexo>()
            .Property(a => a.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<EntregaAnexo>()
            .HasOne(a => a.Entrega)
            .WithMany(e => e.Anexos)
            .HasForeignKey(a => a.EntregaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Filtro global de soft delete — anexos excluídos não aparecem nas consultas
        modelBuilder.Entity<EntregaAnexo>()
            .HasQueryFilter(a => a.ExcluidoEm == null);

        // EntregaLink
        modelBuilder.Entity<EntregaLink>()
            .Property(l => l.Url)
            .HasMaxLength(1000);

        modelBuilder.Entity<EntregaLink>()
            .Property(l => l.Descricao)
            .HasMaxLength(300);

        modelBuilder.Entity<EntregaLink>()
            .Property(l => l.CriadoEm)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<EntregaLink>()
            .HasOne(l => l.Entrega)
            .WithMany(e => e.Links)
            .HasForeignKey(l => l.EntregaId)
            .OnDelete(DeleteBehavior.Cascade);

        // HistoricoContrato
        modelBuilder.Entity<HistoricoContrato>()
            .Property(h => h.Acao)
            .HasConversion<string>();

        modelBuilder.Entity<HistoricoContrato>()
            .Property(h => h.Descricao)
            .HasMaxLength(1000);

        modelBuilder.Entity<HistoricoContrato>()
            .Property(h => h.DataEvento)
            .HasDefaultValueSql("timezone('utc', now())");

        modelBuilder.Entity<HistoricoContrato>()
            .HasIndex(h => h.ContratoServicoId);

    }
}
