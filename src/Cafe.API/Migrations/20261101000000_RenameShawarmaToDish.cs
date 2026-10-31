using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>
    /// Нейтральные названия для любого кафе: shawarmas → dishes, shawarma_images → dish_images,
    /// shawarma_addons → dish_addons, столбцы shawarma_id → dish_id, а с ними индексы, ключи и последовательности.
    /// Пометка «с сыром» (has_cheese) убрана. Данные сохраняются.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261101000000_RenameShawarmaToDish")]
    public partial class RenameShawarmaToDish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE r record;
BEGIN
    IF to_regclass('public.shawarmas') IS NOT NULL THEN ALTER TABLE shawarmas RENAME TO dishes; END IF;
    IF to_regclass('public.shawarma_images') IS NOT NULL THEN ALTER TABLE shawarma_images RENAME TO dish_images; END IF;
    IF to_regclass('public.shawarma_addons') IS NOT NULL THEN ALTER TABLE shawarma_addons RENAME TO dish_addons; END IF;

    FOR r IN SELECT table_name FROM information_schema.columns
             WHERE table_schema = 'public' AND column_name = 'shawarma_id' LOOP
        EXECUTE format('ALTER TABLE %I RENAME COLUMN shawarma_id TO dish_id', r.table_name);
    END LOOP;

    -- Внешние ключи (у них нет своего индекса)
    FOR r IN SELECT c.conrelid::regclass::text AS tbl, c.conname FROM pg_constraint c
             WHERE c.connamespace = 'public'::regnamespace AND c.contype = 'f' AND c.conname LIKE '%shawarma%' LOOP
        EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO %I', r.tbl, r.conname,
            replace(replace(r.conname, 'shawarmas', 'dishes'), 'shawarma', 'dish'));
    END LOOP;

    -- Индексы; переименование индекса первичного ключа переименовывает и сам ключ
    FOR r IN SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname LIKE '%shawarma%' LOOP
        EXECUTE format('ALTER INDEX %I RENAME TO %I', r.indexname,
            replace(replace(r.indexname, 'shawarmas', 'dishes'), 'shawarma', 'dish'));
    END LOOP;

    FOR r IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' AND sequencename LIKE '%shawarma%' LOOP
        EXECUTE format('ALTER SEQUENCE %I RENAME TO %I', r.sequencename,
            replace(replace(r.sequencename, 'shawarmas', 'dishes'), 'shawarma', 'dish'));
    END LOOP;
END $$;");

            migrationBuilder.DropColumn(
                name: "has_cheese",
                table: "dishes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_cheese",
                table: "dishes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Обратно; sub_dish_id (состав блюда) не трогаем
            migrationBuilder.Sql(@"
DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' AND sequencename ~ '(^|_)dish(es)?(_|$)' AND sequencename NOT LIKE '%sub_dish%' LOOP
        EXECUTE format('ALTER SEQUENCE %I RENAME TO %I', r.sequencename,
            regexp_replace(regexp_replace(r.sequencename, 'dishes', 'shawarmas', 'g'), '(^|_)dish(_|$)', '\1shawarma\2', 'g'));
    END LOOP;

    FOR r IN SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname ~ '(^|_)dish(es)?(_|$)' AND indexname NOT LIKE '%sub_dish%' LOOP
        EXECUTE format('ALTER INDEX %I RENAME TO %I', r.indexname,
            regexp_replace(regexp_replace(r.indexname, 'dishes', 'shawarmas', 'g'), '(^|_)dish(_|$)', '\1shawarma\2', 'g'));
    END LOOP;

    FOR r IN SELECT c.conrelid::regclass::text AS tbl, c.conname FROM pg_constraint c
             WHERE c.connamespace = 'public'::regnamespace AND c.contype = 'f' AND c.conname ~ '(^|_)dish(es)?(_|$)' LOOP
        EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO %I', r.tbl, r.conname,
            regexp_replace(regexp_replace(replace(r.conname, 'sub_dish_id', '@@SUB@@'), 'dishes', 'shawarmas', 'g'), '(^|_)dish(_|$)', '\1shawarma\2', 'g'));
    END LOOP;
    FOR r IN SELECT c.conrelid::regclass::text AS tbl, c.conname FROM pg_constraint c
             WHERE c.connamespace = 'public'::regnamespace AND c.conname LIKE '%@@SUB@@%' LOOP
        EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO %I', r.tbl, r.conname, replace(r.conname, '@@SUB@@', 'sub_dish_id'));
    END LOOP;

    FOR r IN SELECT table_name FROM information_schema.columns
             WHERE table_schema = 'public' AND column_name = 'dish_id' LOOP
        EXECUTE format('ALTER TABLE %I RENAME COLUMN dish_id TO shawarma_id', r.table_name);
    END LOOP;

    IF to_regclass('public.dish_addons') IS NOT NULL THEN ALTER TABLE dish_addons RENAME TO shawarma_addons; END IF;
    IF to_regclass('public.dish_images') IS NOT NULL THEN ALTER TABLE dish_images RENAME TO shawarma_images; END IF;
    IF to_regclass('public.dishes') IS NOT NULL THEN ALTER TABLE dishes RENAME TO shawarmas; END IF;
END $$;");
        }
    }
}
