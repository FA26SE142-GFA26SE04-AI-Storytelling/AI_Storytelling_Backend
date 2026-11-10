using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreditLotConfiguration : IEntityTypeConfiguration<CreditLot>
{
    public void Configure(EntityTypeBuilder<CreditLot> builder)
    {
        builder.ToTable("credit_lots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreditType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Payment)
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lo gan giao dich chi cap mot lan.
        builder.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasFilter("\"PaymentId\" IS NOT NULL");

        // Moi tai khoan chi co mot lo dung thu.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("IX_credit_lots_UserId_Trial")
            .HasFilter("\"Source\" = 'Trial' AND NOT \"IsDeleted\"");

        builder.HasIndex(x => new { x.UserId, x.CreditType });

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
