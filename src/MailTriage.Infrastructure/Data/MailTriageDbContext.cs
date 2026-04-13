using Microsoft.EntityFrameworkCore;
using MailTriage.Core.Models;

namespace MailTriage.Infrastructure.Data;

public class MailTriageDbContext : DbContext
{
    public MailTriageDbContext(DbContextOptions<MailTriageDbContext> options) : base(options) { }

    public DbSet<MailAccount> MailAccounts => Set<MailAccount>();
    public DbSet<TriagedEmail> TriagedEmails => Set<TriagedEmail>();
    public DbSet<ForwardingRule> ForwardingRules => Set<ForwardingRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Host, x.Username }).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Host).IsRequired().HasMaxLength(500);
            e.Property(x => x.Username).IsRequired().HasMaxLength(500);
        });

        modelBuilder.Entity<TriagedEmail>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MailAccountId, x.MessageId }).IsUnique();
            e.HasIndex(x => x.TriagedAt);
            e.HasIndex(x => x.Category);
            e.HasOne(x => x.MailAccount).WithMany(a => a.TriagedEmails).HasForeignKey(x => x.MailAccountId);
            e.Property(x => x.MessageId).IsRequired().HasMaxLength(500);
            e.Property(x => x.Subject).HasMaxLength(2000);
            e.Property(x => x.FromAddress).HasMaxLength(500);
        });

        modelBuilder.Entity<ForwardingRule>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsEnabled);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.ForwardToAddress).IsRequired().HasMaxLength(500);
        });
    }
}
