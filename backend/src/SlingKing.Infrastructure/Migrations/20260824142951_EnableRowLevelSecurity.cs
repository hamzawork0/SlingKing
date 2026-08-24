using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlingKing.Infrastructure.Migrations
{
    /// <summary>
    /// Supabase publishes every table in the exposed schema through PostgREST, reachable
    /// by anyone holding the publishable key — a key that is designed to be shipped inside
    /// a frontend bundle. Without row level security that is anonymous read and write
    /// access to <c>users</c> (which holds BCrypt hashes) and <c>refresh_tokens</c>.
    ///
    /// Enabling RLS with NO policies denies the <c>anon</c> and <c>authenticated</c> roles
    /// outright, which is exactly what we want: this application never speaks to PostgREST.
    /// It connects as <c>postgres</c>, which carries <c>rolbypassrls</c>, so the API is
    /// entirely unaffected.
    ///
    /// ENABLE, never FORCE. FORCE would subject the table owner to RLS as well, and the
    /// owner is us — it would lock the application out of its own database.
    /// </summary>
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // current_schema() rather than a literal, so this applies correctly to the
            // throwaway 'test' schema the integration tests migrate into.
            migrationBuilder.Sql(@"
do $$
declare t record;
begin
  for t in select tablename from pg_tables where schemaname = current_schema()
  loop
    execute format('alter table %I enable row level security', t.tablename);
  end loop;
end $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
do $$
declare t record;
begin
  for t in select tablename from pg_tables where schemaname = current_schema()
  loop
    execute format('alter table %I disable row level security', t.tablename);
  end loop;
end $$;");
        }
    }
}
