using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buyit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillProductImageUrls : Migration
    {
        // Backfills accurate product image URLs onto the existing product rows.
        // The realistic-catalogue seeder only runs against an EMPTY database
        // (DbInitializer has an "if Users.Any() return" idempotency guard), so
        // updated ImageUrls never reach already-seeded environments (e.g. prod).
        // This migration runs on every deploy via db.Database.Migrate() and is
        // keyed on the stable Sku, so it corrects existing rows regardless of Id.
        // Products whose Sku is absent are simply skipped (0 rows affected).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/11340657/pexels-photo-11340657.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0001';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/10133274/pexels-photo-10133274.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0002';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/8532616/pexels-photo-8532616.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0003';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/19824500/pexels-photo-19824500.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0004';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/12526086/pexels-photo-12526086.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0005';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/14642651/pexels-photo-14642651.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0006';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/29547631/pexels-photo-29547631.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0007';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6001659/pexels-photo-6001659.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0008';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6931344/pexels-photo-6931344.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0009';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/9901666/pexels-photo-9901666.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0010';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/36607477/pexels-photo-36607477.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0011';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/37484033/pexels-photo-37484033.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0012';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/31959214/pexels-photo-31959214.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0013';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/14773602/pexels-photo-14773602.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0014';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/9594142/pexels-photo-9594142.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'CLO-0015';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/11129922/pexels-photo-11129922.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0001';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/9020272/pexels-photo-9020272.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0002';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7172697/pexels-photo-7172697.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0003';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/31406895/pexels-photo-31406895.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0004';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/326512/pexels-photo-326512.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0005';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/210927/pexels-photo-210927.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0006';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/4917455/pexels-photo-4917455.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0007';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7054723/pexels-photo-7054723.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0008';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7172701/pexels-photo-7172701.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0009';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/11216304/pexels-photo-11216304.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0010';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/15603968/pexels-photo-15603968.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0011';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/5083490/pexels-photo-5083490.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0012';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/27559487/pexels-photo-27559487.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0013';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6296911/pexels-photo-6296911.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0014';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/34195903/pexels-photo-34195903.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'ELC-0015';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6754875/pexels-photo-6754875.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0001';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/35392792/pexels-photo-35392792.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0002';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6717265/pexels-photo-6717265.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0003';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7952556/pexels-photo-7952556.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0004';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6621470/pexels-photo-6621470.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0005';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/35505878/pexels-photo-35505878.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0006';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/13211211/pexels-photo-13211211.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0007';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/10341349/pexels-photo-10341349.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0008';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7129393/pexels-photo-7129393.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0009';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/19346997/pexels-photo-19346997.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0010';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/17888788/pexels-photo-17888788.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0011';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/35810927/pexels-photo-35810927.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0012';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6232495/pexels-photo-6232495.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0013';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/35669377/pexels-photo-35669377.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0014';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/2333596/pexels-photo-2333596.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'HMD-0015';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/19564918/pexels-photo-19564918.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0001';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/4532560/pexels-photo-4532560.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0002';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/10976654/pexels-photo-10976654.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0003';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/20141640/pexels-photo-20141640.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0004';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7514818/pexels-photo-7514818.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0005';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7882737/pexels-photo-7882737.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0006';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/14058109/pexels-photo-14058109.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0007';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/10341191/pexels-photo-10341191.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0008';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/29967978/pexels-photo-29967978.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0009';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/29736434/pexels-photo-29736434.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0010';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/33837743/pexels-photo-33837743.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0011';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/14509757/pexels-photo-14509757.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0012';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/34549909/pexels-photo-34549909.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0013';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/10075092/pexels-photo-10075092.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0014';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/14355033/pexels-photo-14355033.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'JWL-0015';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/27915834/pexels-photo-27915834.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0001';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/34688570/pexels-photo-34688570.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0002';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/260044/pexels-photo-260044.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0003';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7743320/pexels-photo-7743320.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0004';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6246682/pexels-photo-6246682.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0005';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/27152933/pexels-photo-27152933.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0006';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7690201/pexels-photo-7690201.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0007';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6516206/pexels-photo-6516206.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0008';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/12956080/pexels-photo-12956080.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0009';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/6339679/pexels-photo-6339679.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0010';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/16513603/pexels-photo-16513603.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0011';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/7748773/pexels-photo-7748773.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0012';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/9391902/pexels-photo-9391902.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0013';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/14502821/pexels-photo-14502821.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0014';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = 'https://images.pexels.com/photos/5036927/pexels-photo-5036927.jpeg?auto=compress&cs=tinysrgb&w=1000&h=1000&fit=crop' WHERE \"Sku\" = 'SPT-0015';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No prior values are recorded; reverting clears the backfilled URLs
            // for the affected products.
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = NULL WHERE \"Sku\" IN ('CLO-0001', 'CLO-0002', 'CLO-0003', 'CLO-0004', 'CLO-0005', 'CLO-0006', 'CLO-0007', 'CLO-0008', 'CLO-0009', 'CLO-0010', 'CLO-0011', 'CLO-0012', 'CLO-0013', 'CLO-0014', 'CLO-0015', 'ELC-0001', 'ELC-0002', 'ELC-0003', 'ELC-0004', 'ELC-0005', 'ELC-0006', 'ELC-0007', 'ELC-0008', 'ELC-0009', 'ELC-0010', 'ELC-0011', 'ELC-0012', 'ELC-0013', 'ELC-0014', 'ELC-0015', 'HMD-0001', 'HMD-0002', 'HMD-0003', 'HMD-0004', 'HMD-0005', 'HMD-0006', 'HMD-0007', 'HMD-0008', 'HMD-0009', 'HMD-0010', 'HMD-0011', 'HMD-0012', 'HMD-0013', 'HMD-0014', 'HMD-0015', 'JWL-0001', 'JWL-0002', 'JWL-0003', 'JWL-0004', 'JWL-0005', 'JWL-0006', 'JWL-0007', 'JWL-0008', 'JWL-0009', 'JWL-0010', 'JWL-0011', 'JWL-0012', 'JWL-0013', 'JWL-0014', 'JWL-0015', 'SPT-0001', 'SPT-0002', 'SPT-0003', 'SPT-0004', 'SPT-0005', 'SPT-0006', 'SPT-0007', 'SPT-0008', 'SPT-0009', 'SPT-0010', 'SPT-0011', 'SPT-0012', 'SPT-0013', 'SPT-0014', 'SPT-0015');");
        }
    }
}
