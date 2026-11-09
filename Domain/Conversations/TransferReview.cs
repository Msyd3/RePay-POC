namespace RePay.WhatsAppPoc.Services;

public sealed record TransferReviewPerson(string Id, string? Avatar, string Name, string Phone);

public sealed record TransferReview(
    string Id,
    string Status,
    string AccountName,
    TransferReviewPerson Sender,
    TransferReviewPerson Recipient,
    int AmountMinor,
    string Currency,
    string? Note);
