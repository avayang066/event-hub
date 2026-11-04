using EventHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Web.Data.Configurations;

public class PaymentNotificationConfiguration : IEntityTypeConfiguration<PaymentNotification>
{
    public void Configure(EntityTypeBuilder<PaymentNotification> p)
    {
        p.ToTable("payment_notifications");

        p.HasKey(x => x.Id);
        p.Property(x => x.Id).HasColumnName("id");

        p.Property(x => x.OrderId).HasColumnName("order_id");
        p.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        p.Property(x => x.MerchantTradeNo).HasColumnName("merchant_trade_no").HasMaxLength(20).IsRequired();
        p.Property(x => x.EcpayTradeNo).HasColumnName("ecpay_trade_no").HasMaxLength(20);
        p.Property(x => x.RtnCode).HasColumnName("rtn_code");
        p.Property(x => x.RtnMsg).HasColumnName("rtn_msg").HasMaxLength(200);
        p.Property(x => x.TradeAmount).HasColumnName("trade_amount");
        p.Property(x => x.PaymentType).HasColumnName("payment_type").HasMaxLength(30);
        p.Property(x => x.PaymentDate).HasColumnName("payment_date").HasMaxLength(30);
        p.Property(x => x.IsSimulated).HasColumnName("is_simulated");
        p.Property(x => x.IsCheckMacValid).HasColumnName("is_check_mac_valid");
        p.Property(x => x.ProcessResult).HasColumnName("process_result").HasMaxLength(30).IsRequired();
        p.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("text").IsRequired();
        p.Property(x => x.ReceivedAt).HasColumnName("received_at");

        p.HasIndex(x => x.OrderId).HasDatabaseName("ix_payment_notifications_order_id");
        p.HasIndex(x => x.MerchantTradeNo).HasDatabaseName("ix_payment_notifications_merchant_trade_no");
    }
}
