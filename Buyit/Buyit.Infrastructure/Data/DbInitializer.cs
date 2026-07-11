using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buyit.Infrastructure.Data
{
    /// <summary>
    /// Resets the database to a clean, realistic marketplace catalogue.
    ///
    /// Behaviour:
    ///  - Runs at most once per dataset: guarded by a marker store (<c>peak-performance-sports</c>).
    ///    If that store already exists the method is a no-op, so normal restarts never touch data.
    ///  - On first run against an already-populated database it WIPES every table (identity reset)
    ///    except the EF migrations history, then re-creates the single admin account plus the demo
    ///    catalogue below. This is what makes the reset take effect on the next deployment.
    ///
    /// The only original account preserved is the administrator (admin@buyit.com / Admin123!); it is
    /// re-created with identical credentials so the login is unchanged.
    /// </summary>
    public static class DbInitializer
    {
        // 5 stores, one per category. Each owner carries the store's public contact email + phone
        // (the Store entity itself has no logo/address/contact columns — see the note in the summary).
        public static void Seed(AppDbContext context)
        {
            // (0) Idempotency marker: if the realistic catalogue is already present, do nothing.
            //     Uses IgnoreQueryFilters so a (hypothetical) soft-deleted marker still counts.
            if (context.Stores.Any(s => s.Slug == "peak-performance-sports"))
                return;

            // (1) RESET — remove every existing row (see ResetDatabase). Admin is re-created below.
            ResetDatabase(context);

            // (2) The one administrator account we keep. Password is BCrypt-hashed, never plain text.
            var admin = new User
            {
                FirstName = "Site",
                LastName = "Admin",
                Email = "admin@buyit.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Role = UserRole.Admin
            };
            context.Users.Add(admin);

            // (3) Five top-level categories, one per store.
            var sports = new Category { Name = "Sports", Description = "Sporting goods, fitness and outdoor gear" };
            var electronics = new Category { Name = "Electronics", Description = "Computers, audio, wearables and accessories" };
            var handmade = new Category { Name = "Handmade", Description = "Artisan, hand-crafted home and lifestyle goods" };
            var clothing = new Category { Name = "Clothing", Description = "Men's and women's apparel and footwear" };
            var jewelry = new Category { Name = "Jewelry", Description = "Fine and fashion jewellery" };
            context.Categories.AddRange(sports, electronics, handmade, clothing, jewelry);

            // (4) Store 1 — Sports.
            AddStore(context,
                owner: new User
                {
                    FirstName = "Marcus", LastName = "Feldman",
                    Email = "marcus@peakperformance-sports.com",
                    PhoneNumber = "+1 (415) 555-0182",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller123!"),
                    Role = UserRole.Seller
                },
                store: new Store
                {
                    Name = "Peak Performance Sports",
                    Slug = "peak-performance-sports",
                    Description = "Performance sports and fitness equipment for athletes at every level — from " +
                                  "match-day essentials to home-gym gear that lasts. Based in San Francisco, CA.",
                    Status = StoreStatus.Approved,
                    CommissionRate = 0.12m
                },
                category: sports,
                lockStart: 101,
                items: new[]
                {
                    ("Match-Grade Football", "SPT-0001", 29.99m, 85, "football,soccer,ball",
                        "Official size 5 match football with a hand-stitched TPU casing and butyl bladder for " +
                        "reliable air retention. Balanced flight and a soft touch make it ideal for training and match play."),
                    ("Indoor/Outdoor Basketball", "SPT-0002", 34.99m, 70, "basketball,ball",
                        "Composite-leather basketball with a deep-channel design and pebbled grip that performs on " +
                        "both hardwood courts and outdoor asphalt. Regulation size 7 with consistent bounce."),
                    ("Men's Trail Running Shoes", "SPT-0003", 119.99m, 45, "running,shoes,sneaker",
                        "Lightweight trail runners with a responsive EVA midsole, breathable mesh upper and a " +
                        "lugged rubber outsole for grip on loose terrain. Cushioned heel for long-distance comfort."),
                    ("Adjustable Dumbbell Set", "SPT-0004", 189.99m, 30, "dumbbell,weights,gym",
                        "Space-saving adjustable dumbbell pair that dials from 5 to 52.5 lb each, replacing 15 sets of " +
                        "weights. Knurled handles and a secure locking mechanism for safe strength training at home."),
                    ("Non-Slip Yoga Mat", "SPT-0005", 39.99m, 120, "yoga,mat,fitness",
                        "6 mm high-density TPE yoga mat with a double-sided non-slip texture and body-alignment lines. " +
                        "Cushions joints during floor work and rolls up with the included carry strap."),
                    ("Carbon Tennis Racket", "SPT-0006", 149.99m, 25, "tennis,racket",
                        "Graphite-carbon tennis racket with a 100 sq in head and an even balance for a blend of power " +
                        "and control. Pre-strung and finished with a cushioned, sweat-absorbing overgrip."),
                    ("Insulated Water Bottle 32oz", "SPT-0007", 24.99m, 150, "water,bottle,steel",
                        "Double-wall vacuum-insulated stainless-steel bottle that keeps drinks cold for 24 hours or hot " +
                        "for 12. Leak-proof lid, powder-coated finish and a wide mouth that fits ice cubes."),
                    ("Resistance Bands Set", "SPT-0008", 27.99m, 110, "resistance,band,fitness",
                        "Set of five stackable latex resistance bands from 10 to 50 lb, with cushioned handles, ankle " +
                        "straps and a door anchor. A complete portable gym for strength and mobility work."),
                    ("Aerodynamic Cycling Helmet", "SPT-0009", 79.99m, 40, "cycling,helmet,bike",
                        "In-mould road-cycling helmet with 18 vents for airflow, an adjustable dial-fit system and a " +
                        "lightweight EPS shell. CPSC certified for road and gravel riding."),
                    ("Speed Jump Rope", "SPT-0010", 14.99m, 140, "jump,rope,fitness",
                        "Adjustable steel-cable speed rope with ball-bearing swivels for fast, tangle-free rotation. " +
                        "Ergonomic anti-slip handles built for HIIT, boxing and double-unders."),
                    ("High-Density Foam Roller", "SPT-0011", 32.99m, 90, "foam,roller,muscle",
                        "18-inch high-density foam roller with a textured surface that targets trigger points for " +
                        "myofascial release. Firm enough for deep recovery yet gentle on sore muscles."),
                    ("Compression Leggings", "SPT-0012", 49.99m, 75, "leggings,gym,activewear",
                        "Four-way-stretch compression leggings with moisture-wicking fabric, a high supportive waistband " +
                        "and a hidden pocket. Squat-proof and breathable for training or running."),
                    ("Gym Duffel Bag", "SPT-0013", 44.99m, 60, "gym,duffel,bag",
                        "Water-resistant 40 L gym duffel with a ventilated shoe compartment, a wet-gear pocket and a " +
                        "padded shoulder strap. Durable ripstop fabric handles daily commutes to the gym."),
                    ("Cast-Iron Kettlebell 16kg", "SPT-0014", 54.99m, 35, "kettlebell,gym,weights",
                        "Solid cast-iron 16 kg kettlebell with a smooth, wide handle for two-handed swings and a flat " +
                        "base that stays put during renegade rows. Powder-coated for a secure, chalk-friendly grip."),
                    ("Fitness Tracker Band", "SPT-0015", 59.99m, 50, "fitness,tracker,watch",
                        "Slim fitness band tracking heart rate, steps, sleep and 14 workout modes, with a 10-day battery " +
                        "and 5 ATM water resistance. Syncs to a companion app for trends and goals.")
                });

            // (5) Store 2 — Electronics.
            AddStore(context,
                owner: new User
                {
                    FirstName = "Priya", LastName = "Nair",
                    Email = "priya@voltix-electronics.com",
                    PhoneNumber = "+1 (206) 555-0147",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller123!"),
                    Role = UserRole.Seller
                },
                store: new Store
                {
                    Name = "Voltix Electronics",
                    Slug = "voltix-electronics",
                    Description = "Consumer electronics and computing accessories, hand-picked for build quality and " +
                                  "value. Fast shipping and a 30-day return policy. Shipping from Seattle, WA.",
                    Status = StoreStatus.Approved,
                    CommissionRate = 0.10m
                },
                category: electronics,
                lockStart: 116,
                items: new[]
                {
                    ("15\" Ultrabook Laptop", "ELC-0001", 1099.99m, 20, "laptop,computer",
                        "Thin-and-light 15.6-inch ultrabook with a 12-core processor, 16 GB RAM and a 512 GB NVMe SSD. " +
                        "A crisp 1080p display and all-day battery make it a capable work-and-study machine."),
                    ("Mechanical Keyboard", "ELC-0002", 89.99m, 65, "mechanical,keyboard",
                        "Tenkeyless mechanical keyboard with tactile brown switches, hot-swappable sockets and " +
                        "per-key RGB. A sturdy aluminium top plate and PBT keycaps give a satisfying, durable typing feel."),
                    ("Wireless Ergonomic Mouse", "ELC-0003", 29.99m, 130, "wireless,mouse,computer",
                        "Contoured 2.4 GHz wireless mouse with a silent-click design, adjustable 800–4000 DPI and a " +
                        "battery rated for 18 months. Sculpted for the right hand to reduce wrist strain."),
                    ("Smartwatch Series 6", "ELC-0004", 249.99m, 40, "smartwatch,watch",
                        "GPS smartwatch with an always-on AMOLED display, blood-oxygen and heart-rate sensors and " +
                        "built-in workout tracking. Water resistant to 50 m with up to 7 days of battery life."),
                    ("27\" 4K UHD Monitor", "ELC-0005", 329.99m, 25, "monitor,screen,computer",
                        "27-inch 4K IPS monitor with 99% sRGB colour, HDR10 and a 60 Hz refresh. USB-C with 65 W power " +
                        "delivery drives a laptop and display over a single cable; height-adjustable stand included."),
                    ("Noise-Cancelling Headphones", "ELC-0006", 199.99m, 55, "headphones,audio",
                        "Over-ear wireless headphones with hybrid active noise cancellation, 30-hour battery and " +
                        "plush memory-foam earcups. Multipoint Bluetooth pairs to a phone and laptop at once."),
                    ("Portable Bluetooth Speaker", "ELC-0007", 59.99m, 90, "bluetooth,speaker,audio",
                        "Pocket-sized IPX7 waterproof speaker with 360-degree sound, punchy bass and 16 hours of " +
                        "playtime. Pair two for stereo and clip it to a backpack with the built-in strap."),
                    ("USB-C Hub 7-in-1", "ELC-0008", 39.99m, 100, "usb,hub,adapter",
                        "Aluminium 7-in-1 USB-C hub adding 4K HDMI, 100 W pass-through charging, gigabit Ethernet, an " +
                        "SD/microSD reader and two USB-A ports. Plug-and-play with no drivers required."),
                    ("1080p Streaming Webcam", "ELC-0009", 49.99m, 70, "webcam,camera",
                        "Full-HD 1080p webcam with autofocus, dual noise-reducing microphones and automatic light " +
                        "correction. A universal clip fits monitors and laptops for sharp video calls and streams."),
                    ("Portable SSD 1TB", "ELC-0010", 109.99m, 45, "ssd,storage,drive",
                        "Shock-resistant 1 TB portable SSD with USB 3.2 Gen 2 speeds up to 1050 MB/s and hardware " +
                        "AES-256 encryption. Pocket-sized aluminium body for fast backups and file transfers."),
                    ("Smartphone Gimbal Stabilizer", "ELC-0011", 79.99m, 35, "gimbal,camera,phone",
                        "Foldable 3-axis smartphone gimbal with active tracking, gesture control and a built-in " +
                        "extension rod. Smooths out handheld video and lasts about 14 hours per charge."),
                    ("Wireless Charging Pad", "ELC-0012", 24.99m, 120, "wireless,charger",
                        "Qi-certified 15 W wireless charging pad with foreign-object detection and a non-slip surface. " +
                        "Charges through most cases; a status LED confirms correct alignment."),
                    ("Extended Gaming Mouse Pad", "ELC-0013", 19.99m, 110, "mouse,pad,desk",
                        "Extended 900×400 mm desk mat with a smooth micro-textured cloth surface, stitched anti-fray " +
                        "edges and a natural-rubber non-slip base. Rolls flat for consistent mouse tracking."),
                    ("10000mAh Power Bank", "ELC-0014", 34.99m, 95, "power,bank,battery",
                        "Slim 10,000 mAh power bank with 20 W USB-C PD fast charging and dual outputs to top up two " +
                        "devices at once. Recharges fully in under two hours and fits easily in a pocket."),
                    ("Smart LED Light Strip", "ELC-0015", 27.99m, 80, "led,light,strip",
                        "16.4 ft app- and voice-controlled RGB LED strip with 16 million colours, music sync and scene " +
                        "presets. Adhesive backing installs behind desks, shelves and TVs in minutes.")
                });

            // (6) Store 3 — Handmade.
            AddStore(context,
                owner: new User
                {
                    FirstName = "Elena", LastName = "Costa",
                    Email = "elena@artisanandoak.com",
                    PhoneNumber = "+1 (503) 555-0198",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller123!"),
                    Role = UserRole.Seller
                },
                store: new Store
                {
                    Name = "Artisan & Oak",
                    Slug = "artisan-and-oak",
                    Description = "Small-batch, hand-crafted home and lifestyle goods made by independent makers. Every " +
                                  "piece is one of a kind and finished by hand in our Portland, OR studio.",
                    Status = StoreStatus.Approved,
                    CommissionRate = 0.15m
                },
                category: handmade,
                lockStart: 131,
                items: new[]
                {
                    ("Hand-Thrown Ceramic Mug", "HMD-0001", 28.00m, 60, "ceramic,mug,pottery",
                        "Wheel-thrown stoneware mug glazed in a speckled matte finish, holding 12 oz. Each mug is thrown " +
                        "and trimmed by hand, so no two are exactly alike. Microwave and dishwasher safe."),
                    ("Reclaimed Wood Table Lamp", "HMD-0002", 89.00m, 20, "wood,lamp,light",
                        "Table lamp turned from a single block of reclaimed oak, finished with natural beeswax oil and " +
                        "topped with a linen shade. Warm ambient light for a bedside or reading nook."),
                    ("Chunky Knit Crochet Blanket", "HMD-0003", 129.00m, 15, "crochet,blanket,knit",
                        "Oversized hand-crocheted throw in chunky merino-blend yarn, measuring 50×60 inches. Its plush " +
                        "chevron texture adds warmth and a handmade touch to any sofa or bed."),
                    ("Full-Grain Leather Wallet", "HMD-0004", 65.00m, 40, "leather,wallet",
                        "Bifold wallet hand-cut from vegetable-tanned full-grain leather and saddle-stitched with waxed " +
                        "thread. Six card slots and a bill compartment; the leather develops a rich patina over time."),
                    ("Cold-Process Soap Set", "HMD-0005", 22.00m, 100, "soap,handmade,bath",
                        "Set of four cold-process soaps made in small batches with olive and coconut oils and pure " +
                        "essential oils — lavender, oatmeal-honey, charcoal and citrus. Gentle and naturally scented."),
                    ("Soy Wax Scented Candle", "HMD-0006", 26.00m, 85, "candle,soy,wax",
                        "Hand-poured soy-wax candle in a reusable amber glass jar with a cotton wick and a 50-hour burn. " +
                        "Notes of cedar, amber and vanilla fill a room without being overpowering."),
                    ("Macramé Wall Hanging", "HMD-0007", 54.00m, 30, "macrame,wall,decor",
                        "Hand-knotted macramé wall hanging in natural cotton cord mounted on a smooth wooden dowel. " +
                        "A bohemian focal point that measures roughly 24 inches wide."),
                    ("Beeswax Candles (Set of 3)", "HMD-0008", 34.00m, 70, "beeswax,candle",
                        "Set of three hand-poured 100% beeswax pillar candles with cotton wicks and a natural honey " +
                        "scent. Beeswax burns cleanly and slowly for hours of warm, golden light."),
                    ("Olive Wood Cutting Board", "HMD-0009", 48.00m, 45, "wood,cutting,board",
                        "Cutting and serving board carved from a single piece of olive wood, showing unique natural " +
                        "grain. Finished with food-safe oil; hand-wash to preserve the wood's character."),
                    ("Hand-Woven Wool Scarf", "HMD-0010", 58.00m, 35, "wool,scarf,weave",
                        "Loom-woven scarf in soft lambswool with a fringed edge and a heathered herringbone pattern. " +
                        "Lightweight yet warm, and woven one at a time on a traditional floor loom."),
                    ("Stoneware Serving Bowl", "HMD-0011", 42.00m, 50, "stoneware,bowl,pottery",
                        "Hand-thrown stoneware serving bowl with a reactive glaze that pools in deep blues and greens. " +
                        "Generously sized for salads or fruit and safe for oven, microwave and dishwasher."),
                    ("Hand-Stitched Leather Journal", "HMD-0012", 38.00m, 55, "leather,journal,notebook",
                        "Refillable A5 journal bound in soft full-grain leather with hand-stitched signatures of 200 " +
                        "unlined pages. A wrap tie keeps it closed; the cover ages beautifully with use."),
                    ("Dried Flower Wreath", "HMD-0013", 46.00m, 25, "dried,flower,wreath",
                        "12-inch wreath arranged by hand from dried florals, wheat and eucalyptus on a grapevine base. " +
                        "A long-lasting, everlasting alternative to fresh flowers for a door or wall."),
                    ("Hand-Painted Ceramic Planter", "HMD-0014", 32.00m, 65, "ceramic,planter,pot",
                        "Glazed terracotta planter hand-painted with a minimalist line motif, with a drainage hole and " +
                        "matching saucer. Fits a 5-inch nursery pot for herbs or small houseplants."),
                    ("Knitted Wool Beanie", "HMD-0015", 29.00m, 75, "wool,beanie,hat",
                        "Chunky hand-knitted beanie in warm merino wool with a folded ribbed brim and a cosy stretch " +
                        "fit. Knitted to order in a range of naturally dyed colours.")
                });

            // (7) Store 4 — Clothing.
            AddStore(context,
                owner: new User
                {
                    FirstName = "Jonah", LastName = "Whitfield",
                    Email = "jonah@northline-apparel.com",
                    PhoneNumber = "+1 (312) 555-0164",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller123!"),
                    Role = UserRole.Seller
                },
                store: new Store
                {
                    Name = "Northline Apparel",
                    Slug = "northline-apparel",
                    Description = "Modern everyday essentials cut for a clean, comfortable fit and built from quality, " +
                                  "responsibly-sourced fabrics. Free exchanges on all sizes. Chicago, IL.",
                    Status = StoreStatus.Approved,
                    CommissionRate = 0.12m
                },
                category: clothing,
                lockStart: 146,
                items: new[]
                {
                    ("Heavyweight Fleece Hoodie", "CLO-0001", 54.99m, 90, "hoodie,sweatshirt",
                        "Heavyweight 400 gsm brushed-fleece pullover hoodie with a double-lined hood, ribbed cuffs and " +
                        "a roomy kangaroo pocket. Pre-shrunk cotton blend that keeps its shape wash after wash."),
                    ("Slim-Fit Stretch Jeans", "CLO-0002", 69.99m, 80, "jeans,denim",
                        "Slim-fit five-pocket jeans in comfort-stretch denim with just enough give for all-day wear. A " +
                        "mid-rise cut and a classic dark indigo wash pair with anything."),
                    ("Organic Cotton T-Shirt", "CLO-0003", 24.99m, 150, "tshirt,cotton,shirt",
                        "Everyday crew-neck tee in 100% organic ring-spun cotton with a soft hand-feel and a durable " +
                        "double-stitched hem. A relaxed regular fit that holds up to daily washing."),
                    ("Water-Resistant Bomber Jacket", "CLO-0004", 119.99m, 40, "bomber,jacket",
                        "Lightweight bomber jacket in water-resistant ripstop with a ribbed collar, YKK zips and a " +
                        "quilted lining. Packs down easily and layers over a hoodie in cooler weather."),
                    ("Low-Top Canvas Sneakers", "CLO-0005", 64.99m, 70, "sneakers,shoes,canvas",
                        "Classic low-top sneakers with a breathable cotton-canvas upper, cushioned insole and a " +
                        "vulcanised rubber sole. A timeless silhouette that goes with jeans or shorts."),
                    ("Merino Wool Sweater", "CLO-0006", 89.99m, 45, "sweater,wool,knit",
                        "Fine-gauge crew-neck sweater knitted from 100% extra-fine merino wool that's soft, breathable " +
                        "and naturally temperature-regulating. Ribbed trims hold their shape season after season."),
                    ("Slim Chino Trousers", "CLO-0007", 59.99m, 65, "chino,trousers,pants",
                        "Slim-tapered chinos in a peached cotton-twill with a hint of stretch and a clean, versatile " +
                        "leg. Dress them up with a shirt or down with a tee."),
                    ("Quilted Puffer Vest", "CLO-0008", 79.99m, 50, "vest,puffer,jacket",
                        "Lightweight quilted vest with recycled synthetic insulation, a stand collar and zip hand " +
                        "pockets. Adds core warmth as a mid-layer without bulking up your sleeves."),
                    ("Classic Oxford Shirt", "CLO-0009", 49.99m, 85, "oxford,shirt,button",
                        "Button-down Oxford shirt in breathable cotton with a structured collar and a tailored-but-easy " +
                        "fit. A wardrobe staple that works for the office or the weekend."),
                    ("Athletic Jogger Pants", "CLO-0010", 44.99m, 95, "joggers,pants,sweatpants",
                        "Tapered French-terry joggers with an elastic drawstring waist, zip pockets and ribbed ankle " +
                        "cuffs. Soft and stretchy for workouts, travel or lounging."),
                    ("Denim Trucker Jacket", "CLO-0011", 94.99m, 35, "denim,jacket,trucker",
                        "Classic trucker jacket in rigid 12 oz denim with button-flap chest pockets and adjustable waist " +
                        "tabs. A rugged layer that breaks in and fades to your own everyday piece."),
                    ("Ribbed Beanie Hat", "CLO-0012", 19.99m, 130, "beanie,hat,knit",
                        "Soft ribbed-knit beanie in an acrylic-wool blend with a fold-over cuff and a snug, stretchy " +
                        "fit. A cold-weather essential in a range of everyday colours."),
                    ("Full-Grain Leather Belt", "CLO-0013", 34.99m, 100, "leather,belt",
                        "1.5-inch belt cut from full-grain leather with a brushed-nickel roller buckle and edge-painted " +
                        "finish. A durable, minimal design that suits jeans and chinos alike."),
                    ("Flannel Button-Up Shirt", "CLO-0014", 47.99m, 60, "flannel,shirt,plaid",
                        "Brushed-cotton flannel shirt in a classic plaid with a soft, warm hand and a regular fit. " +
                        "Chest pocket, real buttons and a versatile layer for cool days."),
                    ("Crew Socks (5-Pack)", "CLO-0015", 16.99m, 140, "socks,crew",
                        "Five pairs of cushioned cotton-blend crew socks with arch support, a seamless toe and a " +
                        "ribbed cuff that stays up. Breathable everyday comfort in a neutral colour set.")
                });

            // (8) Store 5 — Jewelry.
            AddStore(context,
                owner: new User
                {
                    FirstName = "Sofia", LastName = "Bernard",
                    Email = "sofia@lumiere-jewelry.com",
                    PhoneNumber = "+1 (646) 555-0129",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller123!"),
                    Role = UserRole.Seller
                },
                store: new Store
                {
                    Name = "Lumière Fine Jewelry",
                    Slug = "lumiere-fine-jewelry",
                    Description = "Ethically-sourced fine and fashion jewellery, hand-finished by our bench jewellers. " +
                                  "Every piece ships in a gift box with a certificate of authenticity. New York, NY.",
                    Status = StoreStatus.Approved,
                    CommissionRate = 0.18m
                },
                category: jewelry,
                lockStart: 161,
                items: new[]
                {
                    ("18k Gold Chain Necklace", "JWL-0001", 899.00m, 12, "gold,necklace,chain",
                        "Solid 18k yellow-gold rope-chain necklace, 20 inches long, with a secure lobster clasp. A " +
                        "timeless everyday piece with a warm, high-polish shine. Comes gift-boxed with certification."),
                    ("Sterling Silver Band Ring", "JWL-0002", 79.00m, 60, "silver,ring",
                        "Minimalist 3 mm band in solid 925 sterling silver with a high-polish comfort-fit interior. " +
                        "Understated and stackable; hypoallergenic and tarnish-resistant with proper care."),
                    ("Diamond Stud Earrings 0.5ct", "JWL-0003", 1250.00m, 10, "diamond,earrings,studs",
                        "Classic four-prong diamond studs totalling 0.5 carat, set in 14k white gold with secure " +
                        "screw-backs. Ethically sourced, near-colourless stones with excellent brilliance."),
                    ("Diamond Tennis Bracelet", "JWL-0004", 340.00m, 20, "tennis,bracelet,diamond",
                        "Sparkling tennis bracelet lined with lab-grown diamonds in a rhodium-plated setting, finished " +
                        "with a safe box clasp and figure-eight catch. Flexible and comfortable on the wrist."),
                    ("Freshwater Pearl Pendant", "JWL-0005", 129.00m, 35, "pearl,pendant,necklace",
                        "Single lustrous freshwater pearl suspended from a delicate 16-inch sterling-silver chain. A " +
                        "classic, elegant pendant for everyday wear or special occasions."),
                    ("Rose Gold Hoop Earrings", "JWL-0006", 189.00m, 30, "rose,gold,hoop,earrings",
                        "Chunky 14k rose-gold-plated hoops with a lightweight hollow build and secure hinged clasps. " +
                        "A modern statement pair that stays comfortable all day."),
                    ("Sapphire Halo Ring", "JWL-0007", 780.00m, 15, "sapphire,ring,gemstone",
                        "Oval blue-sapphire ring encircled by a halo of pavé white sapphires in 14k white gold. A " +
                        "striking cocktail piece with vivid colour and plenty of sparkle."),
                    ("Cuban Link Chain Bracelet", "JWL-0008", 220.00m, 25, "cuban,link,bracelet",
                        "8 mm Cuban-link bracelet in gold-plated stainless steel with a hidden box clasp and a weighty, " +
                        "premium feel. A bold unisex accessory that resists tarnish."),
                    ("Birthstone Stackable Ring", "JWL-0009", 95.00m, 55, "birthstone,ring",
                        "Dainty stackable ring in 14k gold vermeil set with a single round birthstone. Designed to " +
                        "layer with other bands; choose a stone to mark a special month."),
                    ("Gold Bar Pendant Necklace", "JWL-0010", 260.00m, 28, "gold,bar,pendant",
                        "Sleek vertical gold-bar pendant in 14k solid gold on an adjustable 16–18 inch cable chain. A " +
                        "clean, contemporary layering piece that can be engraved on request."),
                    ("Emerald Drop Earrings", "JWL-0011", 640.00m, 14, "emerald,earrings,drop",
                        "Pear-cut green-emerald drop earrings framed in 14k gold with brilliant accent stones and " +
                        "leverback closures. Rich colour that catches the light with every movement."),
                    ("Personalized Name Necklace", "JWL-0012", 110.00m, 45, "name,necklace,gold",
                        "Custom nameplate necklace hand-cut in 18k gold-plated sterling silver on a 16-inch chain. Made " +
                        "to order in a flowing script — a thoughtful, personal gift."),
                    ("Silver Charm Bracelet", "JWL-0013", 85.00m, 50, "silver,charm,bracelet",
                        "Sterling-silver snake-chain charm bracelet with a secure clasp, ready to build with your own " +
                        "charms. Polished finish and a classic, collectable design."),
                    ("Moissanite Engagement Ring", "JWL-0014", 1450.00m, 12, "engagement,ring,moissanite",
                        "1.5-carat round brilliant moissanite solitaire in a 14k white-gold cathedral setting. Near-" +
                        "colourless and exceptionally brilliant — a conflict-free alternative to a diamond."),
                    ("Layered Choker Necklace", "JWL-0015", 140.00m, 40, "choker,necklace,layered",
                        "Two-strand layered choker in 14k gold vermeil combining a fine box chain and a satellite " +
                        "chain, with an adjustable extender. On-trend layering without the tangle.")
                });

            // (9) Persist everything in one transaction.
            context.SaveChanges();
        }

        /// <summary>
        /// Adds a seller, their store and 15 products (each with an inventory row and image) in one go.
        /// EF fixes up the foreign keys from the navigation properties on SaveChanges.
        /// </summary>
        private static void AddStore(
            AppDbContext context,
            User owner,
            Store store,
            Category category,
            int lockStart,
            (string Name, string Sku, decimal Price, int Stock, string ImageKeyword, string Description)[] items)
        {
            store.Owner = owner;
            context.Users.Add(owner);
            context.Stores.Add(store);

            var lockId = lockStart;
            foreach (var item in items)
            {
                // A low-stock threshold of ~10% of the seeded stock (min 5) keeps alerts realistic.
                var threshold = Math.Max(5, item.Stock / 10);

                context.Products.Add(new Product
                {
                    Name = item.Name,
                    Description = item.Description,
                    Sku = item.Sku,
                    Price = item.Price,
                    // loremflickr returns a real, keyword-matched Creative-Commons photo; ?lock pins a
                    // stable image per product so the same URL always resolves to the same picture.
                    ImageUrl = $"https://loremflickr.com/600/600/{item.ImageKeyword}?lock={lockId}",
                    Category = category,
                    Store = store,
                    Inventory = new Inventory
                    {
                        QuantityInStock = item.Stock,
                        LowStockThreshold = threshold
                    }
                });
                lockId++;
            }
        }

        /// <summary>
        /// Wipes every application table (resetting identity sequences) so the seed starts from a clean
        /// slate. Skipped for non-relational providers (the in-memory database used by unit tests).
        /// </summary>
        private static void ResetDatabase(AppDbContext context)
        {
            if (!context.Database.IsRelational())
                return;

            // Truncate every table in the public schema except EF's migration-history table. CASCADE
            // clears foreign-key children; RESTART IDENTITY resets the auto-increment sequences.
            context.Database.ExecuteSqlRaw(@"
                DO $$
                DECLARE r RECORD;
                BEGIN
                    FOR r IN
                        SELECT tablename FROM pg_tables
                        WHERE schemaname = 'public'
                          AND tablename <> '__EFMigrationsHistory'
                    LOOP
                        EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
                    END LOOP;
                END $$;");
        }
    }
}
