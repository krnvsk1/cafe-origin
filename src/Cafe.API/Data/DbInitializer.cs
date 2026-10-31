using Microsoft.EntityFrameworkCore;
using Cafe.API.Models;

namespace Cafe.API.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            Console.WriteLine("🔍 Начало инициализации базы данных...");
            
            // Применяем миграции если есть
            context.Database.Migrate();
            
            Console.WriteLine("✅ Миграции применены");
            
            // Данные добавляются через админ-панель или миграции
            // Seed data strategy: items are created via the admin API after deployment
            
            Console.WriteLine("🎉 Инициализация базы данных завершена успешно!");
        }
    }
}
