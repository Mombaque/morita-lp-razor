namespace Morita.LP.Razor.Models;

public sealed class ProductRating
{
    public decimal Average { get; set; }
    public int Count { get; set; }
}

public sealed class ReviewForm
{
    public string PublicOrderNumber { get; set; } = "";
    public List<ReviewFormItem> Items { get; set; } = [];
}

public sealed class ReviewFormItem
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
    public bool Reviewed { get; set; }
}

public sealed class ProductReview
{
    public string DisplayName { get; set; } = "";
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public string? StaffReply { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ProductReviewPage
{
    public ProductRating Rating { get; set; } = new();
    public List<ProductReview> Items { get; set; } = [];
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

public sealed record ReviewInput(string Slug, int Rating, string? Comment);

public enum ReviewLoadState { Success, NotFound, Unavailable }

public sealed record ReviewFormResult(ReviewLoadState State, ReviewForm? Form);

public enum ReviewSubmitState { Accepted, Invalid, NotFound, Unavailable }

public sealed record ReviewSubmitResult(ReviewSubmitState State, string? Message);

public static class ReviewPresentation
{
    public const int MaximumCommentLength = 1000;

    public static string Stars(decimal rating)
    {
        var full = (int)Math.Round(Math.Clamp(rating, 0, 5), MidpointRounding.AwayFromZero);
        return new string('★', full) + new string('☆', 5 - full);
    }

    public static string Average(decimal average) => average.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    public static string CountLabel(int count) => count == 1 ? "1 avaliação" : $"{count} avaliações";
}
