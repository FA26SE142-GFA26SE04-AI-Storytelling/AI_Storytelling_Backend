using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class BusinessReportConfiguration : IEntityTypeConfiguration<BusinessReport>
{
    public void Configure(EntityTypeBuilder<BusinessReport> builder)
    {
        builder.ToTable("business_reports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
