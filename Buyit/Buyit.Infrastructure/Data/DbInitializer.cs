using Buyit.Domain.Entities;
using Buyit.Domain.Enums;

namespace Buyit.Infrastructure.Data
{
    public static class DbInitializer
    {
        public static void Seed(AppDbContext context)
        {
            // (1) Idempotency guard: if there is already at least one user,
            //     assume the database is seeded and do nothing.
            if (context.Users.Any())
                return;

            // (2) One admin user — password is HASHED, never stored as plain text.
            var admin = new User
            {
                FirstName = "Site",
                LastName = "Admin",
                Email = "admin@buyit.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Role = UserRole.Admin
            };
            context.Users.Add(admin);


            // A default store the platform itself owns; legacy/demo products live here.
            var platformStore = new Store
            {
                Owner = admin,
                Name = "Platform Store",
                Slug = "platform-store",
                Status = StoreStatus.Approved,
                CommissionRate = 0.00m   // platform doesn't charge itself
            };
            context.Stores.Add(platformStore);

            // A demo seller with their own approved store, to exercise the multi-store path.
            var seller = new User
            {
                FirstName = "Demo",
                LastName = "Seller",
                Email = "seller@buyit.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller123!"),
                Role = UserRole.Seller
            };
            context.Users.Add(seller);

            var sellerStore = new Store
            {
                Owner = seller,
                Name = "Demo Seller Store",
                Slug = "demo-seller-store",
                Status = StoreStatus.Approved,
                CommissionRate = 0.15m   // platform takes 15% of this seller's sales
            };
            context.Stores.Add(sellerStore);


            // (3) Three categories.
            var electronics = new Category { Name = "Electronics", Description = "Phones, laptops and gadgets" };
            var clothing = new Category { Name = "Clothing", Description = "Men and women apparel" };
            var books = new Category { Name = "Books", Description = "Fiction and non-fiction" };
            context.Categories.AddRange(electronics, clothing, books);

            // (4) Five products, each WITH an inventory row (set via navigation property).
            var products = new List<Product>
              {
                  new Product
                  {
                      Name = "Wireless Mouse",
                      Description = "Ergonomic 2.4GHz wireless mouse",
                      Sku = "ELEC-MOUSE-001",
                      Price = 19.99m,
                      Category = electronics,
                      Store = platformStore,
                      Inventory = new Inventory { QuantityInStock = 120, LowStockThreshold = 10 }
                  },
                  new Product
                  {
                      Name = "Laptop 15-inch",
                      Description = "15-inch laptop, 16GB RAM, 512GB SSD",
                      Sku = "ELEC-LAP-001",
                      Price = 1200.00m,
                      Category = electronics,
                      Store = platformStore,
                      Inventory = new Inventory { QuantityInStock = 25, LowStockThreshold = 5 }
                  },
                  new Product
                  {
                      Name = "Cotton T-Shirt",
                      Description = "100% cotton crew-neck t-shirt",
                      Sku = "CLO-TSHIRT-001",
                      Price = 9.99m,
                      Category = clothing,
                      Store = platformStore,
                      Inventory = new Inventory { QuantityInStock = 200, LowStockThreshold = 20 }
                  },
                  new Product
                  {
                      Name = "Denim Jacket",
                      Description = "Classic blue denim jacket",
                      Sku = "CLO-JACKET-001",
                      Price = 49.99m,
                      Category = clothing,
                      Store = platformStore,
                      Inventory = new Inventory { QuantityInStock = 60, LowStockThreshold = 8 }
                  },
                  new Product
                  {
                      Name = "C# in Depth",
                      Description = "A deep dive into the C# language",
                      Sku = "BOOK-CSHARP-001",
                      Price = 39.99m,
                      Category = books,
                      Store = platformStore,
                      Inventory = new Inventory { QuantityInStock = 4, LowStockThreshold = 5 }
                  }
              };
            context.Products.AddRange(products);

            // A product for the demo seller's store, so the multi-store path has real data.
            context.Products.Add(new Product
            {
                Name = "Handmade Mug",
                Description = "Ceramic mug from the demo seller",
                Sku = "SELLER-MUG-001",
                Price = 14.50m,
                Category = clothing,          // any existing category is fine for a demo
                Store = sellerStore,
                Inventory = new Inventory { QuantityInStock = 30, LowStockThreshold = 5 }
            });

            // (5) Write everything to the database in one transaction.
            context.SaveChanges();
        }
    }
}
