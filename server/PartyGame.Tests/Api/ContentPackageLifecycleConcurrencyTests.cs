using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Infrastructure.Persistence;
using Xunit;

namespace PartyGame.Tests.Api;

public sealed class ContentPackageLifecycleConcurrencyTests : IClassFixture<PartyGameApiFactory>
{
    private readonly PartyGameApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ContentPackageLifecycleConcurrencyTests(PartyGameApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateDraft_TaskWhenAll_CreatesExactlyOneDeepCopy()
    {
        var source = await CreatePublishedAsync("parallel_draft", questionCount: 2);
        var responses = await Task.WhenAll(
            _factory.CreateClient().PostAsync($"/api/admin/content-packages/{source.Id}/create-draft", null),
            _factory.CreateClient().PostAsync($"/api/admin/content-packages/{source.Id}/create-draft", null));

        var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("content_package_already_has_draft", (await conflict.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("code").GetString());
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);

        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var family = await db.GamePackages.Include(package => package.Categories).ThenInclude(category => category.Questions)
            .Where(package => package.LogicalPackageId == source.LogicalId).ToListAsync();
        var draft = Assert.Single(family, package => package.Status == ContentPackageStatus.Draft);
        Assert.Equal(draftId, draft.Id);
        Assert.Equal(2, draft.Version);
        Assert.Equal(source.Categories, draft.Categories.Count);
        Assert.Equal(source.Questions, draft.Categories.Sum(category => category.Questions.Count));
        Assert.Equal(source.Categories, draft.Categories.Select(category => category.Id).Distinct().Count());
        Assert.Equal(source.Questions, draft.Categories.SelectMany(category => category.Questions).Select(question => question.Id).Distinct().Count());
    }

    [Fact]
    public async Task CreateDraft_DeepCopyHasNewIdentifiersTokensAndIndependentContent()
    {
        var source = await CreatePublishedAsync("deep_copy", questionCount: 2);
        var response = await _client.PostAsync($"/api/admin/content-packages/{source.Id}/create-draft", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draftId = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var packages = await db.GamePackages.Include(package => package.Categories).ThenInclude(category => category.Questions)
            .Where(package => package.Id == source.Id || package.Id == draftId).ToListAsync();
        var published = Assert.Single(packages, package => package.Id == source.Id);
        var draft = Assert.Single(packages, package => package.Id == draftId);
        Assert.Equal(published.LogicalPackageId, draft.LogicalPackageId);
        Assert.Equal(published.Version + 1, draft.Version);
        Assert.Equal(ContentPackageStatus.Draft, draft.Status);
        Assert.Null(draft.PublishedAtUtc);
        Assert.Null(draft.ArchivedAtUtc);
        Assert.NotEqual(published.ConcurrencyToken, draft.ConcurrencyToken);
        Assert.Equal(published.NamePl, draft.NamePl);
        Assert.Equal(published.DescriptionEn, draft.DescriptionEn);
        Assert.Equal(published.Categories.Count, draft.Categories.Count);
        foreach (var sourceCategory in published.Categories)
        {
            var copiedCategory = Assert.Single(draft.Categories, category => category.Key == sourceCategory.Key);
            Assert.NotEqual(sourceCategory.Id, copiedCategory.Id);
            Assert.NotEqual(sourceCategory.ConcurrencyToken, copiedCategory.ConcurrencyToken);
            Assert.Equal((sourceCategory.NamePl, sourceCategory.NameEn, sourceCategory.DescriptionPl, sourceCategory.DescriptionEn, sourceCategory.IsActive, sourceCategory.SortOrder),
                (copiedCategory.NamePl, copiedCategory.NameEn, copiedCategory.DescriptionPl, copiedCategory.DescriptionEn, copiedCategory.IsActive, copiedCategory.SortOrder));
            foreach (var sourceQuestion in sourceCategory.Questions)
            {
                var copiedQuestion = Assert.Single(copiedCategory.Questions, question => question.Key == sourceQuestion.Key);
                Assert.NotEqual(sourceQuestion.Id, copiedQuestion.Id);
                Assert.NotEqual(sourceQuestion.ConcurrencyToken, copiedQuestion.ConcurrencyToken);
                Assert.Equal((sourceQuestion.Type, sourceQuestion.TextPl, sourceQuestion.TextEn, sourceQuestion.MinimumPlayers, sourceQuestion.IsActive, sourceQuestion.SortOrder),
                    (copiedQuestion.Type, copiedQuestion.TextPl, copiedQuestion.TextEn, copiedQuestion.MinimumPlayers, copiedQuestion.IsActive, copiedQuestion.SortOrder));
            }
        }

        var draftToken = draft.ConcurrencyToken;
        var edit = await _client.PatchAsJsonAsync($"/api/admin/content-packages/{draft.Id}", new { namePl = "Zmieniony Draft", concurrencyToken = draftToken });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal("deep_copy PL", (await db.GamePackages.SingleAsync(package => package.Id == published.Id)).NamePl);
    }

    [Fact]
    public async Task PublishVsPublish_TaskWhenAll_HasOneWinnerAndNoPartialState()
    {
        var draft = await CreateValidDraftAsync("publish_publish", questionCount: 1);
        var responses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{draft.Id}/publish", new { concurrencyToken = draft.Token }),
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{draft.Id}/publish", new { concurrencyToken = draft.Token }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);
        var package = await GetPackageAsync(draft.Id);
        Assert.Equal("Published", package.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(package.GetProperty("publishedAtUtc").GetString()));
        Assert.Equal(1, package.GetProperty("categoryCount").GetInt32());
        Assert.Equal(1, package.GetProperty("questionCount").GetInt32());
    }

    [Fact]
    public async Task PublishVsMetadataCategoryQuestionAndReorder_HasControlledOutcomes()
    {
        var metadata = await CreateValidDraftAsync("publish_metadata", questionCount: 2);
        var metadataResponses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{metadata.Id}/publish", new { concurrencyToken = metadata.Token }),
            _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{metadata.Id}", new { namePl = "Równoległa nazwa", concurrencyToken = metadata.Token }));
        AssertRace(metadataResponses);

        var category = await CreateValidDraftAsync("publish_category", questionCount: 2);
        var categoryResponses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{category.Id}/publish", new { concurrencyToken = category.Token }),
            _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{category.Id}/categories/{category.CategoryIds[0]}", new { namePl = "Równoległa kategoria", concurrencyToken = category.CategoryTokens[0], packageConcurrencyToken = category.Token }));
        AssertRace(categoryResponses);

        var question = await CreateValidDraftAsync("publish_question", questionCount: 2);
        var questionResponses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{question.Id}/publish", new { concurrencyToken = question.Token }),
            _factory.CreateClient().PatchAsJsonAsync($"/api/admin/content-packages/{question.Id}/questions/{question.QuestionIds[0]}", new { textPl = "Równoległe pytanie", concurrencyToken = question.QuestionTokens[0], packageConcurrencyToken = question.Token }));
        AssertRace(questionResponses);

        var categoryOrder = await CreateValidDraftAsync("publish_category_order", questionCount: 2, categoryCount: 2);
        var categoryOrderResponses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{categoryOrder.Id}/publish", new { concurrencyToken = categoryOrder.Token }),
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{categoryOrder.Id}/categories/reorder", new { packageConcurrencyToken = categoryOrder.Token, items = categoryOrder.CategoryIds.Select((id, index) => new { id, sortOrder = categoryOrder.CategoryIds.Count - index - 1 }) }));
        AssertRace(categoryOrderResponses);

        var questionOrder = await CreateValidDraftAsync("publish_question_order", questionCount: 2);
        var questionOrderResponses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{questionOrder.Id}/publish", new { concurrencyToken = questionOrder.Token }),
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{questionOrder.Id}/questions/reorder", new { packageConcurrencyToken = questionOrder.Token, items = questionOrder.QuestionIds.Select((id, index) => new { id, sortOrder = questionOrder.QuestionIds.Count - index - 1 }) }));
        AssertRace(questionOrderResponses);
    }

    [Fact]
    public async Task ArchiveVsCreateRoom_TaskWhenAll_BindsRoomOrRejectsItWithout500()
    {
        var published = await CreatePublishedAsync("archive_room", questionCount: 1);
        var responses = await Task.WhenAll(
            _factory.CreateClient().PostAsJsonAsync($"/api/admin/content-packages/{published.Id}/archive", new { concurrencyToken = published.Token }),
            _factory.CreateClient().PostAsJsonAsync("/api/rooms", new CreateRoomRequest("ArchiveHost", null, null, null, published.Id)));
        var archive = responses.Single(response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/archive", StringComparison.Ordinal));
        var room = responses.Single(response => response.RequestMessage!.RequestUri!.AbsolutePath == "/api/rooms");
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.True(room.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest);
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);
        if (room.StatusCode == HttpStatusCode.Created)
        {
            var access = await room.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions);
            Assert.Equal(published.Id, access!.Snapshot.ContentPackageVersionId);
        }
    }

    [Fact]
    public async Task ArchivedAndNewerVersions_PreserveExistingRoomBindingAndRejectInvalidIds()
    {
        var v1 = await CreatePublishedAsync("room_history", questionCount: 1);
        var oldRoom = await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("HistoryHost", null, null, null, v1.Id));
        var oldAccess = await oldRoom.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.Created, oldRoom.StatusCode);
        Assert.Equal(v1.Id, oldAccess!.Snapshot.ContentPackageVersionId);

        var v2Response = await _client.PostAsync($"/api/admin/content-packages/{v1.Id}/create-draft", null);
        var v2Id = (await v2Response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
        var v2 = await GetPackageAsync(v2Id);
        var publish = await _client.PostAsJsonAsync($"/api/admin/content-packages/{v2Id}/publish", new { concurrencyToken = v2.GetProperty("concurrencyToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var explicitV2 = await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("V2Host", null, null, null, v2Id));
        Assert.Equal(HttpStatusCode.Created, explicitV2.StatusCode);

        var v1Fresh = await GetPackageAsync(v1.Id);
        var archive = await _client.PostAsJsonAsync($"/api/admin/content-packages/{v1.Id}/archive", new { concurrencyToken = v1Fresh.GetProperty("concurrencyToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var reloadedRoom = await _client.GetAsync($"/api/rooms/{oldAccess.RoomCode}");
        var reloaded = await reloadedRoom.Content.ReadFromJsonAsync<RoomSnapshot>(JsonOptions);
        Assert.Equal(v1.Id, reloaded!.ContentPackageVersionId);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("Archived", null, null, null, v1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("Draft", null, null, null, Guid.NewGuid()))).StatusCode);
    }

    private static void AssertRace(IEnumerable<HttpResponseMessage> responses)
    {
        var list = responses.ToList();
        Assert.Contains(list, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(list, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        Assert.DoesNotContain(list, response => (int)response.StatusCode >= 500);
    }

    private async Task<JsonElement> GetPackageAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/admin/content-packages/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<PackageSeed> CreatePublishedAsync(string prefix, int questionCount)
    {
        var draft = await CreateValidDraftAsync(prefix, questionCount);
        var response = await _client.PostAsJsonAsync($"/api/admin/content-packages/{draft.Id}/publish", new { concurrencyToken = draft.Token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return draft with { Token = published.GetProperty("concurrencyToken").GetString()! };
    }

    private async Task<PackageSeed> CreateValidDraftAsync(string prefix, int questionCount, int categoryCount = 1)
    {
        var packageResponse = await _client.PostAsJsonAsync("/api/admin/content-packages", new { key = prefix, namePl = $"{prefix} PL", nameEn = $"{prefix} EN", descriptionPl = $"{prefix} opis", descriptionEn = $"{prefix} description" });
        Assert.Equal(HttpStatusCode.Created, packageResponse.StatusCode);
        var package = await packageResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = package.GetProperty("id").GetGuid();
        var logicalId = package.GetProperty("logicalPackageId").GetGuid();
        var token = package.GetProperty("concurrencyToken").GetString()!;
        var categoryIds = new List<Guid>(); var categoryTokens = new List<string>(); var questionIds = new List<Guid>(); var questionTokens = new List<string>();
        for (var categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            var categoryResponse = await _client.PostAsJsonAsync($"/api/admin/content-packages/{id}/categories", new { key = $"{prefix}_cat_{categoryIndex}", namePl = $"{prefix} kategoria {categoryIndex}", nameEn = $"{prefix} category {categoryIndex}", descriptionPl = "opis", descriptionEn = "description", isActive = true, sortOrder = categoryIndex, packageConcurrencyToken = token });
            Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
            var category = await categoryResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var item = category.GetProperty("category");
            categoryIds.Add(item.GetProperty("id").GetGuid()); categoryTokens.Add(item.GetProperty("concurrencyToken").GetString()!);
            token = category.GetProperty("packageConcurrencyToken").GetString()!;
        }
        for (var questionIndex = 0; questionIndex < questionCount; questionIndex++)
        {
            var questionResponse = await _client.PostAsJsonAsync($"/api/admin/content-packages/{id}/questions", new { categoryId = categoryIds[0], key = $"{prefix}_question_{questionIndex}", type = 0, textPl = "Kto wybiera {player}?", textEn = "Who chooses {player}?", isActive = true, minimumPlayers = 3, sortOrder = questionIndex });
            Assert.Equal(HttpStatusCode.Created, questionResponse.StatusCode);
            var question = await questionResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            questionIds.Add(question.GetProperty("id").GetGuid()); questionTokens.Add(question.GetProperty("concurrencyToken").GetString()!);
        }
        var categories = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/content-packages/{id}/categories", JsonOptions);
        var freshCategories = categories.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());
        categoryTokens = categoryIds.Select(categoryId => freshCategories[categoryId].GetProperty("concurrencyToken").GetString()!).ToList();
        return new PackageSeed(id, logicalId, categories.GetProperty("packageConcurrencyToken").GetString()!, categoryIds, categoryTokens, questionIds, questionTokens);
    }

    private sealed record PackageSeed(Guid Id, Guid LogicalId, string Token, List<Guid> CategoryIds, List<string> CategoryTokens, List<Guid> QuestionIds, List<string> QuestionTokens)
    {
        public int Categories => CategoryIds.Count;
        public int Questions => QuestionIds.Count;
    }
}
