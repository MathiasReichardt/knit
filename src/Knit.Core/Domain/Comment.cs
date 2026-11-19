namespace Knit.Core.Domain;

public sealed record Comment(
    string Id,
    string Author,
    DateTimeOffset Created,
    string Body
);
