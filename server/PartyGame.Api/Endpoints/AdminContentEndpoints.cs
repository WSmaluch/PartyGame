using Microsoft.EntityFrameworkCore;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Content;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Endpoints;

public static class AdminContentEndpoints
{
    public static IEndpointRouteBuilder MapAdminContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/content-packages").WithTags("Admin Content");

        // --- Packages ---
        admin.MapGet("", async (PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var packages = await dbContext.GamePackages
                .Include(p => p.Categories)
                    .ThenInclude(c => c.Questions)
                .OrderBy(p => p.LogicalPackageId)
                .ThenByDescending(p => p.Version)
                .ToListAsync(cancellationToken);

            var result = packages.Select(p => ToPackageResponse(p)).ToList();
            return Results.Ok(result);
        });

        admin.MapGet("/{packageVersionId:guid}", async (Guid packageVersionId, PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages
                .Include(p => p.Categories)
                    .ThenInclude(c => c.Questions)
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

            if (package is null)
                return Results.Json(new { code = "content_package_not_found", message = "Package version not found." }, statusCode: 404);

            return Results.Ok(ToPackageDetailResponse(package));
        });

        admin.MapPost("", async (CreatePackageRequest request, PartyGameDbContext dbContext, IContentValidationService validator, IGameClock clock, CancellationToken cancellationToken) =>
        {
            var now = clock.UtcNow;
            var key = string.IsNullOrWhiteSpace(request.Key) ? "pack_" + Guid.NewGuid().ToString("N")[..8] : request.Key.Trim();

            var package = new GamePackage
            {
                Id = Guid.NewGuid(),
                LogicalPackageId = Guid.NewGuid(),
                Version = 1,
                Key = key,
                NamePl = request.NamePl?.Trim() ?? "",
                NameEn = request.NameEn?.Trim() ?? "",
                DescriptionPl = request.DescriptionPl?.Trim() ?? "",
                DescriptionEn = request.DescriptionEn?.Trim() ?? "",
                Status = ContentPackageStatus.Draft,
                IsActive = true,
                IsDefault = false,
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };

            var valResult = validator.ValidatePackageMetadata(package);
            if (!valResult.IsValid)
                return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

            dbContext.GamePackages.Add(package);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/admin/content-packages/{package.Id}", ToPackageResponse(package));
        });

        admin.MapPost("/{packageVersionId:guid}/create-draft", async (Guid packageVersionId, PartyGameDbContext dbContext, IGameClock clock, ContentPackageLockProvider packageLocks, CancellationToken cancellationToken) =>
        {
            var sourcePackage = await dbContext.GamePackages
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

            if (sourcePackage is null)
                return Results.Json(new { code = "content_package_not_found", message = "Source package not found." }, statusCode: 404);

            var packageLock = packageLocks.ForLogicalPackage(sourcePackage.LogicalPackageId);
            await packageLock.WaitAsync(cancellationToken);
            try
            {
                sourcePackage = await dbContext.GamePackages
                    .Include(p => p.Categories)
                        .ThenInclude(c => c.Questions)
                    .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

                if (sourcePackage is null)
                    return Results.Json(new { code = "content_package_not_found", message = "Source package not found." }, statusCode: 404);

                if (sourcePackage.Status == ContentPackageStatus.Draft)
                    return Results.Json(new { code = "content_package_already_has_draft", message = "Nie można tworzyć Draftu z wersji roboczej." }, statusCode: 409);

                // Single active draft per logical family rule. The filtered unique index is
                // the durable cross-process guard; this lock avoids needless local races.
                var existingDraft = await dbContext.GamePackages
                    .AnyAsync(p => p.LogicalPackageId == sourcePackage.LogicalPackageId && p.Status == ContentPackageStatus.Draft, cancellationToken);

                if (existingDraft)
                    return Results.Json(new { code = "content_package_already_has_draft", message = "Dla tej rodziny pakietów istnieje już aktywna wersja robocza (Draft)." }, statusCode: 409);

                var maxVersion = await dbContext.GamePackages
                    .Where(p => p.LogicalPackageId == sourcePackage.LogicalPackageId)
                    .MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0;

                var now = clock.UtcNow;
                var newPackageId = Guid.NewGuid();
                var draftPackage = new GamePackage
                {
                    Id = newPackageId,
                    LogicalPackageId = sourcePackage.LogicalPackageId,
                    Version = maxVersion + 1,
                    Key = sourcePackage.Key,
                    NamePl = sourcePackage.NamePl,
                    NameEn = sourcePackage.NameEn,
                    DescriptionPl = sourcePackage.DescriptionPl,
                    DescriptionEn = sourcePackage.DescriptionEn,
                    Status = ContentPackageStatus.Draft,
                    IsActive = true,
                    IsDefault = sourcePackage.IsDefault,
                    SortOrder = sourcePackage.SortOrder,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                };

                // Deep copy categories and questions
                foreach (var cat in sourcePackage.Categories.OrderBy(c => c.SortOrder))
                {
                    var newCatId = Guid.NewGuid();
                    var newCat = new GameCategory
                    {
                        Id = newCatId,
                        PackageId = newPackageId,
                        Key = cat.Key,
                        NamePl = cat.NamePl,
                        NameEn = cat.NameEn,
                        DescriptionPl = cat.DescriptionPl,
                        DescriptionEn = cat.DescriptionEn,
                        IsActive = cat.IsActive,
                        SortOrder = cat.SortOrder,
                        ConcurrencyToken = Guid.NewGuid().ToString("N"),
                        Package = draftPackage
                    };

                    foreach (var q in cat.Questions.OrderBy(q => q.SortOrder))
                    {
                        var newQ = new GameQuestion
                        {
                            Id = Guid.NewGuid(),
                            CategoryId = newCatId,
                            Key = q.Key,
                            Type = q.Type,
                            TextPl = q.TextPl,
                            TextEn = q.TextEn,
                            IsActive = q.IsActive,
                            MinimumPlayers = q.MinimumPlayers,
                            SortOrder = q.SortOrder,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now,
                            ConcurrencyToken = Guid.NewGuid().ToString("N"),
                            Category = newCat
                        };
                        newCat.Questions.Add(newQ);
                    }
                    draftPackage.Categories.Add(newCat);
                }

                dbContext.GamePackages.Add(draftPackage);
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // A second API process can win after our local pre-check. Re-query to
                    // distinguish the documented single-Draft conflict from other faults.
                    var draftExists = await dbContext.GamePackages.AsNoTracking().AnyAsync(
                        p => p.LogicalPackageId == sourcePackage.LogicalPackageId && p.Status == ContentPackageStatus.Draft,
                        cancellationToken);
                    if (draftExists)
                        return Results.Json(new { code = "content_package_already_has_draft", message = "Dla tej rodziny pakietów istnieje już aktywna wersja robocza (Draft)." }, statusCode: 409);
                    return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409);
                }

                return Results.Created($"/api/admin/content-packages/{draftPackage.Id}", ToPackageResponse(draftPackage));
            }
            finally
            {
                packageLock.Release();
            }
        });

        admin.MapPatch("/{packageVersionId:guid}", async (Guid packageVersionId, UpdatePackageRequest request, PartyGameDbContext dbContext, IContentValidationService validator, IGameClock clock, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null)
                return Results.Json(new { code = "content_package_not_found", message = "Package not found." }, statusCode: 404);

            if (package.Status != ContentPackageStatus.Draft)
                return Results.Json(new { code = "content_package_not_editable", message = "Tylko wersja robocza (Draft) może być edytowana." }, statusCode: 400);

            if (!string.IsNullOrEmpty(request.ConcurrencyToken) && package.ConcurrencyToken != request.ConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Ta treść została zmieniona w innej sesji. Odśwież dane przed ponownym zapisem." }, statusCode: 409);

            package.NamePl = request.NamePl?.Trim() ?? package.NamePl;
            package.NameEn = request.NameEn?.Trim() ?? package.NameEn;
            package.DescriptionPl = request.DescriptionPl?.Trim() ?? package.DescriptionPl;
            package.DescriptionEn = request.DescriptionEn?.Trim() ?? package.DescriptionEn;
            if (request.IsActive.HasValue) package.IsActive = request.IsActive.Value;

            package.UpdatedAtUtc = clock.UtcNow;
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");

            var valResult = validator.ValidatePackageMetadata(package);
            if (!valResult.IsValid)
                return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409); }
            return Results.Ok(ToPackageResponse(package));
        });

        admin.MapPost("/{packageVersionId:guid}/publish", async (Guid packageVersionId, PublishPackageRequest request, PartyGameDbContext dbContext, IContentValidationService validator, IGameClock clock, ContentPackageLockProvider packageLocks, CancellationToken cancellationToken) =>
        {
            var packageLock = packageLocks.ForVersion(packageVersionId);
            await packageLock.WaitAsync(cancellationToken);
            try
            {
                var package = await dbContext.GamePackages
                    .Include(p => p.Categories)
                        .ThenInclude(c => c.Questions)
                    .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

                if (package is null)
                    return Results.Json(new { code = "content_package_not_found", message = "Package not found." }, statusCode: 404);

                if (package.Status != ContentPackageStatus.Draft)
                    return Results.Json(new { code = "content_package_already_published", message = "Pakiet został już opublikowany lub zarchiwizowany." }, statusCode: 400);

                if (!string.IsNullOrEmpty(request.ConcurrencyToken) && package.ConcurrencyToken != request.ConcurrencyToken)
                    return Results.Json(new { code = "content_concurrency_conflict", message = "Ta treść została zmieniona w innej sesji. Odśwież dane przed ponownym zapisem." }, statusCode: 409);

                var valResult = validator.ValidateForPublish(package);
                if (!valResult.IsValid)
                    return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

                var now = clock.UtcNow;
                package.Status = ContentPackageStatus.Published;
                package.PublishedAtUtc = now;
                package.UpdatedAtUtc = now;
                package.ConcurrencyToken = Guid.NewGuid().ToString("N");

                try { await dbContext.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409); }
                return Results.Ok(ToPackageResponse(package));
            }
            finally { packageLock.Release(); }
        });

        admin.MapPost("/{packageVersionId:guid}/archive", async (Guid packageVersionId, ArchivePackageRequest request, PartyGameDbContext dbContext, IGameClock clock, ContentPackageLockProvider packageLocks, CancellationToken cancellationToken) =>
        {
            var packageLock = packageLocks.ForVersion(packageVersionId);
            await packageLock.WaitAsync(cancellationToken);
            try
            {
                var package = await dbContext.GamePackages.FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
                if (package is null)
                    return Results.Json(new { code = "content_package_not_found", message = "Package not found." }, statusCode: 404);

                if (package.Status != ContentPackageStatus.Published)
                    return Results.Json(new { code = "content_package_not_archivable", message = "Tylko opublikowana wersja pakietu może zostać zarchiwizowana." }, statusCode: 400);

                if (!string.IsNullOrEmpty(request.ConcurrencyToken) && package.ConcurrencyToken != request.ConcurrencyToken)
                    return Results.Json(new { code = "content_concurrency_conflict", message = "Ta treść została zmieniona w innej sesji. Odśwież dane przed ponownym zapisem." }, statusCode: 409);

                var now = clock.UtcNow;
                package.Status = ContentPackageStatus.Archived;
                package.ArchivedAtUtc = now;
                package.UpdatedAtUtc = now;
                package.ConcurrencyToken = Guid.NewGuid().ToString("N");

                try { await dbContext.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409); }
                return Results.Ok(ToPackageResponse(package));
            }
            finally { packageLock.Release(); }
        });

        // --- Categories ---
        admin.MapGet("/{packageVersionId:guid}/categories", async (Guid packageVersionId, PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).ThenInclude(c => c.Questions)
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null) return Results.Json(new { code = "content_package_not_found" }, statusCode: 404);
            return Results.Ok(new { items = package.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(ToCategoryResponse), packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapPost("/{packageVersionId:guid}/categories", async (Guid packageVersionId, CreateCategoryRequest request, PartyGameDbContext dbContext, IContentValidationService validator, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null) return Results.Json(new { code = "content_package_not_found" }, statusCode: 404);
            if (package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);
            if (string.IsNullOrEmpty(request.PackageConcurrencyToken) || package.ConcurrencyToken != request.PackageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409);

            var key = string.IsNullOrWhiteSpace(request.Key) ? "cat_" + Guid.NewGuid().ToString("N")[..8] : request.Key.Trim();
            var category = new GameCategory
            {
                Id = Guid.NewGuid(),
                PackageId = packageVersionId,
                Key = key,
                NamePl = request.NamePl?.Trim() ?? "",
                NameEn = request.NameEn?.Trim() ?? "",
                DescriptionPl = request.DescriptionPl?.Trim() ?? "",
                DescriptionEn = request.DescriptionEn?.Trim() ?? "",
                IsActive = request.IsActive,
                SortOrder = request.SortOrder ?? (package.Categories.Count > 0 ? package.Categories.Max(c => c.SortOrder) + 1 : 0),
                ConcurrencyToken = Guid.NewGuid().ToString("N"),
                Package = package
            };

            var valResult = validator.ValidateCategory(category, package.Categories);
            if (!valResult.IsValid) return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

            dbContext.GameCategories.Add(category);
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/admin/content-packages/{packageVersionId}/categories/{category.Id}", new { category = ToCategoryResponse(category), packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapPatch("/{packageVersionId:guid}/categories/{categoryId:guid}", async (Guid packageVersionId, Guid categoryId, UpdateCategoryRequest request, PartyGameDbContext dbContext, IContentValidationService validator, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            var category = package.Categories.FirstOrDefault(c => c.Id == categoryId);
            if (category is null) return Results.Json(new { code = "content_category_not_found" }, statusCode: 404);

            if (string.IsNullOrEmpty(request.ConcurrencyToken) || category.ConcurrencyToken != request.ConcurrencyToken || string.IsNullOrEmpty(request.PackageConcurrencyToken) || package.ConcurrencyToken != request.PackageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Ta kategoria została zmieniona w innej sesji." }, statusCode: 409);

            category.Key = request.Key?.Trim() ?? category.Key;
            category.NamePl = request.NamePl?.Trim() ?? category.NamePl;
            category.NameEn = request.NameEn?.Trim() ?? category.NameEn;
            category.DescriptionPl = request.DescriptionPl?.Trim() ?? category.DescriptionPl;
            category.DescriptionEn = request.DescriptionEn?.Trim() ?? category.DescriptionEn;
            if (request.IsActive.HasValue) category.IsActive = request.IsActive.Value;
            if (request.SortOrder.HasValue) category.SortOrder = request.SortOrder.Value;
            category.ConcurrencyToken = Guid.NewGuid().ToString("N");
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");

            var valResult = validator.ValidateCategory(category, package.Categories);
            if (!valResult.IsValid) return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Ta kategoria została zmieniona w innej sesji." }, statusCode: 409); }
            return Results.Ok(new { category = ToCategoryResponse(category), packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapDelete("/{packageVersionId:guid}/categories/{categoryId:guid}", async (Guid packageVersionId, Guid categoryId, string? mode, Guid? targetCategoryId, string? concurrencyToken, string? packageConcurrencyToken, PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages
                .Include(p => p.Categories)
                    .ThenInclude(c => c.Questions)
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            var category = package.Categories.FirstOrDefault(c => c.Id == categoryId);
            if (category is null) return Results.Json(new { code = "content_category_not_found" }, statusCode: 404);

            if (string.IsNullOrEmpty(concurrencyToken) || category.ConcurrencyToken != concurrencyToken || string.IsNullOrEmpty(packageConcurrencyToken) || package.ConcurrencyToken != packageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Kategoria zmieniona w innej sesji." }, statusCode: 409);

            var deleteMode = (mode ?? "reject").ToLowerInvariant();
            if (category.Questions.Count > 0)
            {
                if (deleteMode == "reject")
                {
                    return Results.Json(new
                    {
                        code = "content_category_has_questions",
                        message = $"Kategoria zawiera {category.Questions.Count} pytań. Przenieś je lub użyj trybu usuwania pytań.",
                        questionCount = category.Questions.Count
                    }, statusCode: 409);
                }
                else if (deleteMode == "movequestions")
                {
                    if (!targetCategoryId.HasValue)
                        return Results.Json(new { code = "target_category_required", message = "Nie podano docelowej kategorii." }, statusCode: 400);

                    var targetCategory = package.Categories.FirstOrDefault(c => c.Id == targetCategoryId.Value);
                    if (targetCategory is null || targetCategory.Id == categoryId)
                        return Results.Json(new { code = "invalid_target_category", message = "Nieprawidłowa docelowa kategoria w tym pakiecie." }, statusCode: 400);

                    var nextOrder = targetCategory.Questions.Count == 0 ? 0 : targetCategory.Questions.Max(q => q.SortOrder) + 1;
                    foreach (var q in category.Questions.OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToList())
                    {
                        q.CategoryId = targetCategory.Id;
                        q.Category = targetCategory;
                        q.SortOrder = nextOrder++;
                        q.ConcurrencyToken = Guid.NewGuid().ToString("N");
                        targetCategory.Questions.Add(q);
                    }
                }
                else if (deleteMode == "deletequestions")
                {
                    dbContext.GameQuestions.RemoveRange(category.Questions);
                }
                else if (deleteMode != "reject")
                {
                    return Results.Json(new { code = "content_validation_failed", message = "Nieznany tryb usuwania kategorii." }, statusCode: 400);
                }
            }

            dbContext.GameCategories.Remove(category);
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Kategoria została zmieniona w innej sesji." }, statusCode: 409); }

            return Results.Ok(new { success = true, packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapPost("/{packageVersionId:guid}/categories/reorder", async (Guid packageVersionId, ReorderRequest request, PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            if (string.IsNullOrEmpty(request.PackageConcurrencyToken) || package.ConcurrencyToken != request.PackageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet zmieniony w innej sesji." }, statusCode: 409);

            if (request.Items.Count != package.Categories.Count || request.Items.Select(i => i.Id).Distinct().Count() != request.Items.Count || request.Items.Select(i => i.SortOrder).Distinct().Count() != request.Items.Count || request.Items.Any(i => i.SortOrder < 0 || !package.Categories.Any(c => c.Id == i.Id)))
                return Results.Json(new { code = "content_invalid_reorder" }, statusCode: 400);
            foreach (var item in request.Items)
            {
                var cat = package.Categories.FirstOrDefault(c => c.Id == item.Id);
                if (cat != null)
                {
                    cat.SortOrder = item.SortOrder;
                    cat.ConcurrencyToken = Guid.NewGuid().ToString("N");
                }
            }

            package.ConcurrencyToken = Guid.NewGuid().ToString("N");
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409); }
            return Results.Ok(new { items = package.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(ToCategoryResponse), packageConcurrencyToken = package.ConcurrencyToken });
        });

        // --- Questions ---
        admin.MapGet("/{packageVersionId:guid}/questions/{questionId:guid}", async (Guid packageVersionId, Guid questionId, PartyGameDbContext dbContext, IContentValidationService validator, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null) return Results.Json(new { code = "content_package_not_found" }, statusCode: 404);
            var question = await dbContext.GameQuestions.Include(q => q.Category).FirstOrDefaultAsync(q => q.Id == questionId && q.Category.PackageId == packageVersionId, cancellationToken);
            if (question is null) return Results.Json(new { code = "content_question_not_found" }, statusCode: 404);
            var allQuestions = await dbContext.GameQuestions.Include(q => q.Category).Where(q => q.Category.PackageId == packageVersionId).ToListAsync(cancellationToken);
            var errors = validator.ValidateQuestion(question, allQuestions).Errors.Select(error => new { error.Path, error.Code, error.Message });
            return Results.Ok(new { question = ToQuestionResponse(question, errors), packageConcurrencyToken = package.ConcurrencyToken, packageStatus = package.Status.ToString() });
        });

        admin.MapGet("/{packageVersionId:guid}/questions", async (Guid packageVersionId, Guid? categoryId, QuestionType? questionType, bool? isEnabled, bool? missingTranslation, bool? validationErrors, string? search, int? page, int? pageSize, string? sort, PartyGameDbContext dbContext, IContentValidationService validator, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null) return Results.Json(new { code = "content_package_not_found" }, statusCode: 404);

            if (page is < 1 || pageSize is < 1 or > 100)
                return Results.Json(new { code = "content_invalid_pagination", message = "page musi być >= 1, a pageSize w zakresie 1–100." }, statusCode: 400);
            if (categoryId.HasValue && !await dbContext.GameCategories.AnyAsync(c => c.Id == categoryId.Value && c.PackageId == packageVersionId, cancellationToken))
                return Results.Json(new { code = "content_category_not_found", message = "Kategoria nie należy do wskazanego pakietu." }, statusCode: 400);

            var query = dbContext.GameQuestions.Include(q => q.Category).Where(q => q.Category.PackageId == packageVersionId);

            if (categoryId.HasValue) query = query.Where(q => q.CategoryId == categoryId.Value);
            if (questionType.HasValue) query = query.Where(q => q.Type == questionType.Value);
            if (isEnabled.HasValue) query = query.Where(q => q.IsActive == isEnabled.Value);
            if (missingTranslation == true) query = query.Where(q => string.IsNullOrWhiteSpace(q.TextPl) || string.IsNullOrWhiteSpace(q.TextEn));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(q => EF.Functions.Like(q.TextPl, $"%{s}%") || EF.Functions.Like(q.TextEn, $"%{s}%") || EF.Functions.Like(q.Key, $"%{s}%"));
            }

            var sortOrder = (sort ?? "sortOrderAsc").ToLowerInvariant();
            query = sortOrder switch
            {
                "sortorder" or "sortorderasc" => query.OrderBy(q => q.Category.SortOrder).ThenBy(q => q.SortOrder).ThenBy(q => q.Id),
                "sortorderdesc" => query.OrderByDescending(q => q.Category.SortOrder).ThenByDescending(q => q.SortOrder).ThenBy(q => q.Id),
                "updatedatutc" or "updateddesc" => query.OrderByDescending(q => q.UpdatedAtUtc).ThenBy(q => q.Id),
                "updatedasc" => query.OrderBy(q => q.UpdatedAtUtc).ThenBy(q => q.Id),
                "key" or "keyasc" => query.OrderBy(q => q.Key).ThenBy(q => q.Id),
                "keydesc" => query.OrderByDescending(q => q.Key).ThenBy(q => q.Id),
                "typeasc" => query.OrderBy(q => q.Type).ThenBy(q => q.Id),
                _ => null!
            };

            if (query is null) return Results.Json(new { code = "content_invalid_sort", message = "Nieznany sposób sortowania." }, statusCode: 400);
            var allPackageQuestions = await dbContext.GameQuestions.Include(q => q.Category).Where(q => q.Category.PackageId == packageVersionId).ToListAsync(cancellationToken);
            var filtered = await query.ToListAsync(cancellationToken);
            var errorsById = allPackageQuestions.ToDictionary(q => q.Id, q => validator.ValidateQuestion(q, allPackageQuestions).Errors.Select(error => new { error.Path, error.Code, error.Message }).ToList());
            if (validationErrors == true) filtered = filtered.Where(q => errorsById[q.Id].Count > 0).ToList();

            var currentPage = page ?? 1;
            var currentPageSize = pageSize ?? 25;
            var totalItems = filtered.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)currentPageSize);
            var items = filtered.Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToList();

            return Results.Ok(new
            {
                items = items.Select(q => ToQuestionResponse(q, errorsById[q.Id])).ToList(),
                page = currentPage,
                pageSize = currentPageSize,
                totalItems,
                totalPages,
                packageConcurrencyToken = package.ConcurrencyToken
            });
        });

        admin.MapPost("/{packageVersionId:guid}/questions", async (Guid packageVersionId, CreateQuestionRequest request, PartyGameDbContext dbContext, IContentValidationService validator, IGameClock clock, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages
                .Include(p => p.Categories)
                    .ThenInclude(c => c.Questions)
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            var category = package.Categories.FirstOrDefault(c => c.Id == request.CategoryId);
            if (category is null) return Results.Json(new { code = "content_category_not_found" }, statusCode: 404);

            var key = string.IsNullOrWhiteSpace(request.Key) ? "q_" + Guid.NewGuid().ToString("N")[..8] : request.Key.Trim();
            var now = clock.UtcNow;

            var question = new GameQuestion
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                Key = key,
                Type = request.Type,
                TextPl = request.TextPl?.Trim() ?? "",
                TextEn = request.TextEn?.Trim() ?? "",
                IsActive = request.IsActive,
                MinimumPlayers = Math.Max(3, request.MinimumPlayers),
                SortOrder = request.SortOrder ?? (category.Questions.Count > 0 ? category.Questions.Max(q => q.SortOrder) + 1 : 0),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N"),
                Category = category
            };

            var allQuestions = package.Categories.SelectMany(c => c.Questions).ToList();
            var valResult = validator.ValidateQuestion(question, allQuestions);
            if (!valResult.IsValid) return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

            dbContext.GameQuestions.Add(question);
            category.ConcurrencyToken = Guid.NewGuid().ToString("N");
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/admin/content-packages/{packageVersionId}/questions/{question.Id}", ToQuestionResponse(question));
        });

        admin.MapPatch("/{packageVersionId:guid}/questions/{questionId:guid}", async (Guid packageVersionId, Guid questionId, UpdateQuestionRequest request, PartyGameDbContext dbContext, IContentValidationService validator, IGameClock clock, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages
                .Include(p => p.Categories)
                    .ThenInclude(c => c.Questions)
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);

            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            var allQuestions = package.Categories.SelectMany(c => c.Questions).ToList();
            var question = allQuestions.FirstOrDefault(q => q.Id == questionId);
            if (question is null) return Results.Json(new { code = "content_question_not_found" }, statusCode: 404);

            if (!string.IsNullOrEmpty(request.ConcurrencyToken) && question.ConcurrencyToken != request.ConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pytanie zmienione w innej sesji." }, statusCode: 409);
            if (!string.IsNullOrEmpty(request.PackageConcurrencyToken) && package.ConcurrencyToken != request.PackageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409);

            if (request.CategoryId.HasValue && request.CategoryId.Value != question.CategoryId)
            {
                var newCat = package.Categories.FirstOrDefault(c => c.Id == request.CategoryId.Value);
                if (newCat is null) return Results.Json(new { code = "content_category_not_found" }, statusCode: 404);
                question.CategoryId = newCat.Id;
                question.Category = newCat;
            }

            if (request.Type.HasValue) question.Type = request.Type.Value;
            question.TextPl = request.TextPl?.Trim() ?? question.TextPl;
            question.TextEn = request.TextEn?.Trim() ?? question.TextEn;
            if (request.IsActive.HasValue) question.IsActive = request.IsActive.Value;
            if (request.MinimumPlayers.HasValue) question.MinimumPlayers = Math.Max(3, request.MinimumPlayers.Value);
            if (request.SortOrder.HasValue) question.SortOrder = request.SortOrder.Value;

            question.UpdatedAtUtc = clock.UtcNow;
            question.ConcurrencyToken = Guid.NewGuid().ToString("N");
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");

            var valResult = validator.ValidateQuestion(question, allQuestions);
            if (!valResult.IsValid) return Results.Json(new { code = "content_package_validation_failed", errors = valResult.Errors }, statusCode: 400);

            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pytanie zmienione w innej sesji." }, statusCode: 409); }
            return Results.Ok(new { question = ToQuestionResponse(question), packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapDelete("/{packageVersionId:guid}/questions/{questionId:guid}", async (Guid packageVersionId, Guid questionId, string? concurrencyToken, string? packageConcurrencyToken, PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).ThenInclude(c => c.Questions).FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            var question = dbContext.GameQuestions.FirstOrDefault(q => q.Id == questionId);
            if (question is null) return Results.Json(new { code = "content_question_not_found" }, statusCode: 404);

            if (!string.IsNullOrEmpty(concurrencyToken) && question.ConcurrencyToken != concurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pytanie zmienione w innej sesji." }, statusCode: 409);
            if (!string.IsNullOrEmpty(packageConcurrencyToken) && package.ConcurrencyToken != packageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409);

            dbContext.GameQuestions.Remove(question);
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pytanie zostało zmienione w innej sesji." }, statusCode: 409); }
            return Results.Ok(new { success = true, packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapPost("/{packageVersionId:guid}/questions/{questionId:guid}/duplicate", async (Guid packageVersionId, Guid questionId, QuestionMutationRequest? request, PartyGameDbContext dbContext, IGameClock clock, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).ThenInclude(c => c.Questions).FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            var allQuestions = package.Categories.SelectMany(c => c.Questions).ToList();
            var orig = allQuestions.FirstOrDefault(q => q.Id == questionId);
            if (orig is null) return Results.Json(new { code = "content_question_not_found" }, statusCode: 404);
            if (!string.IsNullOrEmpty(request?.ConcurrencyToken) && orig.ConcurrencyToken != request.ConcurrencyToken || !string.IsNullOrEmpty(request?.PackageConcurrencyToken) && package.ConcurrencyToken != request.PackageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pytanie lub pakiet zostały zmienione w innej sesji." }, statusCode: 409);

            var newKey = orig.Key + "_copy";
            var counter = 1;
            while (allQuestions.Any(q => q.Key.Equals(newKey, StringComparison.OrdinalIgnoreCase)))
            {
                newKey = $"{orig.Key}_copy_{counter++}";
            }

            var now = clock.UtcNow;
            var duplicate = new GameQuestion
            {
                Id = Guid.NewGuid(),
                CategoryId = orig.CategoryId,
                Key = newKey,
                Type = orig.Type,
                TextPl = orig.TextPl,
                TextEn = orig.TextEn,
                IsActive = orig.IsActive,
                MinimumPlayers = orig.MinimumPlayers,
                SortOrder = orig.SortOrder + 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N"),
                Category = orig.Category
            };

            dbContext.GameQuestions.Add(duplicate);
            package.ConcurrencyToken = Guid.NewGuid().ToString("N");
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pytanie zostało zmienione w innej sesji." }, statusCode: 409); }

            return Results.Created($"/api/admin/content-packages/{packageVersionId}/questions/{duplicate.Id}", new { question = ToQuestionResponse(duplicate), packageConcurrencyToken = package.ConcurrencyToken });
        });

        admin.MapPost("/{packageVersionId:guid}/questions/reorder", async (Guid packageVersionId, ReorderRequest request, PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var package = await dbContext.GamePackages.Include(p => p.Categories).ThenInclude(c => c.Questions).FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null || package.Status != ContentPackageStatus.Draft) return Results.Json(new { code = "content_package_not_editable" }, statusCode: 400);

            if (!string.IsNullOrEmpty(request.PackageConcurrencyToken) && package.ConcurrencyToken != request.PackageConcurrencyToken)
                return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet zmieniony w innej sesji." }, statusCode: 409);

            if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.SortOrder < 0) || request.Items.Select(i => i.Id).Distinct().Count() != request.Items.Count || request.Items.Select(i => i.SortOrder).Distinct().Count() != request.Items.Count)
                return Results.Json(new { code = "content_validation_failed", message = "Nieprawidłowa lista kolejności pytań." }, statusCode: 400);
            var allQuestions = package.Categories.SelectMany(c => c.Questions).ToList();
            var selected = request.Items.Select(item => allQuestions.FirstOrDefault(x => x.Id == item.Id)).ToList();
            if (selected.Any(q => q is null) || selected.Select(q => q!.CategoryId).Distinct().Count() != 1)
                return Results.Json(new { code = "content_validation_failed", message = "Pytania muszą należeć do jednej kategorii pakietu." }, statusCode: 400);
            var categoryQuestions = allQuestions.Where(q => q.CategoryId == selected[0]!.CategoryId).ToList();
            if (categoryQuestions.Count != request.Items.Count || categoryQuestions.Any(q => request.Items.All(item => item.Id != q.Id)))
                return Results.Json(new { code = "content_validation_failed", message = "Reorder wymaga pełnej listy pytań kategorii." }, statusCode: 400);
            foreach (var item in request.Items)
            {
                var q = selected.Single(x => x!.Id == item.Id)!;
                q.SortOrder = item.SortOrder;
                q.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }

            package.ConcurrencyToken = Guid.NewGuid().ToString("N");
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "content_concurrency_conflict", message = "Pakiet został zmieniony w innej sesji." }, statusCode: 409); }
            return Results.Ok(new { items = categoryQuestions.OrderBy(q => q.SortOrder).ThenBy(q => q.Id).Select(q => ToQuestionResponse(q)), packageConcurrencyToken = package.ConcurrencyToken });
        });

        return endpoints;
    }

    private static object ToPackageResponse(GamePackage p)
    {
        var questions = p.Categories.SelectMany(c => c.Questions).ToList();
        var byType = questions.GroupBy(q => q.Type.ToString()).ToDictionary(g => g.Key, g => g.Count());

        return new
        {
            id = p.Id,
            logicalPackageId = p.LogicalPackageId,
            version = p.Version,
            key = p.Key,
            namePl = p.NamePl,
            nameEn = p.NameEn,
            descriptionPl = p.DescriptionPl,
            descriptionEn = p.DescriptionEn,
            status = p.Status.ToString(),
            isActive = p.IsActive,
            isDefault = p.IsDefault,
            sortOrder = p.SortOrder,
            categoryCount = p.Categories.Count,
            questionCount = questions.Count,
            questionCountByType = byType,
            createdAtUtc = p.CreatedAtUtc,
            updatedAtUtc = p.UpdatedAtUtc,
            publishedAtUtc = p.PublishedAtUtc,
            archivedAtUtc = p.ArchivedAtUtc,
            concurrencyToken = p.ConcurrencyToken
        };
    }

    private static object ToPackageDetailResponse(GamePackage p)
    {
        var baseResp = (IDictionary<string, object?>)AnonymousToDictionary(ToPackageResponse(p));
        baseResp["categories"] = p.Categories.OrderBy(c => c.SortOrder).Select(c => ToCategoryResponse(c)).ToList();
        return baseResp;
    }

    private static object ToCategoryResponse(GameCategory c) => new
    {
        id = c.Id,
        packageId = c.PackageId,
        key = c.Key,
        namePl = c.NamePl,
        nameEn = c.NameEn,
        descriptionPl = c.DescriptionPl,
        descriptionEn = c.DescriptionEn,
        isActive = c.IsActive,
        sortOrder = c.SortOrder,
        questionCount = c.Questions.Count,
        concurrencyToken = c.ConcurrencyToken
    };

    private static object ToQuestionResponse(GameQuestion q, IEnumerable<object>? validationErrors = null) => new
    {
        id = q.Id,
        packageId = q.Category?.PackageId,
        categoryId = q.CategoryId,
        categoryKey = q.Category?.Key ?? "",
        categoryNamePl = q.Category?.NamePl ?? "",
        categoryName = q.Category?.NamePl ?? "",
        key = q.Key,
        questionType = q.Type.ToString(),
        type = q.Type.ToString(),
        textPl = q.TextPl,
        textEn = q.TextEn,
        isEnabled = q.IsActive,
        isActive = q.IsActive,
        minimumPlayers = q.MinimumPlayers,
        sortOrder = q.SortOrder,
        createdAtUtc = q.CreatedAtUtc,
        updatedAtUtc = q.UpdatedAtUtc,
        concurrencyToken = q.ConcurrencyToken,
        validationErrors = validationErrors ?? Array.Empty<object>()
    };

    private static IDictionary<string, object?> AnonymousToDictionary(object obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj.GetType().GetProperties())
        {
            dict[prop.Name] = prop.GetValue(obj);
        }
        return dict;
    }
}

public record CreatePackageRequest(string? NamePl, string? NameEn, string? DescriptionPl, string? DescriptionEn, string? Key);
public record UpdatePackageRequest(string? NamePl, string? NameEn, string? DescriptionPl, string? DescriptionEn, bool? IsActive, string? ConcurrencyToken);
public record PublishPackageRequest(string? ConcurrencyToken);
public record ArchivePackageRequest(string? ConcurrencyToken);

public record CreateCategoryRequest(string? Key, string? NamePl, string? NameEn, string? DescriptionPl, string? DescriptionEn, bool IsActive = true, int? SortOrder = null, string? PackageConcurrencyToken = null);
public record UpdateCategoryRequest(string? Key, string? NamePl, string? NameEn, string? DescriptionPl, string? DescriptionEn, bool? IsActive, int? SortOrder, string? ConcurrencyToken, string? PackageConcurrencyToken);

public record CreateQuestionRequest(Guid CategoryId, string? Key, QuestionType Type, string? TextPl, string? TextEn, bool IsActive = true, int MinimumPlayers = 3, int? SortOrder = null);
public record UpdateQuestionRequest(Guid? CategoryId, QuestionType? Type, string? TextPl, string? TextEn, bool? IsActive, int? MinimumPlayers, string? ConcurrencyToken, string? PackageConcurrencyToken = null, int? SortOrder = null);
public record QuestionMutationRequest(string? ConcurrencyToken, string? PackageConcurrencyToken);

public record ReorderItem(Guid Id, int SortOrder);
public record ReorderRequest(string? PackageConcurrencyToken, List<ReorderItem> Items);
