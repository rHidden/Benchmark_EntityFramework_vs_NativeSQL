using Microsoft.EntityFrameworkCore;
using Entities;

namespace DbContextNamespace
{
    public class MyDbContext : DbContext
    {
        private readonly string _connectionString;

        public MyDbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(_connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SuperheroTeam>().HasNoKey();
            modelBuilder.Entity<SuperheroPower>().HasNoKey();
            modelBuilder.Entity<Superpower>().HasNoKey();
        }

        public DbSet<Superhero> Superheroes { get; set; }
        public DbSet<Superpower> Superpowers { get; set; }
        public DbSet<SuperheroPower> SuperheroPowers { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<SuperheroTeam> SuperheroTeams { get; set; }
    }
}
