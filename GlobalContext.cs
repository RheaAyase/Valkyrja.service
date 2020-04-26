using Microsoft.EntityFrameworkCore;

using guid = System.UInt64;

namespace Valkyrja.entities
{
	public class GlobalContext: DbContext
	{
		public DbSet<Shard> Shards{ get; set; }
		public DbSet<ServerConfig> ServerConfigurations{ get; set; }

		private GlobalContext(DbContextOptions<GlobalContext> options) : base(options)
		{
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Shard>()
				.HasKey(p => p.Id);

			modelBuilder.Entity<ServerConfig>()
				.HasKey(p => p.ServerId);
		}

		public static GlobalContext Create(string connectionString)
		{
			DbContextOptionsBuilder<GlobalContext> optionsBuilder = new DbContextOptionsBuilder<GlobalContext>();
			optionsBuilder.UseMySql(connectionString);

			GlobalContext newContext = new GlobalContext(optionsBuilder.Options);
			newContext.Database.EnsureCreated();
			return newContext;
		}
	}
}
