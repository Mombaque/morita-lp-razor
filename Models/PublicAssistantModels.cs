using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morita.LP.Razor.Models;

public sealed class CreatePublicAssistantSessionRequest
{
    public bool AcceptedAiNotice { get; set; }
    public string AiNoticeVersion { get; set; } = "public-assistant-v1";
    public string? LandingPage { get; set; }
    public string? Campaign { get; set; }
    public string? InitialProductSlug { get; set; }
    public string? Website { get; set; }
}

public sealed class PublicAssistantSession
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessToken { get; set; }
    public Guid PublicId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DraftRevision { get; set; }
    public JsonDocument Draft { get; set; } = JsonDocument.Parse("{\"items\":[]}");
    public List<PublicAssistantMessage> Messages { get; set; } = [];
    public string ActionType { get; set; } = "None";
    public string? Summary { get; set; }
    public string? ConfirmationToken { get; set; }
}

public sealed class PublicAssistantMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class PublicAssistantMessageRequest
{
    public Guid ClientMessageId { get; set; }
    public int ExpectedRevision { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class PublicAssistantTurn
{
    public PublicAssistantMessage Message { get; set; } = new();
    public JsonDocument Draft { get; set; } = JsonDocument.Parse("{\"items\":[]}");
    public int DraftRevision { get; set; }
    public List<PublicAssistantCatalogCard> CatalogProducts { get; set; } = [];
    public string ActionType { get; set; } = "None";
    public string? Summary { get; set; }
    public string? ConfirmationToken { get; set; }
}

public sealed class PublicAssistantCatalogCard
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ProductPageUrl { get; set; }
    public string? Modality { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public bool? Available { get; set; }
    public List<string> AvailableSizes { get; set; } = [];
    public List<string> AvailableColors { get; set; } = [];
}

public sealed class PublicAssistantSubmitRequest
{
    public string ConfirmationToken { get; set; } = string.Empty;
    public int ExpectedRevision { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerWhatsapp { get; set; } = string.Empty;
    public bool AcceptedPrivacyPolicy { get; set; }
}

public sealed class PublicAssistantSubmission
{
    public int CustomerProductRequestId { get; set; }
    public bool Received { get; set; }
}

public enum PublicAssistantFailureKind { None, NotFound, Conflict, Expired, Validation, RateLimited, Unavailable, Timeout, Malformed }

public sealed record PublicAssistantResult<T>(PublicAssistantFailureKind Failure, T? Value, string? Message = null)
{
    public bool IsSuccess => Failure == PublicAssistantFailureKind.None && Value is not null;
}

public sealed record PublicAssistantActionResult(PublicAssistantFailureKind Failure, string? Message = null)
{
    public bool IsSuccess => Failure == PublicAssistantFailureKind.None;
}
