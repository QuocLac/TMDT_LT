using Microsoft.Extensions.DependencyInjection;

namespace TMDT_LT.Services.Inventory;

/// <summary>
/// Registers application services owned by the inventory module.
/// Keep module registrations here so Program.cs remains an application
/// composition root rather than a list of implementation details.
/// </summary>
public static class InventoryServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services)
    {
        services.AddScoped<InventoryDashboardService>();
        services.AddScoped<
            IInventoryDistributionService,
            InventoryDistributionService>();

        return services;
    }
}
