namespace WebApp.Configuration;

/// <summary>The message contract shared (by copy, not by reference) between the publisher here and the Functions receiver.</summary>
public record OrderCreatedMessage(int OrderId, string Customer, decimal Amount, DateTime CreatedAtUtc);
