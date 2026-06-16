namespace PocApi.Models;

public record Customer(
    int    CustomerId,
    string Name,
    string Email,
    decimal TotalSpend,
    string Tier
);
