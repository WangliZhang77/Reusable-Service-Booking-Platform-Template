using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Domain.Entities;
using BookingTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingTemplate.Infrastructure.DataAccess;

public sealed class BookingDataAccess(AppDbContext dbContext) : IBookingDataAccess
{
    public Task<List<Service>> GetActiveServicesAsync(CancellationToken cancellationToken)
    {
        return dbContext.Services
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
    }

    public Task<Service?> GetActiveServiceByIdAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        return dbContext.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serviceId && x.IsActive, cancellationToken);
    }

    public Task<BusinessHour?> GetBusinessHourByWeekdayAsync(short weekday, CancellationToken cancellationToken)
    {
        return dbContext.BusinessHours
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Weekday == weekday, cancellationToken);
    }

    public Task<List<Booking>> GetBookingsByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        return dbContext.Bookings
            .AsNoTracking()
            .Where(x => x.BookingDate == date)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Booking>> GetBookingsAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        var query = dbContext.Bookings
            .AsNoTracking()
            .Include(x => x.Service)
            .Include(x => x.Customer)
            .Include(x => x.Pet)
            .AsQueryable();

        if (date.HasValue)
        {
            query = query.Where(x => x.BookingDate == date.Value);
        }

        return query.ToListAsync(cancellationToken);
    }

    public Task<Booking?> GetBookingByIdAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        return dbContext.Bookings
            .Include(x => x.Service)
            .Include(x => x.Customer)
            .Include(x => x.Pet)
            .FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
    }

    public Task<Customer?> GetCustomerByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        return dbContext.Customers
            .FirstOrDefaultAsync(x => x.Phone == phone, cancellationToken);
    }

    public Task<Pet?> GetPetByCustomerAndNameAsync(Guid customerId, string petName, CancellationToken cancellationToken)
    {
        return dbContext.Pets
            .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.Name == petName, cancellationToken);
    }

    public Task<List<Faq>> GetAllFaqsAsync(CancellationToken cancellationToken)
    {
        return dbContext.Faqs
            .AsNoTracking()
            .OrderBy(x => x.Category)
            .ThenBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Faq>> GetPublishedFaqsAsync(CancellationToken cancellationToken)
    {
        return dbContext.Faqs
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Faq>> GetPublishedFaqsByCategoryAsync(string category, CancellationToken cancellationToken)
    {
        return dbContext.Faqs
            .AsNoTracking()
            .Where(x => x.IsPublished && x.Category != null && x.Category.ToLower() == category.ToLower())
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public Task AddBookingAsync(Booking booking, CancellationToken cancellationToken)
    {
        return dbContext.Bookings.AddAsync(booking, cancellationToken).AsTask();
    }

    public Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken)
    {
        return dbContext.Customers.AddAsync(customer, cancellationToken).AsTask();
    }

    public Task AddPetAsync(Pet pet, CancellationToken cancellationToken)
    {
        return dbContext.Pets.AddAsync(pet, cancellationToken).AsTask();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
