using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTemplate.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IFaqService, FaqService>();
        services.AddScoped<IChatService, ChatOrchestratorService>();
        return services;
    }
}
