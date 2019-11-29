using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

using guid = System.UInt64;

namespace Valkyrja.entities
{
	public class GlobalContext: DbContext
	{
		public DbSet<Shard> Shards;
		public DbSet<ServerConfig> ServerConfigurations;

		public GlobalContext(DbContextOptions<GlobalContext> options) : base(options)
		{
			this.Shards = new InternalDbSet<Shard>(this);
			this.ServerConfigurations = new InternalDbSet<ServerConfig>(this);
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
