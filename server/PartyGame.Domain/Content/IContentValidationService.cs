namespace PartyGame.Domain.Content;

public sealed record ValidationError(string Path, string Code, string Message);

public sealed record ValidationResult(bool IsValid, List<ValidationError> Errors)
{
    public static ValidationResult Success() => new(true, []);
    public static ValidationResult Failure(List<ValidationError> errors) => new(false, errors);
    public static ValidationResult SingleError(string path, string code, string message) =>
        new(false, [new ValidationError(path, code, message)]);
}

public interface IContentValidationService
{
    ValidationResult ValidatePackageMetadata(GamePackage package);
    ValidationResult ValidateCategory(GameCategory category, IEnumerable<GameCategory> existingCategories);
    ValidationResult ValidateQuestion(GameQuestion question, IEnumerable<GameQuestion> existingQuestions);
    ValidationResult ValidateForPublish(GamePackage package);
}
