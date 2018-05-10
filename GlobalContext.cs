using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Pomelo.EntityFrameworkCore.MySql;

using guid = System.UInt64;

namespace Botwinder.entities
{
	public class GlobalContext: DbContext
	{
		public DbSet<Shard> Shards;

		public GlobalContext(DbContextOptions<GlobalContext> options) : base(options)
		{
			this.Shards = new InternalDbSet<Shard>(this);
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Shard>()
				.HasKey(p => p.Id);
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
