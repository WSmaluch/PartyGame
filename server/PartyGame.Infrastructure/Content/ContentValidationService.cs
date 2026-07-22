using System.Text.RegularExpressions;
using PartyGame.Domain.Content;

namespace PartyGame.Infrastructure.Content;

public sealed class ContentValidationService : IContentValidationService
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex QuestionKeyRegex = new(@"^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public ValidationResult ValidatePackageMetadata(GamePackage package)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(package.NamePl))
            errors.Add(new ValidationError("namePl", "package_name_required", "Nazwa pakietu (PL) jest wymagana."));
        else if (package.NamePl.Length > 120)
            errors.Add(new ValidationError("namePl", "package_name_too_long", "Nazwa pakietu (PL) nie może przekraczać 120 znaków."));

        if (!string.IsNullOrEmpty(package.NameEn) && package.NameEn.Length > 120)
            errors.Add(new ValidationError("nameEn", "package_name_en_too_long", "Nazwa pakietu (EN) nie może przekraczać 120 znaków."));

        if (!string.IsNullOrEmpty(package.DescriptionPl) && package.DescriptionPl.Length > 1000)
            errors.Add(new ValidationError("descriptionPl", "package_description_too_long", "Opis pakietu (PL) nie może przekraczać 1000 znaków."));

        if (!string.IsNullOrEmpty(package.DescriptionEn) && package.DescriptionEn.Length > 1000)
            errors.Add(new ValidationError("descriptionEn", "package_description_en_too_long", "Opis pakietu (EN) nie może przekraczać 1000 znaków."));

        ValidatePlainText("namePl", package.NamePl, errors);
        ValidatePlainText("nameEn", package.NameEn, errors);
        ValidatePlainText("descriptionPl", package.DescriptionPl, errors);
        ValidatePlainText("descriptionEn", package.DescriptionEn, errors);

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    public ValidationResult ValidateCategory(GameCategory category, IEnumerable<GameCategory> existingCategories)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(category.Key))
            errors.Add(new ValidationError("key", "category_key_required", "Klucz kategorii jest wymagany."));
        else if (category.Key.Length > 100)
            errors.Add(new ValidationError("key", "category_key_too_long", "Klucz kategorii nie może przekraczać 100 znaków."));

        if (string.IsNullOrWhiteSpace(category.NamePl))
            errors.Add(new ValidationError("namePl", "category_name_required", "Nazwa kategorii (PL) jest wymagana."));
        else if (category.NamePl.Length > 120)
            errors.Add(new ValidationError("namePl", "category_name_too_long", "Nazwa kategorii (PL) nie może przekraczać 120 znaków."));

        if (string.IsNullOrWhiteSpace(category.NameEn))
            errors.Add(new ValidationError("nameEn", "category_name_en_required", "Nazwa kategorii (EN) jest wymagana."));
        else if (category.NameEn.Length > 120)
            errors.Add(new ValidationError("nameEn", "category_name_en_too_long", "Nazwa kategorii (EN) nie może przekraczać 120 znaków."));

        if (category.SortOrder < 0)
            errors.Add(new ValidationError("sortOrder", "category_sort_order_invalid", "Kolejność kategorii nie może być ujemna."));

        if (!string.IsNullOrEmpty(category.DescriptionPl) && category.DescriptionPl.Length > 1000)
            errors.Add(new ValidationError("descriptionPl", "category_description_too_long", "Opis kategorii (PL) nie może przekraczać 1000 znaków."));

        if (!string.IsNullOrEmpty(category.DescriptionEn) && category.DescriptionEn.Length > 1000)
            errors.Add(new ValidationError("descriptionEn", "category_description_en_too_long", "Opis kategorii (EN) nie może przekraczać 1000 znaków."));

        ValidatePlainText("namePl", category.NamePl, errors);
        ValidatePlainText("nameEn", category.NameEn, errors);
        ValidatePlainText("descriptionPl", category.DescriptionPl, errors);
        ValidatePlainText("descriptionEn", category.DescriptionEn, errors);

        if (!string.IsNullOrEmpty(category.Key) && existingCategories.Any(c => c.Id != category.Id && c.Key.Equals(category.Key, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new ValidationError("key", "category_key_conflict", $"Klucz kategorii '{category.Key}' już istnieje w tym pakiecie."));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    public ValidationResult ValidateQuestion(GameQuestion question, IEnumerable<GameQuestion> existingQuestions)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(question.Key))
            errors.Add(new ValidationError("key", "question_key_required", "Klucz pytania jest wymagany."));
        else if (question.Key.Length > 100)
            errors.Add(new ValidationError("key", "question_key_too_long", "Klucz pytania nie może przekraczać 100 znaków."));
        else if (!QuestionKeyRegex.IsMatch(question.Key))
            errors.Add(new ValidationError("key", "question_key_invalid_format", "Klucz pytania musi mieć postać lowercase_snake_case."));

        if (string.IsNullOrWhiteSpace(question.TextPl))
            errors.Add(new ValidationError("textPl", "question_text_required", "Treść pytania (PL) jest wymagana."));
        else if (question.TextPl.Length > 500)
            errors.Add(new ValidationError("textPl", "question_text_too_long", "Treść pytania (PL) nie może przekraczać 500 znaków."));

        if (string.IsNullOrWhiteSpace(question.TextEn))
            errors.Add(new ValidationError("textEn", "question_text_en_required", "Treść pytania (EN) jest wymagana."));
        else if (question.TextEn.Length > 500)
            errors.Add(new ValidationError("textEn", "question_text_en_too_long", "Treść pytania (EN) nie może przekraczać 500 znaków."));

        if (question.MinimumPlayers < 3)
            errors.Add(new ValidationError("minimumPlayers", "invalid_minimum_players", "Minimalna liczba graczy musi wynosić co najmniej 3."));
        if (question.SortOrder < 0)
            errors.Add(new ValidationError("sortOrder", "question_sort_order_invalid", "Kolejność pytania nie może być ujemna."));

        ValidatePlainText("textPl", question.TextPl, errors);
        ValidatePlainText("textEn", question.TextEn, errors);

        ValidatePlaceholders("textPl", question.TextPl, question.Type, errors);
        ValidatePlaceholders("textEn", question.TextEn, question.Type, errors);

        if (!string.IsNullOrEmpty(question.Key) && existingQuestions.Any(q => q.Id != question.Id && q.Key.Equals(question.Key, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new ValidationError("key", "question_key_conflict", $"Klucz pytania '{question.Key}' już istnieje w tym pakiecie."));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    public ValidationResult ValidateForPublish(GamePackage package)
    {
        var errors = new List<ValidationError>();

        if (package.Status != ContentPackageStatus.Draft)
        {
            errors.Add(new ValidationError("status", "content_package_already_published", "Tylko robocza wersja pakietu (Draft) może zostać opublikowana."));
            return ValidationResult.Failure(errors);
        }

        var metadataResult = ValidatePackageMetadata(package);
        if (!metadataResult.IsValid)
            errors.AddRange(metadataResult.Errors);

        var activeCategories = package.Categories.Where(c => c.IsActive).ToList();
        if (activeCategories.Count == 0)
        {
            errors.Add(new ValidationError("categories", "package_requires_active_category", "Opublikowany pakiet musi posiadać co najmniej jedną aktywną kategorię."));
        }

        var allQuestions = package.Categories.SelectMany(c => c.Questions).ToList();
        var categoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var questionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in package.Categories)
        {
            if (!categoryKeys.Add(cat.Key))
            {
                errors.Add(new ValidationError($"categories[{cat.Key}]", "category_key_conflict", $"Zauważono zduplikowany klucz kategorii: '{cat.Key}'."));
            }

            var catResult = ValidateCategory(cat, package.Categories);
            if (!catResult.IsValid) errors.AddRange(catResult.Errors);

            if (cat.IsActive)
            {
                var activeQuestions = cat.Questions.Where(q => q.IsActive).ToList();
                if (activeQuestions.Count == 0)
                {
                    errors.Add(new ValidationError($"categories[{cat.Key}].questions", "category_requires_question", $"Aktywna kategoria '{cat.NamePl}' musi zawierać co najmniej jedno aktywne pytanie."));
                }
            }

            foreach (var q in cat.Questions)
            {
                if (!questionKeys.Add(q.Key))
                {
                    errors.Add(new ValidationError($"questions[{q.Key}]", "question_key_conflict", $"Zauważono zduplikowany klucz pytania: '{q.Key}'."));
                }

                var qResult = ValidateQuestion(q, allQuestions);
                if (!qResult.IsValid) errors.AddRange(qResult.Errors);
            }
        }

        // Check overall sufficient active content
        var totalActiveQuestions = activeCategories.SelectMany(c => c.Questions).Where(q => q.IsActive).ToList();
        if (totalActiveQuestions.Count == 0)
        {
            errors.Add(new ValidationError("questions", "content_insufficient_questions", "Pakiet nie zawiera aktywnych pytań."));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    private static void ValidatePlainText(string fieldName, string? text, List<ValidationError> errors)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Check HTML / Script tags
        if (HtmlTagRegex.IsMatch(text))
        {
            errors.Add(new ValidationError(fieldName, "text_contains_html", $"Pole '{fieldName}' zawiera zabronione znaki lub tagi HTML. Treść musi być w czystym tekście (plain text)."));
        }

        // Control characters check except \r, \n, \t
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
            {
                errors.Add(new ValidationError(fieldName, "text_contains_control_chars", $"Pole '{fieldName}' zawiera zabronione znaki sterujące."));
                break;
            }
        }
    }

    private static void ValidatePlaceholders(string fieldName, string? text, QuestionType type, List<ValidationError> errors)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (text.Contains("{}", StringComparison.Ordinal) || text.Count(ch => ch == '{') != text.Count(ch => ch == '}'))
        {
            errors.Add(new ValidationError(fieldName, "content_invalid_placeholder", "Placeholder musi mieć postać {player}."));
            return;
        }

        var matches = PlaceholderRegex.Matches(text);
        foreach (Match match in matches)
        {
            var placeholder = match.Groups[1].Value.Trim();
            if (type == QuestionType.PlayerSelection)
            {
                if (!placeholder.Equals("player", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(placeholder, @"^player:\d+$", RegexOptions.IgnoreCase))
                {
                    errors.Add(new ValidationError(fieldName, "content_invalid_placeholder", $"Nieznany placeholder {{{placeholder}}} w pytaniu typu PlayerSelection. Dozwolone: {{player}}."));
                }
            }
            else
            {
                // Non-PlayerSelection types should not require mandatory placeholders, but if present validate known ones
                if (!placeholder.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ValidationError(fieldName, "content_invalid_placeholder", $"Nieznany placeholder {{{placeholder}}}."));
                }
            }
        }
    }
}
