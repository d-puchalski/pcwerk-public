using PcSell.Components;
using Services.Localization;
using Services.Configurator;
using Services.Ordering;
using Services.Catalog;
using EFDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using PcSell.Models;
using PcSell.Infrastructure;

namespace PcSell
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddDataProtection()
                .SetApplicationName("PCWerk");
            var databaseConnection = builder.Configuration.GetConnectionString("PcWerk")
                ?? throw new InvalidOperationException("Connection string 'PcWerk' is not configured.");
            builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
                options.UseNpgsql(databaseConnection));
            builder.Services.AddSingleton<EfProductCatalog>();
            builder.Services.AddSingleton<PcBuilderService>();
            builder.Services.AddSingleton<PcPresetCatalogService>();
            builder.Services.AddSingleton<LaptopCatalogService>();
            builder.Services.AddScoped<CartService>();
            builder.Services.AddScoped<CartRefreshService>();
            builder.Services.AddScoped<CartBrowserPersistence>();
            builder.Services.AddScoped<OrderService>();
            builder.Services.AddSingleton<IOrderRepository, EfOrderRepository>();
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            var emailOptions = builder.Configuration.GetSection("Email").Get<OrderEmailOptions>()
                ?? new OrderEmailOptions();
            builder.Services.AddSingleton(emailOptions);
            builder.Services.AddSingleton<IOrderEmailSender, SmtpOrderEmailSender>();
            var siteContact = builder.Configuration.GetSection("SiteContact").Get<SiteContactOptions>()
                ?? throw new InvalidOperationException("Site contact details are not configured.");
            builder.Services.AddSingleton(siteContact);
            builder.Services.AddSingleton(
                new JsonTranslationService(Path.Combine(builder.Environment.WebRootPath, "translations")));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
