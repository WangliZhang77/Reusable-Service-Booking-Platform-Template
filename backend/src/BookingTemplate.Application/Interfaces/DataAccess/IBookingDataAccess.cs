using BookingTemplate.Domain.Entities;

namespace BookingTemplate.Application.Interfaces.DataAccess;

public interface IBookingDataAccess
{
    Task<List<Service>> GetActiveServicesAsync(CancellationToken cancellationToken);
    Task<Service?> GetActiveServiceByIdAsync(Guid serviceId, CancellationToken cancellationToken);
    Task<BusinessHour?> GetBusinessHourByWeekdayAsync(short weekday, CancellationToken cancellationToken);
    Task<List<Booking>> GetBookingsByDateAsync(DateOnly date, CancellationToken cancellationToken);
    Task<List<Booking>> GetBookingsAsync(DateOnly? date, CancellationToken cancellationToken);
    Task<Booking?> GetBookingByIdAsync(Guid bookingId, CancellationToken cancellationToken);
    Task<Customer?> GetCustomerByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<Pet?> GetPetByCustomerAndNameAsync(Guid customerId, string petName, CancellationToken cancellationToken);
    Task<List<Faq>> GetAllFaqsAsync(CancellationToken cancellationToken);
    Task<List<Faq>> GetPublishedFaqsAsync(CancellationToken cancellationToken);
    Task<List<Faq>> GetPublishedFaqsByCategoryAsync(string category, CancellationToken cancellationToken);
    Task AddBookingAsync(Booking booking, CancellationToken cancellationToken);
    Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken);
    Task AddPetAsync(Pet pet, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs work inside a Serializable DB transaction so concurrent creates cannot both pass overlap checks.
    /// </summary>
    Task<T> ExecuteInSerializableTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
