using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Models;

namespace Cafe.API.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Dish & Orders
        public DbSet<Dish> Dishes { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;
        public DbSet<DishImage> DishImages { get; set; } = null!;

        // Addons
        public DbSet<AddonCategory> AddonCategories { get; set; } = null!;
        public DbSet<Addon> Addons { get; set; } = null!;
        public DbSet<DishAddon> DishAddons { get; set; } = null!;
        public DbSet<OrderItemAddon> OrderItemAddons { get; set; } = null!;

        // Auth
        public DbSet<UserAddress> UserAddresses { get; set; } = null!;
        public DbSet<SmsVerificationCode> SmsVerificationCodes { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

        // Promo codes
        public DbSet<PromoCode> PromoCodes { get; set; } = null!;

        // Loyalty points
        public DbSet<UserPointsTransaction> UserPointsTransactions { get; set; } = null!;

        // Favorites
        public DbSet<UserFavorite> UserFavorites { get; set; } = null!;

        // Reviews
        public DbSet<OrderReview> OrderReviews { get; set; } = null!;

        // Delivery zones
        public DbSet<DeliveryZone> DeliveryZones { get; set; } = null!;

        // Склад и техкарты
        public DbSet<StockItem> StockItems { get; set; } = null!;
        public DbSet<Supplier> Suppliers { get; set; } = null!;
        public DbSet<StockDocument> StockDocuments { get; set; } = null!;
        public DbSet<StockDocumentLine> StockDocumentLines { get; set; } = null!;
        public DbSet<StockMovement> StockMovements { get; set; } = null!;
        public DbSet<TechCard> TechCards { get; set; } = null!;
        public DbSet<TechCardLine> TechCardLines { get; set; } = null!;

        // Настройки из админки
        public DbSet<AppSetting> AppSettings { get; set; } = null!;

        // Честный знак
        public DbSet<ChzDocument> ChzDocuments { get; set; } = null!;
        public DbSet<EdoDocument> EdoDocuments { get; set; } = null!;
        public DbSet<MercuryDocument> MercuryDocuments { get; set; } = null!;
        public DbSet<KitchenStation> KitchenStations { get; set; } = null!;
        public DbSet<DiningTable> DiningTables { get; set; } = null!;
        public DbSet<TableCheck> TableChecks { get; set; } = null!;
        public DbSet<OrderItemVoid> OrderItemVoids { get; set; } = null!;
        public DbSet<WebPushSubscription> PushSubscriptions { get; set; } = null!;
        public DbSet<VenueShift> VenueShifts { get; set; } = null!;
        public DbSet<WaiterShift> WaiterShifts { get; set; } = null!;
        public DbSet<OrderStationStatus> OrderStationStatuses { get; set; } = null!;
        public DbSet<OrderItemKitchenState> OrderItemKitchenStates { get; set; } = null!;

        // Журналы ХАССП
        public DbSet<HaccpObject> HaccpObjects { get; set; } = null!;
        public DbSet<HaccpStaff> HaccpStaff { get; set; } = null!;
        public DbSet<HaccpEntry> HaccpEntries { get; set; } = null!;
        public DbSet<HaccpEntryRevision> HaccpEntryRevisions { get; set; } = null!;
        public DbSet<FiscalReceipt> FiscalReceipts { get; set; } = null!;
        public DbSet<EdoItemMapping> EdoItemMappings { get; set; } = null!;

        // ЕГАИС
        public DbSet<EgaisDocument> EgaisDocuments { get; set; } = null!;
        public DbSet<EgaisDocumentLine> EgaisDocumentLines { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Переименование таблиц Identity
            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.ToTable("users");
                entity.Property(u => u.Id).HasColumnName("Id");
                entity.HasIndex(u => u.TelegramUserId).IsUnique();
                entity.HasIndex(u => u.ReferralCode).IsUnique();
                entity.HasIndex(u => u.ReferredByUserId);
                entity.Property(u => u.TelegramNotifications).HasDefaultValue(true);
            });
            modelBuilder.Entity<IdentityRole<int>>(entity => entity.ToTable("roles"));
            modelBuilder.Entity<IdentityUserRole<int>>(entity => entity.ToTable("user_roles"));
            modelBuilder.Entity<IdentityUserClaim<int>>(entity => entity.ToTable("user_claims"));
            modelBuilder.Entity<IdentityUserLogin<int>>(entity => entity.ToTable("user_logins"));
            modelBuilder.Entity<IdentityRoleClaim<int>>(entity => entity.ToTable("role_claims"));
            modelBuilder.Entity<IdentityUserToken<int>>(entity => entity.ToTable("user_tokens"));

            // Order configuration
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.UserId);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.User)
                    .WithMany(u => u.Orders)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // OrderItem configuration
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasOne(e => e.Order)
                    .WithMany(o => o.OrderItems)
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Dish configuration
            modelBuilder.Entity<Dish>(entity =>
            {
                // Имя уникально в рамках одного родителя (или среди карточек)
                entity.HasIndex(e => new { e.ParentId, e.Name }).IsUnique();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // AddonCategory configuration
            modelBuilder.Entity<AddonCategory>(entity =>
            {
                entity.HasIndex(e => e.Name);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // Addon configuration
            modelBuilder.Entity<Addon>(entity =>
            {
                entity.HasIndex(e => e.Name);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.Category)
                    .WithMany(c => c.Addons)
                    .HasForeignKey(e => e.AddonCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // DishAddon configuration
            modelBuilder.Entity<DishAddon>(entity =>
            {
                entity.HasIndex(e => new { e.DishId, e.AddonId }).IsUnique();

                entity.HasOne(e => e.Dish)
                    .WithMany(s => s.Addons)
                    .HasForeignKey(e => e.DishId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Addon)
                    .WithMany(a => a.Dishes)
                    .HasForeignKey(e => e.AddonId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // OrderItemAddon configuration
            modelBuilder.Entity<OrderItemAddon>(entity =>
            {
                entity.HasOne(e => e.OrderItem)
                    .WithMany(oi => oi.SelectedAddons)
                    .HasForeignKey(e => e.OrderItemId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // DishImage configuration
            modelBuilder.Entity<DishImage>(entity =>
            {
                entity.HasOne(e => e.Dish)
                    .WithMany(s => s.Images)
                    .HasForeignKey(e => e.DishId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // UserAddress configuration
            modelBuilder.Entity<UserAddress>(entity =>
            {
                entity.HasIndex(e => e.UserId);
                entity.HasOne(e => e.User)
                    .WithMany(u => u.Addresses)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // SmsVerificationCode configuration
            modelBuilder.Entity<SmsVerificationCode>(entity =>
            {
                entity.HasIndex(e => e.Phone);
                entity.HasIndex(e => e.ExpiresAt);
            });

            // RefreshToken configuration
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasIndex(e => e.Token);
                entity.HasIndex(e => e.UserId);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // PromoCode configuration
            modelBuilder.Entity<PromoCode>(entity =>
            {
                entity.HasIndex(e => e.Code).IsUnique();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.Creator)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Order -> Courier configuration
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasIndex(e => e.PaymentId);
                entity.HasIndex(e => e.CourierId);
                entity.HasOne(e => e.Courier)
                    .WithMany()
                    .HasForeignKey(e => e.CourierId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Order -> PromoCode configuration
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasIndex(e => e.PromoCodeId);

                entity.HasOne(e => e.PromoCode)
                    .WithMany(p => p.Orders)
                    .HasForeignKey(e => e.PromoCodeId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // UserPointsTransaction configuration
            modelBuilder.Entity<UserPointsTransaction>(entity =>
            {
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => e.CreatedAt);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Order)
                    .WithMany()
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.PerformedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.PerformedBy)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // UserFavorite configuration
            modelBuilder.Entity<UserFavorite>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.DishId }).IsUnique();
                entity.HasIndex(e => e.DishId);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Dish)
                    .WithMany()
                    .HasForeignKey(e => e.DishId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // OrderReview configuration
            modelBuilder.Entity<OrderReview>(entity =>
            {
                entity.HasIndex(e => e.OrderId).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CreatedAt);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Order)
                    .WithMany()
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // DeliveryZone configuration
            modelBuilder.Entity<DeliveryZone>(entity =>
            {
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // Склад
            modelBuilder.Entity<StockItem>(entity =>
            {
                entity.HasIndex(e => e.Name);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<StockDocument>(entity =>
            {
                entity.HasIndex(e => new { e.Type, e.Date });
                entity.HasIndex(e => e.SupplierId);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Supplier)
                    .WithMany()
                    .HasForeignKey(e => e.SupplierId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasMany(e => e.Lines)
                    .WithOne(l => l.Document)
                    .HasForeignKey(l => l.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StockDocumentLine>(entity =>
            {
                entity.HasIndex(e => e.DocumentId);
                entity.HasIndex(e => e.StockItemId);
                entity.HasOne(e => e.StockItem)
                    .WithMany()
                    .HasForeignKey(e => e.StockItemId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockMovement>(entity =>
            {
                entity.HasIndex(e => new { e.StockItemId, e.CreatedAt });
                entity.HasIndex(e => e.DocumentId);
                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => new { e.Type, e.CreatedAt });
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.StockItem)
                    .WithMany()
                    .HasForeignKey(e => e.StockItemId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TechCard>(entity =>
            {
                // У каждой позиции меню, добавки и полуфабриката — не больше одной карты
                entity.HasIndex(e => e.DishId).IsUnique();
                entity.HasIndex(e => e.AddonId).IsUnique();
                entity.HasIndex(e => e.StockItemId).IsUnique();
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Dish)
                    .WithMany()
                    .HasForeignKey(e => e.DishId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Addon)
                    .WithMany()
                    .HasForeignKey(e => e.AddonId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.StockItem)
                    .WithMany()
                    .HasForeignKey(e => e.StockItemId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(e => e.Lines)
                    .WithOne(l => l.TechCard)
                    .HasForeignKey(l => l.TechCardId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TechCardLine>(entity =>
            {
                entity.HasIndex(e => e.TechCardId);
                entity.HasIndex(e => e.StockItemId);
                entity.HasOne(e => e.StockItem)
                    .WithMany()
                    .HasForeignKey(e => e.StockItemId)
                    .OnDelete(DeleteBehavior.Restrict);
                // Блюдо удалили из меню — строка с ним уходит из составов
                entity.HasIndex(e => e.SubDishId);
                entity.HasOne(e => e.SubDish)
                    .WithMany()
                    .HasForeignKey(e => e.SubDishId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.ToTable(t => t.HasCheckConstraint("ck_tech_card_lines_component",
                    "(stock_item_id IS NOT NULL AND sub_dish_id IS NULL) OR (stock_item_id IS NULL AND sub_dish_id IS NOT NULL)"));
            });

            modelBuilder.Entity<EgaisDocument>(entity =>
            {
                entity.HasIndex(e => e.WbRegId);
                entity.HasIndex(e => new { e.ShipperFsrarId, e.Number, e.Date });
                entity.HasIndex(e => e.Status);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasMany(e => e.Lines)
                    .WithOne(l => l.Document)
                    .HasForeignKey(l => l.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<EgaisDocumentLine>(entity =>
            {
                entity.HasIndex(e => e.DocumentId);
                entity.HasOne(e => e.StockItem)
                    .WithMany()
                    .HasForeignKey(e => e.StockItemId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<StockItem>(entity => entity.HasIndex(e => e.EgaisAlcCode));

            modelBuilder.Entity<HaccpObject>(entity => entity.HasIndex(e => new { e.Kind, e.IsActive }));

            modelBuilder.Entity<HaccpEntry>(entity =>
            {
                entity.HasIndex(e => new { e.Journal, e.OccurredAt });
                entity.HasIndex(e => e.ObjectId);
                entity.HasIndex(e => e.StaffId);
                entity.HasIndex(e => e.StockDocumentId);
                // Записи журнала не удаляются вместе с оборудованием или сотрудником
                entity.HasOne(e => e.Object).WithMany().HasForeignKey(e => e.ObjectId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<HaccpEntryRevision>(entity =>
            {
                entity.HasIndex(e => e.EntryId);
                entity.HasOne(e => e.Entry).WithMany().HasForeignKey(e => e.EntryId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderStationStatus>(entity =>
            {
                entity.HasIndex(e => new { e.OrderId, e.StationId }).IsUnique();
                entity.HasOne<Order>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<KitchenStation>().WithMany().HasForeignKey(e => e.StationId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TableCheck>(entity =>
            {
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.VenueShiftId);
                entity.HasIndex(e => e.WaiterShiftId);
                entity.HasOne(e => e.Table).WithMany().HasForeignKey(e => e.TableId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Order>()
                .HasOne<TableCheck>().WithMany().HasForeignKey(o => o.CheckId).OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<VenueShift>(entity =>
            {
                entity.HasIndex(e => e.BusinessDate);
                entity.HasIndex(e => e.ClosedAt);
            });

            modelBuilder.Entity<WaiterShift>(entity =>
            {
                entity.HasIndex(e => e.VenueShiftId);
                entity.HasIndex(e => e.UserId);
                entity.HasOne<VenueShift>().WithMany().HasForeignKey(e => e.VenueShiftId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<WebPushSubscription>(entity =>
            {
                entity.HasIndex(e => e.Endpoint).IsUnique();
                entity.HasIndex(e => e.UserId);
            });

            modelBuilder.Entity<OrderItemVoid>(entity =>
            {
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RequestedAt);
                entity.HasIndex(e => e.OrderId);
            });

            modelBuilder.Entity<OrderItem>()
                .HasOne<TableCheck>().WithMany().HasForeignKey(i => i.CheckId).OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<OrderItemKitchenState>(entity =>
            {
                entity.HasIndex(e => e.OrderItemId).IsUnique();
                entity.HasOne<OrderItem>().WithMany().HasForeignKey(e => e.OrderItemId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Dish>()
                .HasOne<KitchenStation>().WithMany().HasForeignKey(s => s.KitchenStationId).OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<FiscalReceipt>(entity =>
            {
                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => e.Status);
                entity.HasOne<Order>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MercuryDocument>(entity =>
            {
                entity.HasIndex(e => e.Uuid).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.IssueDate);
            });

            modelBuilder.Entity<EdoDocument>(entity =>
            {
                entity.HasIndex(e => e.SbisId).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.Date);
            });

            modelBuilder.Entity<EdoItemMapping>(entity =>
            {
                entity.HasIndex(e => new { e.SupplierInn, e.ItemKey }).IsUnique();
                entity.HasOne(e => e.StockItem).WithMany().HasForeignKey(e => e.StockItemId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChzDocument>(entity =>
            {
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.Status);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // Dish self-referencing hierarchy (parent_id)
            modelBuilder.Entity<Dish>(entity =>
            {
                entity.HasIndex(e => e.ParentId);
                entity.HasOne(e => e.Parent)
                    .WithMany(p => p.Children)
                    .HasForeignKey(e => e.ParentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
