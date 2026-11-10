using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreditUsageConfiguration : IEntityTypeConfiguration<CreditUsage>
{
    public void Configure(EntityTypeBuilder<CreditUsage> builder)
    {
        builder.ToTable("credit_usages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Lot)
            .WithMany()
            .HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId);
        // Thong ke theo ky doc theo thoi diem tru.
        builder.HasIndex(x => x.CreatedAt);
    }
}
