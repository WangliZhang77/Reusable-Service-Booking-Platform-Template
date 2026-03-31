using BookingTemplate.Application.DTOs.Chat;

namespace BookingTemplate.Application.Services;

/// <summary>
/// 在服务端根据意图计算必填项，避免完全依赖模型列出的 missingFields。
/// </summary>
public static class BookingIntentMissingFields
{
    public static IReadOnlyList<string> Compute(BookingIntentExtractionDto x)
    {
        var intent = (x.Intent ?? "general").Trim().ToLowerInvariant();
        var list = new List<string>();

        switch (intent)
        {
            case "availability":
                if (string.IsNullOrWhiteSpace(x.ServiceName))
                {
                    list.Add("serviceName");
                }

                if (string.IsNullOrWhiteSpace(x.Date))
                {
                    list.Add("date");
                }

                break;

            case "booking":
                if (string.IsNullOrWhiteSpace(x.ServiceName))
                {
                    list.Add("serviceName");
                }

                if (string.IsNullOrWhiteSpace(x.Date))
                {
                    list.Add("date");
                }

                if (string.IsNullOrWhiteSpace(x.StartTime))
                {
                    list.Add("startTime");
                }

                if (string.IsNullOrWhiteSpace(x.CustomerName))
                {
                    list.Add("customerName");
                }

                if (string.IsNullOrWhiteSpace(x.Phone))
                {
                    list.Add("phone");
                }

                if (string.IsNullOrWhiteSpace(x.PetName))
                {
                    list.Add("petName");
                }

                if (string.IsNullOrWhiteSpace(x.PetType))
                {
                    list.Add("petType");
                }

                break;

            case "price":
                if (string.IsNullOrWhiteSpace(x.ServiceName))
                {
                    list.Add("serviceName");
                }

                break;

            case "faq":
                if (string.IsNullOrWhiteSpace(x.FaqQuery))
                {
                    list.Add("faqQuery");
                }

                break;
        }

        return list;
    }
}
