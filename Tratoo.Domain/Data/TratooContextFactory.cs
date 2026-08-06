using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Tratoo.Domain.Data
{
    public class TratooContextFactory
        : IDesignTimeDbContextFactory<TratooContext>
    {
        public TratooContext CreateDbContext(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddUserSecrets<TratooContextFactory>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            var optionsBuilder = new DbContextOptionsBuilder<TratooContext>();

            // SetPostgresVersion evita que o EF precise abrir conexão real com o
            // banco só para detectar a versão do servidor — necessário para
            // `dotnet ef migrations add` funcionar mesmo com uma connection string
            // ainda não apontada para um Neon acessível. Ajustar se o Neon rodar
            // outro major (confere com `SELECT version();`).
            optionsBuilder.UseNpgsql(connectionString, o => o.SetPostgresVersion(16, 0));

            return new TratooContext(optionsBuilder.Options);
        }
    }
}