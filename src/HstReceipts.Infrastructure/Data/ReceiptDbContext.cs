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
    public DbSet<UserPasswordHistory> UserPasswordHistories => Set<UserPasswordHistory>();
    public DbSet<PasswordResetTicketEntity> PasswordResetTickets => Set<PasswordResetTicketEntity>();
    public DbSet<EmailChangeChallengeEntity> EmailChangeChallenges => Set<EmailChangeChallengeEntity>();
    public DbSet<DeletedUsernameEntity> DeletedUsernames => Set<DeletedUsernameEntity>();
    public DbSet<ProcessingBatch> ProcessingBatches => Set<ProcessingBatch>();
    public DbSet<ProcessingBatchResult> ProcessingBatchResults => Set<ProcessingBatchResult>();

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
        user.Property(u => u.Email).HasMaxLength(256);
        // Multiple NULL emails allowed; non-null emails must be unique.
        user.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");
        user.Property(u => u.Role).HasMaxLength(32).IsRequired();
        user.Property(u => u.IsActive).IsRequired();
        user.Ignore(u => u.Status);
        user.Property(u => u.CreatedAtEst).IsRequired();
        user.Property(u => u.ModifiedAtEst).IsRequired();

        var passwordHistory = modelBuilder.Entity<UserPasswordHistory>();
        passwordHistory.ToTable("UserPasswordHistories");
        passwordHistory.HasKey(h => h.Id);
        passwordHistory.Property(h => h.PasswordHash).HasMaxLength(512).IsRequired();
        passwordHistory.Property(h => h.CreatedAtEst).IsRequired();
        passwordHistory.HasIndex(h => h.UserId);
        passwordHistory.HasIndex(h => new { h.UserId, h.CreatedAtEst });
        passwordHistory.HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var resetTicket = modelBuilder.Entity<PasswordResetTicketEntity>();
        resetTicket.ToTable("PasswordResetTickets");
        resetTicket.HasKey(t => t.Id);
        resetTicket.Property(t => t.Token).HasMaxLength(96).IsRequired();
        resetTicket.HasIndex(t => t.Token).IsUnique();
        resetTicket.Property(t => t.Username).HasMaxLength(64).IsRequired();
        resetTicket.Property(t => t.Email).HasMaxLength(256).IsRequired();
        resetTicket.Property(t => t.MaskedEmail).HasMaxLength(256).IsRequired();
        resetTicket.Property(t => t.Code).HasMaxLength(16).IsRequired();
        resetTicket.Property(t => t.SentAtUtc).IsRequired();
        resetTicket.Property(t => t.ExpiresAtUtc).IsRequired();
        resetTicket.Property(t => t.CreatedAtEst).IsRequired();
        resetTicket.Property(t => t.Consumed).IsRequired();
        resetTicket.Property(t => t.IsSetPasswordInvite).IsRequired();
        resetTicket.HasIndex(t => t.UserId);
        resetTicket.HasIndex(t => t.ExpiresAtUtc);

        var emailChange = modelBuilder.Entity<EmailChangeChallengeEntity>();
        emailChange.ToTable("EmailChangeChallenges");
        emailChange.HasKey(c => c.Id);
        emailChange.Property(c => c.Token).HasMaxLength(96).IsRequired();
        emailChange.HasIndex(c => c.Token).IsUnique();
        emailChange.Property(c => c.Username).HasMaxLength(64).IsRequired();
        emailChange.Property(c => c.NewEmail).HasMaxLength(256).IsRequired();
        emailChange.Property(c => c.MaskedEmail).HasMaxLength(256).IsRequired();
        emailChange.Property(c => c.Code).HasMaxLength(16).IsRequired();
        emailChange.Property(c => c.SentAtUtc).IsRequired();
        emailChange.Property(c => c.ExpiresAtUtc).IsRequired();
        emailChange.Property(c => c.CreatedAtEst).IsRequired();
        emailChange.Property(c => c.Consumed).IsRequired();
        emailChange.HasIndex(c => c.UserId);
        emailChange.HasIndex(c => c.ExpiresAtUtc);

        var deletedUsername = modelBuilder.Entity<DeletedUsernameEntity>();
        deletedUsername.ToTable("DeletedUsernames");
        deletedUsername.HasKey(d => d.Username);
        deletedUsername.Property(d => d.Username).HasMaxLength(64).IsRequired();
        deletedUsername.Property(d => d.DeletedAtEst).IsRequired();

        var batch = modelBuilder.Entity<ProcessingBatch>();
        batch.ToTable("ProcessingBatches");
        batch.HasKey(b => b.Id);
        batch.Property(b => b.Username).HasMaxLength(64).IsRequired();
        batch.Property(b => b.Status).HasMaxLength(32).IsRequired();
        batch.Property(b => b.ErrorMessage).HasMaxLength(2000);
        batch.Property(b => b.CreatedAtEst).IsRequired();
        batch.HasIndex(b => b.CreatedAtEst);
        batch.HasIndex(b => b.Username);
        batch.HasMany(b => b.Results)
            .WithOne(r => r.Batch!)
            .HasForeignKey(r => r.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        var batchResult = modelBuilder.Entity<ProcessingBatchResult>();
        batchResult.ToTable("ProcessingBatchResults");
        batchResult.HasKey(r => r.Id);
        batchResult.Property(r => r.SourceFileName).HasMaxLength(512).IsRequired();
        batchResult.Property(r => r.ReceiptName).HasMaxLength(512).IsRequired();
        batchResult.Property(r => r.StoreName).HasMaxLength(256);
        batchResult.Property(r => r.InvoiceNumber).HasMaxLength(128);
        batchResult.Property(r => r.Currency).HasMaxLength(8);
        batchResult.Property(r => r.TransactionTime).HasMaxLength(64);
        batchResult.Property(r => r.Subtotal).HasPrecision(18, 2);
        batchResult.Property(r => r.GstHst).HasPrecision(18, 2);
        batchResult.Property(r => r.TotalAmount).HasPrecision(18, 2);
        batchResult.Property(r => r.SourceTextPreview).HasMaxLength(4000);
        batchResult.Property(r => r.ErrorMessage).HasMaxLength(2000);
        batchResult.Property(r => r.WarningsJson).HasMaxLength(4000).IsRequired();
        batchResult.Property(r => r.CreatedAtEst).IsRequired();
        batchResult.HasIndex(r => r.BatchId);
    }
}

