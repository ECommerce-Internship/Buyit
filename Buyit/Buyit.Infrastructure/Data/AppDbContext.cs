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
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Store> Stores => Set<Store>();
        public DbSet<StoreOrder> StoreOrders => Set<StoreOrder>();
        public DbSet<StoreOrderItem> StoreOrderItems => Set<StoreOrderItem>();

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

            // Coupon (1) -> Carts (N) : optional (Cart.CouponId is nullable)
            modelBuilder.Entity<Cart>()
                .HasOne(c => c.Coupon)
                .WithMany(co => co.Carts)
                .HasForeignKey(c => c.CouponId)
                .OnDelete(DeleteBehavior.Restrict);

            // User (1) -> Stores (N) : keep stores if the owner row is restricted from deletion
            modelBuilder.Entity<Store>()
                .HasOne(s => s.Owner)
                .WithMany(u => u.Stores)
                .HasForeignKey(s => s.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Store (1) -> Products (N) : restrict so a store can't be deleted out from under products
            modelBuilder.Entity<Product>()
                .HasOne(p => p.Store)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            // Store (1) -> StoreOrders (N) : restrict to preserve sales history
            modelBuilder.Entity<StoreOrder>()
                .HasOne(so => so.Store)
                .WithMany(s => s.StoreOrders)
                .HasForeignKey(so => so.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            // Order (1) -> StoreOrders (N) : cascade — a store-slice is owned by its parent order
            modelBuilder.Entity<StoreOrder>()
                .HasOne(so => so.Order)
                .WithMany(o => o.StoreOrders)
                .HasForeignKey(so => so.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // StoreOrder (1) -> StoreOrderItems (N) : cascade — lines are owned by their store-slice
            modelBuilder.Entity<StoreOrderItem>()
                .HasOne(soi => soi.StoreOrder)
                .WithMany(so => so.StoreOrderItems)
                .HasForeignKey(soi => soi.StoreOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Product (1) -> StoreOrderItems (N) : restrict to preserve order history
            modelBuilder.Entity<StoreOrderItem>()
                .HasOne(soi => soi.Product)
                .WithMany()
                .HasForeignKey(soi => soi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Coupon (1) -> Stores ... actually Store (optional) on Coupon: a coupon may target one store
            modelBuilder.Entity<Coupon>()
                .HasOne(c => c.Store)
                .WithMany()
                .HasForeignKey(c => c.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            // Order (1) -> Coupon (optional) : the coupon used on this order, if any
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Coupon)
                .WithMany()
                .HasForeignKey(o => o.CouponId)
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
            // SKU is unique PER STORE, not globally: two stores may reuse the same SKU,
            // but one store cannot list the same SKU twice. (Composite unique index.)
            modelBuilder.Entity<Product>()
                .HasIndex(p => new { p.StoreId, p.Sku })
                .IsUnique();

            // Store slugs appear in public URLs, so they must be globally unique.
            modelBuilder.Entity<Store>()
                .HasIndex(s => s.Slug)
                .IsUnique();

            // Coupons are frequently filtered by store; index (non-unique) speeds that up.
            modelBuilder.Entity<Coupon>()
                .HasIndex(c => c.StoreId);
            
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
            modelBuilder.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<Coupon>().Property(c => c.DiscountPercentage).HasPrecision(5, 2);
            modelBuilder.Entity<StoreOrder>().Property(so => so.SubTotal).HasPrecision(10, 2);
            modelBuilder.Entity<StoreOrder>().Property(so => so.CommissionAmount).HasPrecision(10, 2);
            modelBuilder.Entity<StoreOrder>().Property(so => so.SellerNetAmount).HasPrecision(10, 2);
            modelBuilder.Entity<Store>().Property(s => s.CommissionRate).HasPrecision(5, 4);
            modelBuilder.Entity<StoreOrderItem>().Property(soi => soi.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<StoreOrderItem>().Property(soi => soi.Subtotal).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.DiscountAmount).HasPrecision(18, 2);

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