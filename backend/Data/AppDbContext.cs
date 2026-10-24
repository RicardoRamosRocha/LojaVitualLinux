using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Data;

public class AppDbContext : DbContext
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

    public DbSet<Customer> Customers { get; set; }

    public DbSet<Address> Addresses { get; set; }

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
}


}
