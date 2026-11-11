using EventHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Web.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> o)
    {
        o.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_amount", "amount >= 0");
            t.HasCheckConstraint("ck_orders_fee", "platform_fee >= 0 AND platform_fee <= amount");
            t.HasCheckConstraint("ck_orders_split", "organizer_amount = amount - platform_fee");
        });

        o.HasKey(x => x.Id);
        o.Property(x => x.Id).HasColumnName("id");

        o.Property(x => x.OrderNo).HasColumnName("order_no").HasMaxLength(20).IsRequired();

        o.Property(x => x.EventId).HasColumnName("event_id");
        o.HasOne(x => x.Event)
            .WithMany()
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        o.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        o.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        o.Property(x => x.OrganizerId).HasColumnName("organizer_id").IsRequired();
        o.HasOne(x => x.Organizer)
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Restrict);

        o.Property(x => x.AttendeeName).HasColumnName("attendee_name").HasMaxLength(50).IsRequired();
        o.Property(x => x.AttendeePhone).HasColumnName("attendee_phone").HasMaxLength(20).IsRequired();
        o.Property(x => x.AttendeeEmail).HasColumnName("attendee_email").HasMaxLength(256).IsRequired();
        o.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);

        o.Property(x => x.Amount).HasColumnName("amount");
        o.Property(x => x.PlatformFee).HasColumnName("platform_fee");
        o.Property(x => x.OrganizerAmount).HasColumnName("organizer_amount");

        o.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        o.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        o.Property(x => x.PaidAt).HasColumnName("paid_at");
        o.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        o.Property(x => x.RefundedAt).HasColumnName("refunded_at");
        o.Property(x => x.RefundAmount).HasColumnName("refund_amount");

        o.Property(x => x.EcpayTradeNo).HasColumnName("ecpay_trade_no").HasMaxLength(20);
        o.Property(x => x.PaymentType).HasColumnName("payment_type").HasMaxLength(30);

        o.Property(x => x.ConfirmationEmailSentAt).HasColumnName("confirmation_email_sent_at");

        o.Property(x => x.CreatedAt).HasColumnName("created_at");
        o.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // 訂單編號唯一（網址與客服查詢用）
        o.HasIndex(x => x.OrderNo).IsUnique().HasDatabaseName("ux_orders_order_no");
        // 同一筆綠界交易只能對應一張訂單（MySQL 唯一索引允許多個 NULL）
        o.HasIndex(x => x.EcpayTradeNo).IsUnique().HasDatabaseName("ux_orders_ecpay_trade_no");
        // 活動報名名單：某活動的已付款訂單
        o.HasIndex(x => new { x.EventId, x.Status }).HasDatabaseName("ix_orders_event_status");
        // 會員：我的報名
        o.HasIndex(x => x.UserId).HasDatabaseName("ix_orders_user_id");
        // 主辦方後台與結算：某主辦方的訂單
        o.HasIndex(x => new { x.OrganizerId, x.Status }).HasDatabaseName("ix_orders_organizer_status");
        // 排程：找出逾期未付款的訂單
        o.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("ix_orders_status_expires_at");
    }
}
