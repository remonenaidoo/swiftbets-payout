using System.ComponentModel.DataAnnotations;

namespace SwiftBets.Payout.Infrastructure.Messaging;

public sealed class LadderOptions
{
    public const string SectionName = "Payout:Ladder";

    /// <summary>Exactly three rungs (standard: 5, 60, 900). No default here: configuration binding appends to array defaults.</summary>
    [MinLength(3)]
    [MaxLength(3)]
    public int[] RungSeconds { get; set; } = [];
}
