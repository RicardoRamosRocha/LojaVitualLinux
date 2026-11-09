using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace LojaVirtual.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<ProductMedia> ProductMedias { get; set; }

    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<OrderStockReservation> StockReservations { get; set; }
    public DbSet<OrderAccessCredential> OrderAccessCredentials { get; set; }
    public DbSet<OrderAccessRecoveryToken> OrderAccessRecoveryTokens { get; set; }
    public DbSet<OrderPaymentAttempt> OrderPaymentAttempts { get; set; }
    public DbSet<OrderShipment> OrderShipments { get; set; }
    public DbSet<OrderNotificationOutbox> OrderNotificationOutbox { get; set; }

    public DbSet<Customer> Customers { get; set; }

    public DbSet<Address> Addresses { get; set; }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<DistributedCacheEntry> DistributedCacheEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderItem>()
            .HasOne(x => x.Order)
        .WithMany(x => x.Items)
        .HasForeignKey(x => x.OrderId);

    modelBuilder.Entity<OrderItem>()
        .HasOne(x => x.Product)
        .WithMany(x => x.OrderItems)
        .HasForeignKey(x => x.ProductId);

        modelBuilder.Entity<Order>()
            .HasIndex(order => order.IdempotencyKey)
            .IsUnique();

        modelBuilder.Entity<Order>()
            .Property(order => order.Status)
            .HasMaxLength(32)
            .IsRequired();

        modelBuilder.Entity<Order>()
            .Property(order => order.ShippingCost)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(order => order.ShippingQuoteId)
            .HasMaxLength(80);

        modelBuilder.Entity<Order>()
            .Property(order => order.ShippingServiceId)
            .HasMaxLength(80);

        modelBuilder.Entity<OrderStockReservation>()
            .HasOne(reservation => reservation.Order)
            .WithMany(order => order.StockReservations)
            .HasForeignKey(reservation => reservation.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderStockReservation>()
            .HasOne(reservation => reservation.Product)
            .WithMany()
            .HasForeignKey(reservation => reservation.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OrderStockReservation>()
            .Property(reservation => reservation.Status)
            .HasMaxLength(32)
            .IsRequired();

        modelBuilder.Entity<OrderStockReservation>()
            .HasIndex(reservation => new { reservation.ProductId, reservation.Status, reservation.ExpiresAt });

        modelBuilder.Entity<OrderAccessCredential>()
            .HasOne(credential => credential.Order)
            .WithMany(order => order.AccessCredentials)
            .HasForeignKey(credential => credential.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderAccessCredential>()
            .Property(credential => credential.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        modelBuilder.Entity<OrderAccessCredential>()
            .HasIndex(credential => credential.TokenHash)
            .IsUnique();

        modelBuilder.Entity<OrderAccessCredential>()
                .HasIndex(credential => new { credential.OrderId, credential.RevokedAt, credential.ExpiresAt });

        modelBuilder.Entity<OrderAccessRecoveryToken>()
            .HasOne(token => token.Order)
            .WithMany(order => order.AccessRecoveryTokens)
            .HasForeignKey(token => token.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderAccessRecoveryToken>()
            .Property(token => token.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        modelBuilder.Entity<OrderAccessRecoveryToken>()
            .HasIndex(token => token.TokenHash)
            .IsUnique();

        modelBuilder.Entity<OrderAccessRecoveryToken>()
            .HasIndex(token => new { token.OrderId, token.ExpiresAtUtc, token.UsedAtUtc });

        modelBuilder.Entity<OrderPaymentAttempt>()
            .HasOne(attempt => attempt.Order)
            .WithMany(order => order.PaymentAttempts)
            .HasForeignKey(attempt => attempt.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderPaymentAttempt>()
            .Property(attempt => attempt.Status).HasMaxLength(40).IsRequired();
        modelBuilder.Entity<OrderPaymentAttempt>()
            .Property(attempt => attempt.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<OrderPaymentAttempt>()
            .Property(attempt => attempt.Currency).HasMaxLength(3).IsRequired();
        modelBuilder.Entity<OrderPaymentAttempt>()
            .Property(attempt => attempt.Provider).HasMaxLength(32).IsRequired();
        modelBuilder.Entity<OrderPaymentAttempt>()
            .Property(attempt => attempt.IdempotencyKey).HasMaxLength(220).IsRequired();
        modelBuilder.Entity<OrderPaymentAttempt>()
            .HasIndex(attempt => new { attempt.OrderId, attempt.AttemptNumber }).IsUnique();
        modelBuilder.Entity<OrderPaymentAttempt>()
            .HasIndex(attempt => attempt.PreferenceId).IsUnique();
        modelBuilder.Entity<OrderPaymentAttempt>()
            .HasIndex(attempt => attempt.PaymentId).IsUnique();

        modelBuilder.Entity<OrderShipment>()
            .HasOne(shipment => shipment.Order)
            .WithOne(order => order.Shipment)
            .HasForeignKey<OrderShipment>(shipment => shipment.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderShipment>().HasIndex(shipment => shipment.OrderId).IsUnique();
        modelBuilder.Entity<OrderShipment>().HasIndex(shipment => shipment.ProviderShipmentId);
        modelBuilder.Entity<OrderShipment>().HasIndex(shipment => shipment.IdempotencyKey).IsUnique();
        modelBuilder.Entity<OrderShipment>().Property(shipment => shipment.Status).HasMaxLength(32).IsRequired();
        modelBuilder.Entity<OrderShipment>().Property(shipment => shipment.PurchaseAmount).HasPrecision(18, 2);
        modelBuilder.Entity<OrderShipment>().Property(shipment => shipment.LastError).HasMaxLength(500);

        modelBuilder.Entity<OrderNotificationOutbox>()
            .HasOne(notification => notification.Order)
            .WithMany()
            .HasForeignKey(notification => notification.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderNotificationOutbox>()
            .Property(notification => notification.EventType)
            .HasMaxLength(64)
            .IsRequired();

        modelBuilder.Entity<OrderNotificationOutbox>()
            .Property(notification => notification.LeaseToken)
            .HasMaxLength(64);

        modelBuilder.Entity<OrderNotificationOutbox>()
            .Property(notification => notification.LastError)
            .HasMaxLength(120);

        modelBuilder.Entity<OrderNotificationOutbox>()
            .HasIndex(notification => new { notification.OrderId, notification.EventType })
            .IsUnique();

        modelBuilder.Entity<OrderNotificationOutbox>()
            .HasIndex(notification => new
            {
                notification.ProcessedAtUtc,
                notification.NextAttemptAtUtc,
                notification.LeaseExpiresAtUtc
            });
}


}
