using HstReceipts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HstReceipts.Infrastructure.Data;

public class ReceiptDbContext : DbContext
{
    public ReceiptDbContext(DbContextOptions<ReceiptDbContext> options)
        : base(options)
    {
    }

    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptCorrection> ReceiptCorrections => Set<ReceiptCorrection>();
    public DbSet<ReceiptAiProfile> ReceiptAiProfiles => Set<ReceiptAiProfile>();
    public DbSet<AiApiUsageLog> AiApiUsageLogs => Set<AiApiUsageLog>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var receipt = modelBuilder.Entity<Receipt>();

        receipt.ToTable("Receipts");
        receipt.HasKey(r => r.Id);

        // Column order matches the upload preview (left → right), then audit fields.
        receipt.Property(r => r.Id).HasColumnOrder(0);
        receipt.Property(r => r.InvoiceNumber)
            .HasMaxLength(128)
            .IsRequired()
            .HasColumnOrder(1);
        receipt.Property(r => r.StoreName)
            .HasMaxLength(256)
            .IsRequired()
            .HasColumnOrder(2);
        receipt.Property(r => r.Currency)
            .HasMaxLength(8)
            .IsRequired()
            .HasColumnOrder(3);
        receipt.Property(r => r.Subtotal)
            .HasPrecision(18, 2)
            .HasColumnOrder(4);
        receipt.Property(r => r.GstHst)
            .HasPrecision(18, 2)
            .HasColumnOrder(5);
        receipt.Property(r => r.TotalAmount)
            .HasPrecision(18, 2)
            .HasColumnOrder(6);
        receipt.Property(r => r.ReceiptDate)
            .IsRequired()
            .HasColumnOrder(7);
        receipt.Property(r => r.TransactionTime)
            .HasMaxLength(64)
            .HasColumnOrder(8);
        receipt.Property(r => r.MatchStatus)
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue(ReceiptMatchStatuses.New)
            .HasColumnOrder(9);
        receipt.Property(r => r.BatchId)
            .HasColumnOrder(10);
        receipt.Property(r => r.CreatedAtEst)
            .IsRequired()
            .HasColumnOrder(11);
        receipt.Property(r => r.ModifiedAtEst)
            .HasColumnOrder(12);

        receipt.HasIndex(r => r.BatchId);
        receipt.HasIndex(r => r.InvoiceNumber);
        receipt.HasIndex(r => new { r.InvoiceNumber, r.StoreName });

        var correction = modelBuilder.Entity<ReceiptCorrection>();
        correction.ToTable("ReceiptCorrections");
        correction.HasKey(c => c.Id);
        correction.Property(c => c.Username).HasMaxLength(64).IsRequired();
        correction.Property(c => c.FieldName).HasMaxLength(64).IsRequired();
        correction.Property(c => c.OldValue).HasMaxLength(512);
        correction.Property(c => c.NewValue).HasMaxLength(512);
        correction.Property(c => c.MatchKind).HasMaxLength(16).IsRequired();
        correction.Property(c => c.CreatedAtEst).IsRequired();
        correction.HasIndex(c => c.ReceiptId);
        correction.HasIndex(c => c.CreatedAtEst);
        correction.HasIndex(c => c.BatchId);
        correction.HasOne(c => c.Receipt)
            .WithMany(r => r.Corrections)
            .HasForeignKey(c => c.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        var ai = modelBuilder.Entity<ReceiptAiProfile>();
        ai.ToTable("ReceiptAiProfiles");
        ai.HasKey(p => p.Id);
        ai.Property(p => p.SimilarityKey).HasMaxLength(256).IsRequired();
        ai.HasIndex(p => p.SimilarityKey).IsUnique();
        ai.Property(p => p.CanonicalStoreName).HasMaxLength(256);
        ai.Property(p => p.Currency).HasMaxLength(8);
        ai.Property(p => p.StoreNameAliasesJson).HasMaxLength(4000);
        ai.Property(p => p.InvoiceNumberHint).HasMaxLength(512);
        ai.Property(p => p.ReceiptDateHint).HasMaxLength(256);
        ai.Property(p => p.SubtotalHint).HasMaxLength(256);
        ai.Property(p => p.GstHstHint).HasMaxLength(256);
        ai.Property(p => p.TotalAmountHint).HasMaxLength(256);
        ai.Property(p => p.Notes).HasMaxLength(1024);
        ai.Property(p => p.RawResponse).HasMaxLength(4000);
        ai.Property(p => p.ModifiedAtEst).IsRequired();

        var usage = modelBuilder.Entity<AiApiUsageLog>();
        usage.ToTable("AiApiUsageLogs");
        usage.HasKey(u => u.Id);
        usage.Property(u => u.CreatedAtEst).IsRequired();
        usage.Property(u => u.Username).HasMaxLength(64).IsRequired();
        usage.Property(u => u.Operation).HasMaxLength(64).IsRequired();
        usage.Property(u => u.Model).HasMaxLength(64).IsRequired();
        usage.Property(u => u.EstimatedCostUsd).HasPrecision(18, 8);
        usage.Property(u => u.Context).HasMaxLength(512);
        usage.HasIndex(u => u.CreatedAtEst);
        usage.HasIndex(u => u.Username);

        var user = modelBuilder.Entity<AppUser>();
        user.ToTable("Users");
        user.HasKey(u => u.Id);
        user.Property(u => u.Username).HasMaxLength(64).IsRequired();
        user.HasIndex(u => u.Username).IsUnique();
        user.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        user.Property(u => u.Role).HasMaxLength(32).IsRequired();
        user.Property(u => u.IsActive).IsRequired();
        user.Ignore(u => u.Status);
        user.Property(u => u.CreatedAtEst).IsRequired();
        user.Property(u => u.ModifiedAtEst).IsRequired();
    }
}
