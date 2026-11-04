using EventHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Web.Data.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> e)
    {
        e.ToTable("events", t =>
        {
            t.HasCheckConstraint("ck_events_price", "price >= 0");
            t.HasCheckConstraint("ck_events_capacity", "capacity > 0");
            t.HasCheckConstraint("ck_events_reserved", "reserved_count >= 0 AND reserved_count <= capacity");
            t.HasCheckConstraint("ck_events_time", "end_at >= start_at");
        });

        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");

        e.Property(x => x.OrganizerId).HasColumnName("organizer_id").IsRequired();
        e.HasOne(x => x.Organizer)
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Restrict); // 有活動的主辦方不能直接刪帳號

        e.Property(x => x.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        e.Property(x => x.Description).HasColumnName("description").HasColumnType("text");

        e.Property(x => x.StartAt).HasColumnName("start_at");
        e.Property(x => x.EndAt).HasColumnName("end_at");
        e.Property(x => x.RegistrationDeadline).HasColumnName("registration_deadline");

        e.Property(x => x.Location).HasColumnName("location").HasMaxLength(200).IsRequired();

        e.Property(x => x.Price).HasColumnName("price");
        e.Property(x => x.Capacity).HasColumnName("capacity");
        e.Property(x => x.ReservedCount).HasColumnName("reserved_count").HasDefaultValue(0);

        e.Property(x => x.CoverImagePath).HasColumnName("cover_image_path").HasMaxLength(300);

        e.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // 活動列表：已上架 + 依開始時間排序
        e.HasIndex(x => new { x.Status, x.StartAt }).HasDatabaseName("ix_events_status_start_at");
        // 主辦方後台：我的活動
        e.HasIndex(x => x.OrganizerId).HasDatabaseName("ix_events_organizer_id");
    }
}
