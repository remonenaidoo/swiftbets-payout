namespace SwiftBets.Payout.Domain;

public enum PostingType
{
    None,
    Win,
    VoidRefund,
    ResettleCredit,
    ResettleDebit,
}
