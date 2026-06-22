using Microsoft.EntityFrameworkCore;
using Buyit.Domain.Entities;

namespace Buyit.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // ---------- DbSets: one table per entity ----------
        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Inventory> Inventories => Set<Inventory>();
        public DbSet<Coupon> Coupons => Set<Coupon>();
        public DbSet<Cart> Carts => Set<Cart>();
        public DbSet<CartItem> CartItems => Set<CartItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Review> Reviews => Set<Review>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ========== ONE-TO-MANY ==========

            // User (1) -> RefreshTokens (N) : delete user -> delete their tokens
            modelBuilder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // User (1) -> ExternalLogins (N) : delete user -> delete their links
            modelBuilder.Entity<UserExternalLogin>()
                .HasOne(el => el.User)
                .WithMany(u => u.ExternalLogins)
                .HasForeignKey(el => el.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // User (1) -> Orders (N) : keep order history if user removed
            modelBuilder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // User (1) -> Reviews (N)
            modelBuilder.Entity<Review>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Category (1) -> Products (N)
            modelBuilder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Product (1) -> Reviews (N)
            modelBuilder.Entity<Review>()
                .HasOne(r => r.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cart (1) -> CartItems (N) : delete cart -> delete its lines
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Cart)
                .WithMany(c => c.CartItems)
                .HasForeignKey(ci => ci.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            // Product (1) -> CartItems (N) : restrict (avoids multiple cascade paths)
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Product)
                .WithMany(p => p.CartItems)
                .HasForeignKey(ci => ci.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Order (1) -> OrderItems (N) : delete order -> delete its lines
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Product (1) -> OrderItems (N) : restrict (preserve order history)
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Coupon (1) -> Carts (N) : optional (Cart.CouponId is nullable)
            modelBuilder.Entity<Cart>()
                .HasOne(c => c.Coupon)
                .WithMany(co => co.Carts)
                .HasForeignKey(c => c.CouponId)
                .OnDelete(DeleteBehavior.Restrict);


            // ========== SELF-REFERENCING ==========

            // Category -> ParentCategory (must be Restrict; SQL Server forbids self-cascade)
            modelBuilder.Entity<Category>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // ========== ONE-TO-ONE ==========

            // User (1) <-> (1) Cart : Cart is the dependent (holds UserId)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Cart)
                .WithOne(c => c.User)
                .HasForeignKey<Cart>(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Product (1) <-> (1) Inventory : Inventory is the dependent
            modelBuilder.Entity<Product>()
                .HasOne(p => p.Inventory)
                .WithOne(i => i.Product)
                .HasForeignKey<Inventory>(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Order (1) <-> (1) Payment : Payment is the dependent
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Payment)
                .WithOne(p => p.Order)
                .HasForeignKey<Payment>(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // ========== UNIQUE CONSTRAINTS (alternate keys) ==========
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
            modelBuilder.Entity<Coupon>().HasIndex(c => c.Code).IsUnique();

            // Refresh tokens are looked up by their value on every session refresh.
            // A unique index turns that O(n) table scan into a B-tree seek and
            // guarantees no two sessions ever share a token value.
            modelBuilder.Entity<RefreshToken>().HasIndex(rt => rt.Token).IsUnique();
            // A given external account (Provider + ProviderUserId) may be linked
            // to at most ONE of our users. Enforced at the database level so two
            // users can never claim the same Google account.
            modelBuilder.Entity<UserExternalLogin>()
                .HasIndex(el => new { el.Provider, el.ProviderUserId })
                .IsUnique();
            // A user may review a given product at most ONCE. Enforced at the database
            // level so the application-code check in ReviewService can't be defeated by a
            // concurrent double-submit (TOCTOU race) — the second insert hits this index
            // and fails with Postgres 23505, which the service maps to a 409 Conflict.
            modelBuilder.Entity<Review>()
                .HasIndex(r => new { r.UserId, r.ProductId })
                .IsUnique();

            // ========== DECIMAL PRECISION ==========
            modelBuilder.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.TotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<OrderItem>().Property(oi => oi.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<Coupon>().Property(c => c.DiscountPercentage).HasPrecision(5, 2);

            // ========== CHECK CONSTRAINT ==========
            modelBuilder.Entity<Review>()
                .ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "\"Rating\" BETWEEN 1 AND 5"));

            // ========== GLOBAL QUERY FILTERS (soft delete) ==========
            // Every query against Products automatically excludes soft-deleted rows, so
            // "deleted" products vanish from the catalogue without being physically removed.
            // Use .IgnoreQueryFilters() in admin/reporting queries that need to see them.
            modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
        }
    }
}