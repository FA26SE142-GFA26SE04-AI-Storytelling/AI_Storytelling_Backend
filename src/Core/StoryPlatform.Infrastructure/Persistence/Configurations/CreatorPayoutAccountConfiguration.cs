using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreatorPayoutAccountConfiguration : IEntityTypeConfiguration<CreatorPayoutAccount>
{
    public void Configure(EntityTypeBuilder<CreatorPayoutAccount> builder)
    {
        builder.ToTable("creator_payout_accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.AccountHolderName).HasMaxLength(100).IsRequired();

        builder.HasOne(x => x.Creator)
            .WithMany()
            .HasForeignKey(x => x.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mỗi Creator một thông tin nhận tiền.
        builder.HasIndex(x => x.CreatorUserId)
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
