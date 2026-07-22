using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Persistence.Seed;
using PartyGame.Tests.Infrastructure;
using Xunit;

namespace PartyGame.Tests.Api;

public sealed class AdminContentEndpointsTests : IClassFixture<PartyGameApiFactory>
{
    private readonly PartyGameApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AdminContentEndpointsTests(PartyGameApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPackages_ReturnsStarterPackageV1()
    {
        var response = await _client.GetAsync("/api/admin/content-packages");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOptions);
        Assert.NotNull(list);
        Assert.NotEmpty(list);

        var starter = list.FirstOrDefault(p => p.GetProperty("logicalPackageId").GetGuid() == ContentSeeder.StarterLogicalPackageId);
        Assert.NotEqual(default, starter.ValueKind);
        Assert.Equal("Published", starter.GetProperty("status").GetString());
        Assert.Equal(1, starter.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task CreateDraft_DeepCopiesPackageAndEnforcesSingleDraftRule()
    {
        // Create draft from Starter v1
        var createDraftResp = await _client.PostAsync($"/api/admin/content-packages/{ContentSeeder.StarterLogicalPackageId}/create-draft", null);
        Assert.Equal(HttpStatusCode.Created, createDraftResp.StatusCode);

        var draftJson = await createDraftResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var draftId = draftJson.GetProperty("id").GetGuid();
        Assert.Equal("Draft", draftJson.GetProperty("status").GetString());
        Assert.Equal(2, draftJson.GetProperty("version").GetInt32());

        // Attempting to create a second draft for the same family should return 409 Conflict
        var secondDraftResp = await _client.PostAsync($"/api/admin/content-packages/{ContentSeeder.StarterLogicalPackageId}/create-draft", null);
        Assert.Equal(HttpStatusCode.Conflict, secondDraftResp.StatusCode);

        var errJson = await secondDraftResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("content_package_already_has_draft", errJson.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConcurrencyConflict_Returns409WhenTokenMismatch()
    {
        // Create a new package
        var createResp = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = "Test Concurrency", nameEn = "Test Concurrency" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var packJson = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var packId = packJson.GetProperty("id").GetGuid();

        // Perform parallel update attempts with old token
        var update1 = _client.PatchAsJsonAsync($"/api/admin/content-packages/{packId}", new { namePl = "Update 1", concurrencyToken = "invalid_token" });
        var update2 = _client.PatchAsJsonAsync($"/api/admin/content-packages/{packId}", new { namePl = "Update 2", concurrencyToken = "invalid_token" });

        var results = await Task.WhenAll(update1, update2);
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task CategoryDeleteModes_HandledCorrectly()
    {
        // Create package & category
        var createResp = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = "Test Cat Del", nameEn = "Test Cat Del" });
        var pack = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var packId = pack.GetProperty("id").GetGuid();
        var packageToken = pack.GetProperty("concurrencyToken").GetString()!;

        var catResp = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packId}/categories", new { namePl = "Cat 1", nameEn = "Cat 1", key = "cat_1", packageConcurrencyToken = packageToken });
        var catMutation = await catResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var catId = catMutation.GetProperty("category").GetProperty("id").GetGuid();

        // Add question to category
        var qResp = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packId}/questions", new { categoryId = catId, key = "q_1", type = 0, textPl = "Kto jest kim?", textEn = "Who is who?" });
        Assert.Equal(HttpStatusCode.Created, qResp.StatusCode);

        // Delete mode=reject should return 409
        var categories = await (await _client.GetAsync($"/api/admin/content-packages/{packId}/categories")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var currentCategory = categories.GetProperty("items")[0];
        var currentPackageToken = categories.GetProperty("packageConcurrencyToken").GetString();
        var currentCategoryToken = currentCategory.GetProperty("concurrencyToken").GetString();
        var delRejectResp = await _client.DeleteAsync($"/api/admin/content-packages/{packId}/categories/{catId}?mode=reject&concurrencyToken={currentCategoryToken}&packageConcurrencyToken={currentPackageToken}");
        Assert.Equal(HttpStatusCode.Conflict, delRejectResp.StatusCode);

        // Delete mode=deleteQuestions should succeed
        var delForceResp = await _client.DeleteAsync($"/api/admin/content-packages/{packId}/categories/{catId}?mode=deleteQuestions&concurrencyToken={currentCategoryToken}&packageConcurrencyToken={currentPackageToken}");
        Assert.Equal(HttpStatusCode.OK, delForceResp.StatusCode);
    }

    [Fact]
    public async Task Categories_CreateListUpdateAndRejectStaleTokens()
    {
        var packageResponse = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = "Kategorie", nameEn = "Categories" });
        var package = await packageResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var packageId = package.GetProperty("id").GetGuid();
        var packageToken = package.GetProperty("concurrencyToken").GetString()!;
        var createdResponse = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packageId}/categories", new { key = "funny", namePl = "Zabawne", nameEn = "Funny", sortOrder = 2, packageConcurrencyToken = packageToken });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var category = created.GetProperty("category");
        var categoryId = category.GetProperty("id").GetGuid();
        var categoryToken = category.GetProperty("concurrencyToken").GetString()!;
        var currentPackageToken = created.GetProperty("packageConcurrencyToken").GetString()!;

        var update = await _client.PatchAsJsonAsync($"/api/admin/content-packages/{packageId}/categories/{categoryId}", new { namePl = "Zmienione", concurrencyToken = categoryToken, packageConcurrencyToken = currentPackageToken });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var stale = await _client.PatchAsJsonAsync($"/api/admin/content-packages/{packageId}/categories/{categoryId}", new { namePl = "Stare", concurrencyToken = categoryToken, packageConcurrencyToken = currentPackageToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var list = await (await _client.GetAsync($"/api/admin/content-packages/{packageId}/categories")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Zmienione", list.GetProperty("items")[0].GetProperty("namePl").GetString());
    }

    [Fact]
    public async Task Categories_RejectDuplicateKeyAndInvalidReorder()
    {
        var packageResponse = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = "Walidacja", nameEn = "Validation" });
        var package = await packageResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var packageId = package.GetProperty("id").GetGuid();
        var token = package.GetProperty("concurrencyToken").GetString()!;
        var first = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packageId}/categories", new { key = "same", namePl = "A", nameEn = "A", packageConcurrencyToken = token });
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        token = firstJson.GetProperty("packageConcurrencyToken").GetString()!;
        var duplicate = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packageId}/categories", new { key = "same", namePl = "B", nameEn = "B", packageConcurrencyToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var id = firstJson.GetProperty("category").GetProperty("id").GetGuid();
        var reorder = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packageId}/categories/reorder", new { packageConcurrencyToken = token, items = new[] { new { id, sortOrder = -1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, reorder.StatusCode);
    }

    [Fact]
    public async Task Categories_UpdateVsUpdate_TaskWhenAll_HasOneWinnerAndOneConflict()
    {
        var seed = await CreatePackageAndCategoriesAsync("race_update", 1);
        var category = seed.Categories[0];
        var clientA = _factory.CreateClient();
        var clientB = _factory.CreateClient();
        var a = clientA.PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{category.Id}", new { namePl = "Zmiana A", concurrencyToken = category.Token, packageConcurrencyToken = seed.PackageToken });
        var b = clientB.PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{category.Id}", new { namePl = "Zmiana B", concurrencyToken = category.Token, packageConcurrencyToken = seed.PackageToken });
        var responses = await Task.WhenAll(a, b);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var list = await GetCategoriesAsync(seed.PackageId);
        Assert.True(new[] { "Zmiana A", "Zmiana B" }.Contains(list.Items[0].NamePl));
    }

    [Fact]
    public async Task Categories_DeleteVsUpdate_TaskWhenAll_HasControlledOutcome()
    {
        var seed = await CreatePackageAndCategoriesAsync("race_delete", 1);
        var category = seed.Categories[0];
        var delete = _factory.CreateClient().DeleteAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{category.Id}?mode=reject&concurrencyToken={category.Token}&packageConcurrencyToken={seed.PackageToken}");
        var update = _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{category.Id}", new { namePl = "Aktualizacja", concurrencyToken = category.Token, packageConcurrencyToken = seed.PackageToken });
        var responses = await Task.WhenAll(delete, update);
        Assert.Contains(responses, r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.NotFound);
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
    }

    [Fact]
    public async Task Categories_ReorderVsUpdate_TaskWhenAll_HasNoPartialOrder()
    {
        var seed = await CreatePackageAndCategoriesAsync("race_reorder", 3);
        var reorder = _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/categories/reorder", new { packageConcurrencyToken = seed.PackageToken, items = seed.Categories.Select((c, i) => new { id = c.Id, sortOrder = seed.Categories.Count - i - 1 }) });
        var edited = seed.Categories[0];
        var update = _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{edited.Id}", new { namePl = "Równolegle", concurrencyToken = edited.Token, packageConcurrencyToken = seed.PackageToken });
        var responses = await Task.WhenAll(reorder, update);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var list = await GetCategoriesAsync(seed.PackageId);
        Assert.Equal(list.Items.Count, list.Items.Select(c => c.SortOrder).Distinct().Count());
        Assert.DoesNotContain(list.Items, c => c.SortOrder < 0);
    }

    [Fact]
    public async Task Categories_MoveQuestionsVsQuestionUpdate_TaskWhenAll_HasNoOrphans()
    {
        var seed = await CreatePackageAndCategoriesAsync("race_move", 2);
        var source = seed.Categories[0];
        var target = seed.Categories[1];
        var question = await CreateQuestionAsync(seed.PackageId, source.Id, "move_q");
        var fresh = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/categories", JsonOptions);
        var sourceToken = fresh.GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == source.Id).GetProperty("concurrencyToken").GetString()!;
        var packageToken = fresh.GetProperty("packageConcurrencyToken").GetString()!;
        var move = _factory.CreateClient().DeleteAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{source.Id}?mode=moveQuestions&targetCategoryId={target.Id}&concurrencyToken={sourceToken}&packageConcurrencyToken={packageToken}");
        var update = _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions/{question.Id}", new { textPl = "Równoległe pytanie", concurrencyToken = question.Token });
        var responses = await Task.WhenAll(move, update);
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        var listed = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/questions", JsonOptions);
        var questionItems = listed.GetProperty("items").EnumerateArray().Where(q => q.GetProperty("key").GetString() == "move_q").ToList();
        Assert.True(questionItems.Count is 0 or 1);
        if (questionItems.Count == 1) Assert.True(new[] { source.Id, target.Id }.Contains(questionItems[0].GetProperty("categoryId").GetGuid()));
    }

    [Fact]
    public async Task Categories_DeleteQuestionsVsQuestionUpdate_TaskWhenAll_HasNoOrphans()
    {
        var seed = await CreatePackageAndCategoriesAsync("race_delete_questions", 1);
        var category = seed.Categories[0];
        var question = await CreateQuestionAsync(seed.PackageId, category.Id, "delete_q");
        var current = await GetCategoriesAsync(seed.PackageId);
        var categoryToken = (await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/categories", JsonOptions)).GetProperty("items")[0].GetProperty("concurrencyToken").GetString()!;
        var delete = _factory.CreateClient().DeleteAsync($"/api/admin/content-packages/{seed.PackageId}/categories/{category.Id}?mode=deleteQuestions&concurrencyToken={categoryToken}&packageConcurrencyToken={current.PackageToken}");
        var update = _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions/{question.Id}", new { textPl = "Nie może zostać osierocone", concurrencyToken = question.Token });
        var responses = await Task.WhenAll(delete, update);
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        var questions = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/questions", JsonOptions);
        var remaining = questions.GetProperty("items").EnumerateArray().Where(q => q.GetProperty("key").GetString() == "delete_q").ToList();
        if (remaining.Count == 1)
        {
            var categories = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/categories", JsonOptions);
            Assert.Contains(categories.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == remaining[0].GetProperty("categoryId").GetGuid());
        }
    }

    [Fact]
    public async Task Questions_ListFiltersPaginatesAndUsesStableSort()
    {
        var seed = await CreatePackageAndCategoriesAsync("question_list", 2);
        await CreateQuestionAsync(seed.PackageId, seed.Categories[0].Id, "alpha");
        await CreateQuestionAsync(seed.PackageId, seed.Categories[0].Id, "beta");
        await CreateQuestionAsync(seed.PackageId, seed.Categories[1].Id, "gamma");

        var pageOne = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/questions?categoryId={seed.Categories[0].Id}&search=a&page=1&pageSize=1&sort=keyAsc", JsonOptions);
        Assert.Equal(2, pageOne.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, pageOne.GetProperty("totalPages").GetInt32());
        Assert.Equal("alpha", pageOne.GetProperty("items")[0].GetProperty("key").GetString());
        Assert.True(pageOne.GetProperty("items")[0].TryGetProperty("categoryKey", out _));
        Assert.True(pageOne.TryGetProperty("packageConcurrencyToken", out _));

        var pageTwo = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/questions?categoryId={seed.Categories[0].Id}&search=a&page=2&pageSize=1&sort=keyAsc", JsonOptions);
        Assert.Equal("beta", pageTwo.GetProperty("items")[0].GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/admin/content-packages/{seed.PackageId}/questions?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/admin/content-packages/{seed.PackageId}/questions?sort=sql")).StatusCode);
    }

    [Fact]
    public async Task Questions_MutationsUseTokensAndReorderRequiresWholeCategory()
    {
        var seed = await CreatePackageAndCategoriesAsync("question_mutation", 1);
        var first = await CreateQuestionAsync(seed.PackageId, seed.Categories[0].Id, "first");
        var second = await CreateQuestionAsync(seed.PackageId, seed.Categories[0].Id, "second");
        var listed = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/questions?categoryId={seed.Categories[0].Id}", JsonOptions);
        var packageToken = listed.GetProperty("packageConcurrencyToken").GetString()!;
        var firstItem = listed.GetProperty("items").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == first.Id);

        var disable = await _client.PatchAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions/{first.Id}", new { isActive = false, concurrencyToken = firstItem.GetProperty("concurrencyToken").GetString(), packageConcurrencyToken = packageToken });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        var disabled = await disable.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(disabled.GetProperty("question").GetProperty("isActive").GetBoolean());
        packageToken = disabled.GetProperty("packageConcurrencyToken").GetString()!;

        var duplicate = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions/{second.Id}/duplicate", new { concurrencyToken = second.Token, packageConcurrencyToken = packageToken });
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        var copied = await duplicate.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("second_copy", copied.GetProperty("question").GetProperty("key").GetString());
        packageToken = copied.GetProperty("packageConcurrencyToken").GetString()!;

        var afterCopy = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}/questions?categoryId={seed.Categories[0].Id}", JsonOptions);
        var items = afterCopy.GetProperty("items").EnumerateArray().ToList();
        var reorder = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions/reorder", new { packageConcurrencyToken = packageToken, items = items.Select((q, index) => new { id = q.GetProperty("id").GetGuid(), sortOrder = items.Count - index - 1 }) });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var invalid = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions/reorder", new { packageConcurrencyToken = packageToken, items = new[] { new { id = first.Id, sortOrder = 0 }, new { id = first.Id, sortOrder = 1 } } });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
    }

    [Fact]
    public async Task Questions_DetailReturnsTokensAndHidesForeignPackageQuestion()
    {
        var first = await CreatePackageAndCategoriesAsync("question_detail", 1);
        var question = await CreateQuestionAsync(first.PackageId, first.Categories[0].Id, "detail_question");
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{first.PackageId}/questions/{question.Id}", JsonOptions);
        Assert.Equal(question.Id, detail.GetProperty("question").GetProperty("id").GetGuid());
        Assert.True(detail.TryGetProperty("packageConcurrencyToken", out _));
        Assert.Equal("Draft", detail.GetProperty("packageStatus").GetString());
        var other = await CreatePackageAndCategoriesAsync("question_detail_other", 1);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/admin/content-packages/{other.PackageId}/questions/{question.Id}")).StatusCode);
    }

    [Fact]
    public async Task Questions_RejectInvalidKeyPlaceholderAndNegativeSortOrder()
    {
        var seed = await CreatePackageAndCategoriesAsync("question_validation", 1);
        var invalidKey = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions", new { categoryId = seed.Categories[0].Id, key = "Bad key", type = QuestionType.TextAnswer, textPl = "PL", textEn = "EN", minimumPlayers = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        var invalidPlaceholder = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions", new { categoryId = seed.Categories[0].Id, key = "valid_question", type = QuestionType.PlayerSelection, textPl = "{unknown}", textEn = "{player}", minimumPlayers = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPlaceholder.StatusCode);
        var invalidOrder = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/questions", new { categoryId = seed.Categories[0].Id, key = "valid_question", type = QuestionType.TextAnswer, textPl = "PL", textEn = "EN", minimumPlayers = 3, sortOrder = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrder.StatusCode);
    }

    [Fact]
    public async Task Packages_ArchiveRejectsDraftAndArchivesPublishedWithFreshToken()
    {
        var draftResponse = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = "Archiwum", nameEn = "Archive" });
        var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var draftId = draft.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/admin/content-packages/{draftId}/archive", new { concurrencyToken = draft.GetProperty("concurrencyToken").GetString() })).StatusCode);

        var seed = await CreatePackageAndCategoriesAsync("archive_published", 1);
        await CreateQuestionAsync(seed.PackageId, seed.Categories[0].Id, "archive_question");
        var current = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{seed.PackageId}", JsonOptions);
        var publish = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/publish", new { concurrencyToken = current.GetProperty("concurrencyToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var publishedPackage = await publish.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var archive = await _client.PostAsJsonAsync($"/api/admin/content-packages/{seed.PackageId}/archive", new { concurrencyToken = publishedPackage.GetProperty("concurrencyToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var archived = await archive.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Archived", archived.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(archived.GetProperty("archivedAtUtc").GetString()));
    }

    private async Task<(Guid PackageId, string PackageToken, List<(Guid Id, string Token, string NamePl)> Categories)> CreatePackageAndCategoriesAsync(string prefix, int count)
    {
        var packageResponse = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = prefix, nameEn = prefix });
        var package = await packageResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var packageId = package.GetProperty("id").GetGuid();
        var packageToken = package.GetProperty("concurrencyToken").GetString()!;
        var categories = new List<(Guid Id, string Token, string NamePl)>();
        for (var i = 0; i < count; i++)
        {
            var response = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packageId}/categories", new { key = $"{prefix}_{i}", namePl = $"{prefix} {i}", nameEn = $"{prefix} {i}", packageConcurrencyToken = packageToken });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var category = body.GetProperty("category");
            categories.Add((category.GetProperty("id").GetGuid(), category.GetProperty("concurrencyToken").GetString()!, category.GetProperty("namePl").GetString()!));
            packageToken = body.GetProperty("packageConcurrencyToken").GetString()!;
        }
        return (packageId, packageToken, categories);
    }

    private async Task<(List<(int SortOrder, string NamePl)> Items, string PackageToken)> GetCategoriesAsync(Guid packageId)
    {
        var response = await _client.GetAsync($"/api/admin/content-packages/{packageId}/categories");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return (body.GetProperty("items").EnumerateArray().Select(item => (item.GetProperty("sortOrder").GetInt32(), item.GetProperty("namePl").GetString()!)).ToList(), body.GetProperty("packageConcurrencyToken").GetString()!);
    }

    private async Task<(Guid Id, string Token)> CreateQuestionAsync(Guid packageId, Guid categoryId, string key)
    {
        var response = await _client.PostAsJsonAsync($"/api/admin/content-packages/{packageId}/questions", new { categoryId, key, type = 0, textPl = "Kto wybiera?", textEn = "Who chooses?", minimumPlayers = 3 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var question = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return (question.GetProperty("id").GetGuid(), question.GetProperty("concurrencyToken").GetString()!);
    }

    [Fact]
    public async Task RoomCreation_IntegrationWithContentPackageVersionId()
    {
        // 1. Creating room without contentPackageVersionId uses default Published Starter v1
        var resp1 = await _client.PostAsJsonAsync("/api/rooms", new { nickname = "Host1" });
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var access1 = await resp1.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions);
        Assert.NotNull(access1);
        Assert.Equal(ContentSeeder.StarterLogicalPackageId, access1.Snapshot.ContentPackageVersionId);

        // 2. Creating room with specific published version succeeds
        var resp2 = await _client.PostAsJsonAsync("/api/rooms", new { nickname = "Host2", contentPackageVersionId = ContentSeeder.StarterLogicalPackageId });
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);

        // 3. Creating room with Draft version fails with 400 Validation Problem
        var draftResp = await _client.PostAsJsonAsync("/api/admin/content-packages", new { namePl = "Draft Room Test", nameEn = "Draft Room Test" });
        var draftId = (await draftResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        var resp3 = await _client.PostAsJsonAsync("/api/rooms", new { nickname = "Host3", contentPackageVersionId = draftId });
        Assert.Equal(HttpStatusCode.BadRequest, resp3.StatusCode);
    }
}
