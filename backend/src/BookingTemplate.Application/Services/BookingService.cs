using BookingTemplate.Application.DTOs.Availability;
using BookingTemplate.Application.DTOs.Bookings;
using BookingTemplate.Application.DTOs.Common;
using BookingTemplate.Application.DTOs.Services;
using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Domain.Entities;
using BookingTemplate.Domain.Enums;

namespace BookingTemplate.Application.Services;

public sealed class BookingService(IBookingDataAccess dataAccess) : IBookingService
{
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        return services
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(MapServiceDto)
            .ToList();
    }

    public async Task<AvailabilityResponseDto> GetAvailabilityAsync(Guid serviceId, DateOnly date, CancellationToken cancellationToken)
    {
        if (serviceId == Guid.Empty)
        {
            throw new ArgumentException("ServiceId is required.");
        }

        ValidateBookingDate(date);

        var service = await dataAccess.GetActiveServiceByIdAsync(serviceId, cancellationToken)
            ?? throw new KeyNotFoundException("Service not found or inactive.");

        var businessHour = await GetOpenBusinessHourOrThrowAsync(date, cancellationToken);
        var existingBookings = await GetActiveBookingsByDateAsync(date, cancellationToken);

        var slots = BuildSlots(service.DurationMinutes, businessHour, existingBookings);
        return new AvailabilityResponseDto(service.Id, date, slots);
    }

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequestDto request, CancellationToken cancellationToken)
    {
        ValidateCreateBookingRequest(request);

        var service = await dataAccess.GetActiveServiceByIdAsync(request.ServiceId, cancellationToken)
            ?? throw new KeyNotFoundException("Selected service does not exist.");

        var businessHour = await GetOpenBusinessHourOrThrowAsync(request.BookingDate, cancellationToken);

        var start = request.StartTime;
        var end = request.StartTime.AddMinutes(service.DurationMinutes);

        ValidateSlotInsideBusinessHours(businessHour, start, end);
        ValidateSlotAlignment(businessHour, start);

        var existingBookings = await GetActiveBookingsByDateAsync(request.BookingDate, cancellationToken);
        if (HasOverlap(existingBookings, start, end))
        {
            throw new InvalidOperationException("The selected time slot is no longer available.");
        }

        var customer = await UpsertCustomerAsync(request.Customer, cancellationToken);
        var pet = await UpsertPetAsync(customer.Id, request.Pet, cancellationToken);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            ServiceId = service.Id,
            CustomerId = customer.Id,
            PetId = pet.Id,
            BookingDate = request.BookingDate,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CustomerMessage = Clean(request.CustomerMessage),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await dataAccess.AddBookingAsync(booking, cancellationToken);
        await dataAccess.SaveChangesAsync(cancellationToken);

        return MapBookingDto(booking, service.Name, customer.FullName, customer.Phone, pet.Name);
    }

    public async Task<IReadOnlyList<BookingDto>> GetBookingsAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        var bookings = await dataAccess.GetBookingsAsync(date, cancellationToken);
        return bookings
            .OrderBy(x => x.BookingDate)
            .ThenBy(x => x.StartTime)
            .Select(x => MapBookingDto(x, x.Service.Name, x.Customer.FullName, x.Customer.Phone, x.Pet.Name))
            .ToList();
    }

    public async Task<BookingDto> UpdateBookingStatusAsync(
        Guid bookingId,
        BookingStatusDto status,
        string? adminNotes,
        string? cancelReason,
        CancellationToken cancellationToken)
    {
        if (bookingId == Guid.Empty)
        {
            throw new ArgumentException("BookingId is required.");
        }

        var booking = await dataAccess.GetBookingByIdAsync(bookingId, cancellationToken)
            ?? throw new KeyNotFoundException("Booking not found.");

        var targetStatus = MapStatus(status);
        ValidateStatusTransition(booking.Status, targetStatus);

        booking.Status = targetStatus;
        booking.AdminNotes = Clean(adminNotes);
        booking.UpdatedAt = DateTimeOffset.UtcNow;

        if (targetStatus == BookingStatus.Confirmed)
        {
            booking.ConfirmedAt = DateTimeOffset.UtcNow;
        }
        else if (targetStatus == BookingStatus.Cancelled)
        {
            booking.CancelReason = Clean(cancelReason);
            booking.CancelledAt = DateTimeOffset.UtcNow;
        }
        else if (targetStatus == BookingStatus.Completed)
        {
            booking.CompletedAt = DateTimeOffset.UtcNow;
        }

        await dataAccess.SaveChangesAsync(cancellationToken);

        return MapBookingDto(booking, booking.Service.Name, booking.Customer.FullName, booking.Customer.Phone, booking.Pet.Name);
    }

    private static void ValidateCreateBookingRequest(CreateBookingRequestDto request)
    {
        if (request.ServiceId == Guid.Empty)
        {
            throw new ArgumentException("ServiceId is required.");
        }

        ValidateBookingDate(request.BookingDate);

        if (string.IsNullOrWhiteSpace(request.Customer.FullName))
        {
            throw new ArgumentException("Customer full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Customer.Phone))
        {
            throw new ArgumentException("Customer phone is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Pet.Name))
        {
            throw new ArgumentException("Pet name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Pet.Species))
        {
            throw new ArgumentException("Pet species is required.");
        }
    }

    private static void ValidateBookingDate(DateOnly bookingDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (bookingDate < today)
        {
            throw new ArgumentException("Booking date cannot be in the past.");
        }
    }

    private static void ValidateSlotInsideBusinessHours(BusinessHour businessHour, TimeOnly start, TimeOnly end)
    {
        if (businessHour.OpenTime is null || businessHour.CloseTime is null)
        {
            throw new InvalidOperationException("Business hours are not configured.");
        }

        if (start < businessHour.OpenTime || end > businessHour.CloseTime)
        {
            throw new InvalidOperationException("Selected slot is outside business hours.");
        }
    }

    private static void ValidateSlotAlignment(BusinessHour businessHour, TimeOnly start)
    {
        if (businessHour.OpenTime is null)
        {
            throw new InvalidOperationException("Business opening time is not configured.");
        }

        var minutesFromOpen = (start.ToTimeSpan() - businessHour.OpenTime.Value.ToTimeSpan()).TotalMinutes;
        if (minutesFromOpen < 0 || minutesFromOpen % businessHour.SlotIntervalMinutes != 0)
        {
            throw new InvalidOperationException("Selected start time does not align with slot interval.");
        }
    }

    private async Task<BusinessHour> GetOpenBusinessHourOrThrowAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var weekday = (short)date.DayOfWeek;
        var businessHour = await dataAccess.GetBusinessHourByWeekdayAsync(weekday, cancellationToken)
            ?? throw new InvalidOperationException("Business hours are not configured for the selected day.");

        if (!businessHour.IsOpen)
        {
            throw new InvalidOperationException("Business is closed on the selected day.");
        }

        return businessHour;
    }

    private async Task<List<Booking>> GetActiveBookingsByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var all = await dataAccess.GetBookingsByDateAsync(date, cancellationToken);
        return all.Where(x => x.Status != BookingStatus.Cancelled).ToList();
    }

    private async Task<Customer> UpsertCustomerAsync(CustomerInputDto customerInput, CancellationToken cancellationToken)
    {
        var normalizedPhone = customerInput.Phone.Trim();
        var existingCustomer = await dataAccess.GetCustomerByPhoneAsync(normalizedPhone, cancellationToken);

        if (existingCustomer is not null)
        {
            existingCustomer.FullName = customerInput.FullName.Trim();
            existingCustomer.Email = Clean(customerInput.Email);
            existingCustomer.UpdatedAt = DateTimeOffset.UtcNow;
            return existingCustomer;
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FullName = customerInput.FullName.Trim(),
            Phone = normalizedPhone,
            Email = Clean(customerInput.Email),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await dataAccess.AddCustomerAsync(customer, cancellationToken);
        return customer;
    }

    private async Task<Pet> UpsertPetAsync(Guid customerId, PetInputDto petInput, CancellationToken cancellationToken)
    {
        var normalizedPetName = petInput.Name.Trim();
        var existingPet = await dataAccess.GetPetByCustomerAndNameAsync(customerId, normalizedPetName, cancellationToken);

        if (existingPet is not null)
        {
            existingPet.Species = petInput.Species.Trim();
            existingPet.Breed = Clean(petInput.Breed);
            existingPet.Size = petInput.Size;
            existingPet.SpecialNotes = Clean(petInput.SpecialNotes);
            existingPet.UpdatedAt = DateTimeOffset.UtcNow;
            return existingPet;
        }

        var pet = new Pet
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Name = normalizedPetName,
            Species = petInput.Species.Trim(),
            Breed = Clean(petInput.Breed),
            Size = petInput.Size,
            SpecialNotes = Clean(petInput.SpecialNotes),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await dataAccess.AddPetAsync(pet, cancellationToken);
        return pet;
    }

    private static List<AvailabilitySlotDto> BuildSlots(
        int durationMinutes,
        BusinessHour businessHour,
        IReadOnlyCollection<Booking> existingBookings)
    {
        if (businessHour.OpenTime is null || businessHour.CloseTime is null)
        {
            return [];
        }

        var slots = new List<AvailabilitySlotDto>();
        var current = businessHour.OpenTime.Value;
        var latestStart = businessHour.CloseTime.Value.AddMinutes(-durationMinutes);

        while (current <= latestStart)
        {
            var slotEnd = current.AddMinutes(durationMinutes);
            var available = !HasOverlap(existingBookings, current, slotEnd);
            slots.Add(new AvailabilitySlotDto(current, slotEnd, available));
            current = current.AddMinutes(businessHour.SlotIntervalMinutes);
        }

        return slots;
    }

    private static bool HasOverlap(IEnumerable<Booking> bookings, TimeOnly start, TimeOnly end)
    {
        return bookings.Any(x => start < x.EndTime && x.StartTime < end);
    }

    private static BookingStatusDto MapStatusDto(BookingStatus status) => status switch
    {
        BookingStatus.Pending => BookingStatusDto.Pending,
        BookingStatus.Confirmed => BookingStatusDto.Confirmed,
        BookingStatus.Cancelled => BookingStatusDto.Cancelled,
        BookingStatus.Completed => BookingStatusDto.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown booking status.")
    };

    private static BookingStatus MapStatus(BookingStatusDto status) => status switch
    {
        BookingStatusDto.Pending => BookingStatus.Pending,
        BookingStatusDto.Confirmed => BookingStatus.Confirmed,
        BookingStatusDto.Cancelled => BookingStatus.Cancelled,
        BookingStatusDto.Completed => BookingStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown booking status.")
    };

    private static BookingDto MapBookingDto(
        Booking booking,
        string serviceName,
        string customerName,
        string customerPhone,
        string petName)
    {
        return new BookingDto(
            booking.Id,
            booking.ServiceId,
            serviceName,
            booking.CustomerId,
            customerName,
            customerPhone,
            booking.PetId,
            petName,
            booking.BookingDate,
            booking.StartTime,
            booking.EndTime,
            MapStatusDto(booking.Status),
            booking.CustomerMessage,
            booking.AdminNotes,
            booking.CreatedAt);
    }

    private static ServiceDto MapServiceDto(Service service)
    {
        return new ServiceDto(
            service.Id,
            service.Name,
            service.Description,
            service.DurationMinutes,
            service.Price);
    }

    private static void ValidateStatusTransition(BookingStatus current, BookingStatus target)
    {
        if (current == BookingStatus.Completed && target != BookingStatus.Completed)
        {
            throw new InvalidOperationException("Completed booking cannot be changed.");
        }

        if (current == BookingStatus.Cancelled && target != BookingStatus.Cancelled)
        {
            throw new InvalidOperationException("Cancelled booking cannot be changed.");
        }

        if (current == BookingStatus.Pending &&
            target is not (BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Cancelled))
        {
            throw new InvalidOperationException("Pending booking can only become confirmed or cancelled.");
        }

        if (current == BookingStatus.Confirmed &&
            target is not (BookingStatus.Confirmed or BookingStatus.Completed or BookingStatus.Cancelled))
        {
            throw new InvalidOperationException("Confirmed booking can only become completed or cancelled.");
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
