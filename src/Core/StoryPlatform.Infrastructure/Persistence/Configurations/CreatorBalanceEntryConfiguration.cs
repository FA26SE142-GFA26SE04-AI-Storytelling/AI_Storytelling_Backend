using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreatorBalanceEntryConfiguration : IEntityTypeConfiguration<CreatorBalanceEntry>
{
    public void Configure(EntityTypeBuilder<CreatorBalanceEntry> builder)
    {
        builder.ToTable("creator_balance_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.BankTransactionCode).HasMaxLength(100);
        builder.Property(x => x.BankName).HasMaxLength(100);
        builder.Property(x => x.AccountNumber).HasMaxLength(30);
        builder.Property(x => x.AccountHolderName).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasOne(x => x.Creator)
            .WithMany()
            .HasForeignKey(x => x.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.EarningPeriod)
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.EarningPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReversedEntry)
            .WithMany()
            .HasForeignKey(x => x.ReversedEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CreatorUserId)
            .HasDatabaseName("IX_creator_balance_entries_CreatorUserId");

        // Mỗi Creator nhận thu nhập của một kỳ đúng một lần.
        builder.HasIndex(x => new { x.CreatorUserId, x.EarningPeriodId })
            .IsUnique()
            .HasFilter("\"EarningPeriodId\" IS NOT NULL AND NOT \"IsDeleted\"")
            .HasDatabaseName("UX_creator_balance_entries_period_per_creator");

        // Mỗi lần chi chỉ huỷ được một lần.
        builder.HasIndex(x => x.ReversedEntryId)
            .IsUnique()
            .HasFilter("\"ReversedEntryId\" IS NOT NULL AND NOT \"IsDeleted\"")
            .HasDatabaseName("UX_creator_balance_entries_reversal");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
