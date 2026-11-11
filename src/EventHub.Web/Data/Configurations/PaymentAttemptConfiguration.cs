using EventHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Web.Data.Configurations;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> p)
    {
        p.ToTable("payment_attempts", t =>
        {
            t.HasCheckConstraint("ck_payment_attempts_amount", "amount > 0");
        });

        p.HasKey(x => x.Id);
        p.Property(x => x.Id).HasColumnName("id");

        p.Property(x => x.OrderId).HasColumnName("order_id");
        p.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        p.Property(x => x.MerchantTradeNo).HasColumnName("merchant_trade_no").HasMaxLength(20).IsRequired();
        p.Property(x => x.Amount).HasColumnName("amount");
        p.Property(x => x.CreatedAt).HasColumnName("created_at");

        // 綠界付款通知用 merchant_trade_no 找訂單；綠界規定不可重複
        p.HasIndex(x => x.MerchantTradeNo).IsUnique().HasDatabaseName("ux_payment_attempts_merchant_trade_no");
        p.HasIndex(x => x.OrderId).HasDatabaseName("ix_payment_attempts_order_id");
    }
}
