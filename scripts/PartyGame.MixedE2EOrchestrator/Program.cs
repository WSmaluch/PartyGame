using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using PartyGame.MixedE2EOrchestrator;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

var backendUrl = Required("PARTYGAME_MIXED_E2E_BACKEND_URL").TrimEnd('/');
var coordinationDir = Required("PARTYGAME_E2E_COORDINATION_DIR");
Directory.CreateDirectory(coordinationDir);

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var http = new HttpClient { BaseAddress = new Uri(backendUrl) };
var stage = "package-setup";
var startedEvents = 0;
var tracker = new GameTracker();
var submissionIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
var observationFailures = new ConcurrentQueue<Exception>();
var backendObservations = new ClientStateVersionRecorder("backend", coordinationDir);
var hostObservations = new ClientStateVersionRecorder("scripted-player-a", coordinationDir);
var nodeObservations = new ClientStateVersionRecorder("scripted-player-b", coordinationDir);
Guid iosPlayerId = Guid.Empty;
var questions = new[]
{
    new QuestionDefinition("selection", "PlayerSelection", "Kto wybiera {player}?", "Who chooses {player}?", 0),
    new QuestionDefinition("text", "TextAnswer", "Napisz krótką odpowiedź.", "Write a short answer.", 1),
    new QuestionDefinition("photo", "PhotoAnswer", "Zrób zdjęcie czegoś niebieskiego.", "Take a photo of something blue.", 2),
    new QuestionDefinition("drawing", "DrawingAnswer", "Narysuj prosty symbol.", "Draw a simple symbol.", 3)
};

try
{
    var package = await PostJson("/api/admin/content-packages", new
    {
        key = "stage_7_2_mixed",
        namePl = "Stage 7.2 Mixed E2E",
        nameEn = "Stage 7.2 Mixed E2E",
        descriptionPl = "Pakiet czterech typów dla pełnego Mixed Client E2E.",
        descriptionEn = "Four-type package for full Mixed Client E2E."
    });
    var packageId = package.GetProperty("id").GetGuid();
    var category = await PostJson($"/api/admin/content-packages/{packageId}/categories", new
    {
        key = "stage_7_2", namePl = "Orkiestracja", nameEn = "Orchestration",
        descriptionPl = "Pytania dla Mixed Client E2E.", descriptionEn = "Questions for Mixed Client E2E.",
        isActive = true, sortOrder = 0, packageConcurrencyToken = package.GetProperty("concurrencyToken").GetString()
    });
    var categoryId = category.GetProperty("category").GetProperty("id").GetGuid();
    var questionTypes = new Dictionary<Guid, string>();
    foreach (var question in questions)
    {
        var createdQuestion = await PostJson($"/api/admin/content-packages/{packageId}/questions", new
        {
            categoryId, key = question.Key, type = question.Type, textPl = question.TextPl, textEn = question.TextEn,
            isActive = true, minimumPlayers = 3, sortOrder = question.SortOrder
        });
        questionTypes.Add(createdQuestion.GetProperty("id").GetGuid(), question.Type);
    }

    var categories = await GetJson($"/api/admin/content-packages/{packageId}/categories");
    var published = await PostJson($"/api/admin/content-packages/{packageId}/publish", new { concurrencyToken = categories.GetProperty("packageConcurrencyToken").GetString() });
    if (published.GetProperty("status").GetString() != "Published") throw new InvalidOperationException("Pakiet 7.2 nie został opublikowany.");

    stage = "room-creation";
    var roomAccess = await PostJson("/api/rooms", new
    {
        nickname = "E2E Host", contentPackageVersionId = packageId,
        enabledQuestionTypes = new[] { "PlayerSelection", "TextAnswer", "PhotoAnswer", "DrawingAnswer" },
        settings = new { roundCount = 1, questionsPerRound = 4, playerSelectionSeconds = 60, textAnswerSeconds = 60, votingSeconds = 60, photoSeconds = 90, drawingSeconds = 90, resultPresentationSeconds = 5, finalRoundEnabled = true, finalDrawingPasses = 2 }
    });
    var roomCode = roomAccess.GetProperty("roomCode").GetString()!;
    Observe(backendObservations, roomAccess.GetProperty("snapshot"), "room-created");
    var host = Access(roomAccess, "E2E Host");
    var node = Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "E2E Node" }), "E2E Node");
    if (roomAccess.GetProperty("snapshot").GetProperty("contentPackageVersionId").GetGuid() != packageId)
        throw new InvalidOperationException("Pokój nie został związany z wersją pakietu 7.2.");

    await UploadProfile(roomCode, host, await Jpeg(Color.Blue));
    await UploadProfile(roomCode, node, await Jpeg(Color.Green));
    var hostPrivate = new PrivateState();
    var nodePrivate = new PrivateState();
    await using var hostConnection = Connection();
    await using var nodeConnection = Connection();
    hostConnection.On<JsonElement>("RoomSnapshotUpdated", value => Observe(hostObservations, value, "snapshot-accepted"));
    nodeConnection.On<JsonElement>("RoomSnapshotUpdated", value => Observe(nodeObservations, value, "snapshot-accepted"));
    hostConnection.On<JsonElement>("RoomStarted", value =>
    {
        Interlocked.Increment(ref startedEvents);
        Observe(hostObservations, value, "room-started");
    });
    nodeConnection.On<JsonElement>("RoomStarted", value => Observe(nodeObservations, value, "room-started"));
    hostConnection.On<JsonElement>("PlayerPrivateGameStateUpdated", value => hostPrivate = Private(value));
    nodeConnection.On<JsonElement>("PlayerPrivateGameStateUpdated", value => nodePrivate = Private(value));
    await hostConnection.StartAsync(); await nodeConnection.StartAsync();
    Observe(hostObservations, await hostConnection.InvokeAsync<JsonElement>("AttachPlayer", roomCode, host.Id, host.Token), "attach-player-response");
    Observe(nodeObservations, await nodeConnection.InvokeAsync<JsonElement>("AttachPlayer", roomCode, node.Id, node.Token), "attach-player-response");

    await WriteJson("coordination.json", new { backendUrl, roomCode, contentPackageVersionId = packageId, iosNickname = "E2E iPhone", displayExpected = true, scriptedPlayers = new[] { host.Name, node.Name } });
    Mark("orchestrator-ready");
    stage = "waiting-for-real-clients";
    await WaitForMarker("display-attached", TimeSpan.FromSeconds(240));
    await WaitForMarker("ios-ready", TimeSpan.FromSeconds(90));
    var beforeStart = await GetJson($"/api/rooms/{roomCode}");
    if (beforeStart.GetProperty("roomCode").GetString() != roomCode ||
        beforeStart.GetProperty("phase").GetString() != "Lobby" ||
        !beforeStart.GetProperty("displayConnected").GetBoolean())
    {
        throw new InvalidOperationException("Initial Display attach nie potwierdził publicznego snapshotu Lobby dla właściwego pokoju.");
    }
    iosPlayerId = beforeStart.GetProperty("players").EnumerateArray()
        .Single(player => player.GetProperty("nickname").GetString() == "E2E iPhone")
        .GetProperty("id").GetGuid();

    stage = "scripted-ready";
    await hostConnection.InvokeAsync("SetReady", roomCode, host.Id, host.Token, true);
    await nodeConnection.InvokeAsync("SetReady", roomCode, node.Id, node.Token, true);
    stage = "game-start";
    await WaitUntil(() => Volatile.Read(ref startedEvents) == 1, TimeSpan.FromSeconds(30), "RoomStarted exactly once");
    var initial = await GetJson($"/api/rooms/{roomCode}");
    ValidateStarted(initial, packageId);
    Mark("game-started");

    stage = "four-question-game-and-final-round";
    var hostPhoto = await Jpeg(Color.Orange);
    var nodePhoto = await Jpeg(Color.Yellow);
    var hostDrawing = await Png(Color.Purple);
    var nodeDrawing = await Png(Color.Red);
    var actionedStages = new HashSet<string>(StringComparer.Ordinal);
    var finalActionedStages = new HashSet<string>(StringComparer.Ordinal);
    var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(7);
    while (DateTimeOffset.UtcNow < deadline)
    {
        var room = await GetJson($"/api/rooms/{roomCode}");
        ThrowObservationFailure();
        tracker.Observe(room);
        if (IsGameCompleted(room)) break;
        if (TryFinalStage(room, out var finalStage, out var currentPass))
        {
            var finalKey = $"{finalStage}:{currentPass}";
            if (!finalActionedStages.Add(finalKey))
            {
                await Task.Delay(100);
                continue;
            }

            switch (finalStage)
            {
                case "CollectingFinalSelfies":
                    await WaitForMarker("display-finalround-selfies", TimeSpan.FromSeconds(30));
                    await WaitForMarker($"ios-final-selfie-submitted-{room.GetProperty("stateVersion").GetInt64()}", TimeSpan.FromSeconds(45));
                    var hostFinalSelfieSubmissionId = SubmissionId(host, Guid.Empty, "final-selfie");
                    await UploadFinalSelfie(roomCode, host, hostPhoto, hostFinalSelfieSubmissionId);
                    await UploadFinalSelfie(roomCode, host, hostPhoto, hostFinalSelfieSubmissionId);
                    if (TryFinalStage(await GetJson($"/api/rooms/{roomCode}"), out var selfieStageAfterHost, out _) && selfieStageAfterHost == "CollectingFinalSelfies")
                        await UploadFinalSelfie(roomCode, node, nodePhoto, SubmissionId(node, Guid.Empty, "final-selfie"));
                    break;
                case "CollectingFinalEdits":
                    await WaitForMarker("display-finalround-edits", TimeSpan.FromSeconds(30));
                    await WaitForMarker($"ios-final-edit-submitted-pass-{currentPass}", TimeSpan.FromSeconds(45));
                    var assignments = FinalEditAssignments(room);
                    var hostAssignment = assignments.Single(assignment => assignment.EditorPlayerId == host.Id);
                    var nodeAssignment = assignments.Single(assignment => assignment.EditorPlayerId == node.Id);
                    var hostFinalEditSubmissionId = SubmissionId(host, hostAssignment.ArtifactId, $"final-edit-{currentPass}");
                    await UploadFinalEdit(roomCode, host, hostAssignment.ArtifactId, hostDrawing, hostFinalEditSubmissionId);
                    await UploadFinalEdit(roomCode, host, hostAssignment.ArtifactId, hostDrawing, hostFinalEditSubmissionId);
                    if (TryFinalStage(await GetJson($"/api/rooms/{roomCode}"), out var editStageAfterHost, out var passAfterHost) && editStageAfterHost == "CollectingFinalEdits" && passAfterHost == currentPass)
                        await UploadFinalEdit(roomCode, node, nodeAssignment.ArtifactId, nodeDrawing, SubmissionId(node, nodeAssignment.ArtifactId, $"final-edit-{currentPass}"));
                    break;
                case "ShowingFinalPresentation":
                    await WaitForMarker("display-finalround-presentation", TimeSpan.FromSeconds(30));
                    break;
                case "CollectingFinalVotes":
                    await WaitForMarker("display-finalround-voting", TimeSpan.FromSeconds(30));
                    await WaitForMarker($"ios-final-vote-submitted-{room.GetProperty("stateVersion").GetInt64()}", TimeSpan.FromSeconds(30));
                    var finalArtifactIds = FinalArtifactIds(room);
                    var hostFinalVoteSubmissionId = SubmissionId(host, Guid.Empty, "final-vote");
                    await SubmitFinalVote(roomCode, host, finalArtifactIds[0], hostFinalVoteSubmissionId);
                    await SubmitFinalVote(roomCode, host, finalArtifactIds[0], hostFinalVoteSubmissionId);
                    if (TryFinalStage(await GetJson($"/api/rooms/{roomCode}"), out var voteStageAfterHost, out _) && voteStageAfterHost == "CollectingFinalVotes")
                        await SubmitFinalVote(roomCode, node, finalArtifactIds[^1], SubmissionId(node, Guid.Empty, "final-vote"));
                    break;
                case "ShowingFinalResults":
                    await WaitForMarker("display-finalround-results", TimeSpan.FromSeconds(30));
                    break;
            }
            continue;
        }
        if (!hostObservations.TryGetLatestSnapshot(out var hostSnapshot) || !nodeObservations.TryGetLatestSnapshot(out var nodeSnapshot))
        {
            await Task.Delay(100);
            continue;
        }
        var active = Active(hostSnapshot, questionTypes);
        var nodeActive = Active(nodeSnapshot, questionTypes);
        // Snapshot delivery is asynchronous. Record each independently observed
        // active question before requiring both clients to agree on the exact
        // actionable state, so a terminal transition cannot hide a valid round.
        if (active is not null) tracker.ObserveQuestion(active);
        if (nodeActive is not null) tracker.ObserveQuestion(nodeActive);
        if (active is null || nodeActive is null || active.Id != nodeActive.Id || active.Stage != nodeActive.Stage || active.StateVersion != nodeActive.StateVersion)
        {
            await Task.Delay(100);
            continue;
        }
        tracker.ObserveQuestion(active);
        await WriteJson("active-question.json", new { questionId = active.Id, questionType = active.Type, stage = active.Stage, stateVersion = active.StateVersion });
        var key = $"{active.Id}:{active.Stage}";
        if (!actionedStages.Add(key)) { await Task.Delay(100); continue; }

        switch (active.Stage)
        {
            case "CollectingPlayerSelections":
                await WaitForMarker("display-playerselection-collecting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-player-selection-submitted", TimeSpan.FromSeconds(30));
                await hostConnection.InvokeAsync("SubmitPlayerSelectionWithSubmission", roomCode, host.Id, host.Token, node.Id, active.InstanceId, SubmissionId(host, active.InstanceId, "selection"));
                await nodeConnection.InvokeAsync("SubmitPlayerSelectionWithSubmission", roomCode, node.Id, node.Token, host.Id, active.InstanceId, SubmissionId(node, active.InstanceId, "selection"));
                break;
            case "CollectingTextAnswers":
                await WaitForMarker("display-textanswer-collecting", TimeSpan.FromSeconds(30));
                await WaitForAnyMarker(new[] { "ios-text-submitted", "ios-text-subject-observed" }, TimeSpan.FromSeconds(30));
                await hostConnection.InvokeAsync("SubmitTextAnswerWithSubmission", roomCode, host.Id, host.Token, "Odpowiedź hosta", active.InstanceId, SubmissionId(host, active.InstanceId, "text-answer"));
                await nodeConnection.InvokeAsync("SubmitTextAnswerWithSubmission", roomCode, node.Id, node.Token, "Odpowiedź node", active.InstanceId, SubmissionId(node, active.InstanceId, "text-answer"));
                break;
            case "CollectingPhotoAnswers":
                await WaitForMarker("display-photoanswer-collecting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-photo-submitted", TimeSpan.FromSeconds(45));
                if (IsStillCollecting(await GetJson($"/api/rooms/{roomCode}"), active, "CollectingPhotoAnswers"))
                {
                    var hostPhotoSubmissionId = SubmissionId(host, active.InstanceId, "photo-answer");
                    await UploadAnswer(roomCode, host, active.InstanceId, "photo", hostPhoto, "image/jpeg", hostPhotoSubmissionId);
                    await UploadAnswer(roomCode, host, active.InstanceId, "photo", hostPhoto, "image/jpeg", hostPhotoSubmissionId);
                }
                if (IsStillCollecting(await GetJson($"/api/rooms/{roomCode}"), active, "CollectingPhotoAnswers"))
                    await UploadAnswer(roomCode, node, active.InstanceId, "photo", nodePhoto, "image/jpeg", SubmissionId(node, active.InstanceId, "photo-answer"));
                break;
            case "CollectingDrawingAnswers":
                await WaitForMarker("display-drawinganswer-collecting", TimeSpan.FromSeconds(30));
                await WaitForAnyMarker(new[] { "ios-drawing-submitted", "ios-drawing-not-required" }, TimeSpan.FromSeconds(45));
                if (IsStillCollecting(await GetJson($"/api/rooms/{roomCode}"), active, "CollectingDrawingAnswers"))
                    await UploadAnswer(roomCode, host, active.InstanceId, "drawing", hostDrawing, "image/png", SubmissionId(host, active.InstanceId, "drawing-answer"));
                if (IsStillCollecting(await GetJson($"/api/rooms/{roomCode}"), active, "CollectingDrawingAnswers"))
                    await UploadAnswer(roomCode, node, active.InstanceId, "drawing", nodeDrawing, "image/png", SubmissionId(node, active.InstanceId, "drawing-answer"));
                break;
            case "CollectingTextAnswerVotes":
                await WaitForMarker("display-textanswer-voting", TimeSpan.FromSeconds(30));
                await WaitForAnyMarker(new[] { "ios-text-voted", "ios-text-vote-not-required" }, TimeSpan.FromSeconds(30));
                var answers = TextAnswerIds(room);
                if (answers.Count < 2) throw new InvalidOperationException("Głosowanie tekstowe nie ma co najmniej dwóch odpowiedzi.");
                var hostTextVote = answers.First(id => id != hostPrivate.TextAnswerId);
                var hostTextVoteSubmissionId = SubmissionId(host, active.InstanceId, "text-vote");
                await hostConnection.InvokeAsync("SubmitTextAnswerVoteWithSubmission", roomCode, host.Id, host.Token, hostTextVote, active.InstanceId, hostTextVoteSubmissionId);
                await hostConnection.InvokeAsync("SubmitTextAnswerVoteWithSubmission", roomCode, host.Id, host.Token, hostTextVote, active.InstanceId, hostTextVoteSubmissionId);
                await nodeConnection.InvokeAsync("SubmitTextAnswerVoteWithSubmission", roomCode, node.Id, node.Token, answers.First(id => id != nodePrivate.TextAnswerId), active.InstanceId, SubmissionId(node, active.InstanceId, "text-vote"));
                break;
            case "CollectingPhotoAnswerVotes":
                await WaitForMarker("display-photoanswer-voting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-photo-voted", TimeSpan.FromSeconds(30));
                AssertAllMediaSubmitted(room, "photoAnswerResults", "submittedPlayers", "requiredPlayers", "PhotoAnswer");
                var photoAnswerIds = MediaAnswerIds(room, "photoAnswerResults", "photoAnswerId");
                await VoteMedia(hostConnection, "SubmitPhotoAnswerVote", roomCode, host, active.InstanceId, photoAnswerIds[0], SubmissionId(host, active.InstanceId, "photo-vote"));
                await VoteMedia(nodeConnection, "SubmitPhotoAnswerVote", roomCode, node, active.InstanceId, photoAnswerIds[^1], SubmissionId(node, active.InstanceId, "photo-vote"));
                break;
            case "CollectingDrawingAnswerVotes":
                await WaitForMarker("display-drawinganswer-voting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-drawing-voted", TimeSpan.FromSeconds(30));
                AssertAllMediaSubmitted(room, "drawingAnswerResults", "submittedPlayers", "requiredPlayers", "DrawingAnswer");
                var drawingAnswerIds = MediaAnswerIds(room, "drawingAnswerResults", "drawingAnswerId");
                await VoteMedia(hostConnection, "SubmitDrawingAnswerVote", roomCode, host, active.InstanceId, drawingAnswerIds[0], SubmissionId(host, active.InstanceId, "drawing-vote"));
                await VoteMedia(nodeConnection, "SubmitDrawingAnswerVote", roomCode, node, active.InstanceId, drawingAnswerIds[^1], SubmissionId(node, active.InstanceId, "drawing-vote"));
                break;
        }
    }

    var completed = await GetJson($"/api/rooms/{roomCode}");
    ThrowObservationFailure();
    tracker.Observe(completed);
    if (!IsGameCompleted(completed))
        throw new TimeoutException($"Gra nie doszła do Completed przed limitem 7 minut. Ostatnie pytanie: {tracker.LastQuestionType ?? "brak"}, faza: {tracker.LastPhase ?? "brak"}, stateVersion: {tracker.LastStateVersion}.");
    tracker.AssertComplete(completed, Volatile.Read(ref startedEvents));
    ValidateFinalRoundCompleted(completed);
    await WaitForMarker("display-completed", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-completed-observed", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-terminal-snapshot-received", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-completed-rendered", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-ranking-rendered", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-recovered-state", TimeSpan.FromSeconds(30));
    await WaitForMarker("display-reconnected", TimeSpan.FromSeconds(30));
    var ledger = new StateVersionLedgerAggregator().Aggregate(coordinationDir);
    StateVersionLedgerAggregator.Write(coordinationDir, ledger);
    if (!ledger.Passed) throw new InvalidOperationException($"Ledger stateVersion nie przeszedł: {string.Join(", ", ledger.Failures.Select(failure => failure.Code))}.");
    var iosLedger = ledger.Clients["ios"];
    var displayLedger = ledger.Clients["display"];
    var playerALedger = ledger.Clients["scripted-player-a"];
    var playerBLedger = ledger.Clients["scripted-player-b"];
    var backendLedger = ledger.Clients["backend"];
    var iosBefore = iosLedger.VersionBeforeDisconnect ?? throw new InvalidOperationException("Brak wersji iOS sprzed reconnect.");
    var iosRecovered = iosLedger.RecoveredVersion ?? throw new InvalidOperationException("Brak wersji iOS po reconnect.");
    var displayBefore = displayLedger.VersionBeforeDisconnect ?? throw new InvalidOperationException("Brak wersji Display sprzed reconnect.");
    var displayRecovered = displayLedger.RecoveredVersion ?? throw new InvalidOperationException("Brak wersji Display po reconnect.");
    var finalPlayers = completed.GetProperty("players");
    if (finalPlayers.GetArrayLength() != 3 || !finalPlayers.EnumerateArray().Any(player => player.GetProperty("id").GetGuid() == iosPlayerId))
        throw new InvalidOperationException("Reconnect iOS nie odzyskał tego samego gracza w pokoju trzech graczy.");
    var audit = await GetJson($"/api/rooms/{roomCode}/submission-audit?playerId={host.Id}&reconnectToken={Uri.EscapeDataString(host.Token)}");
    await WriteJson("submission-audit.json", audit);
    var auditEntries = audit.GetProperty("entries").EnumerateArray().ToArray();
    var acceptedUniqueSubmissionCount = AuditCount(auditEntries, "Accepted");
    var idempotentReplayCount = AuditCount(auditEntries, "IdempotentReplay");
    var conflictingSubmissionIdCount = AuditCount(auditEntries, "Conflict");
    var duplicateTextAnswerCount = DomainDuplicateCount(auditEntries, "TextAnswer");
    var duplicateTextVoteCount = DomainDuplicateCount(auditEntries, "TextAnswerVote");
    var duplicatePhotoAnswerCount = DomainDuplicateCount(auditEntries, "PhotoAnswer");
    var duplicatePhotoVoteCount = DomainDuplicateCount(auditEntries, "PhotoAnswerVote");
    var duplicateDrawingAnswerCount = DomainDuplicateCount(auditEntries, "DrawingAnswer");
    var duplicateDrawingVoteCount = DomainDuplicateCount(auditEntries, "DrawingAnswerVote");
    var finalSelfieCount = auditEntries.Count(entry => entry.GetProperty("actionType").GetString() == "FinalSelfie" && entry.GetProperty("result").GetString() == "Accepted");
    var finalEditCount = auditEntries.Count(entry => entry.GetProperty("actionType").GetString() == "FinalEdit" && entry.GetProperty("result").GetString() == "Accepted");
    var finalVoteCount = auditEntries.Count(entry => entry.GetProperty("actionType").GetString() == "FinalVote" && entry.GetProperty("result").GetString() == "Accepted");
    var duplicateAnswerCount = duplicateTextAnswerCount + duplicatePhotoAnswerCount + duplicateDrawingAnswerCount;
    var duplicateVoteCount = duplicateTextVoteCount + duplicatePhotoVoteCount + duplicateDrawingVoteCount;
    var duplicateDrawingCount = duplicateDrawingAnswerCount + duplicateDrawingVoteCount;
    var duplicateClientSubmissionIdCount = DuplicateSubmissionIdCount(auditEntries);
    var iosPostReconnectDuplicateSubmissionCount = DomainDuplicateCountForPlayer(auditEntries, iosPlayerId);
    var scriptedPlayerADuplicateSubmissionCount = DomainDuplicateCountForPlayer(auditEntries, host.Id);
    var scriptedPlayerBDuplicateSubmissionCount = DomainDuplicateCountForPlayer(auditEntries, node.Id);
    var displaySubmissionCount = auditEntries.Count(entry =>
    {
        var playerId = entry.GetProperty("playerId").GetGuid();
        return playerId != iosPlayerId && playerId != host.Id && playerId != node.Id;
    });
    if (idempotentReplayCount < 5) throw new InvalidOperationException($"Oczekiwano co najmniej pięciu rzeczywistych replayów, otrzymano {idempotentReplayCount}.");
    if (finalSelfieCount != 3 || finalEditCount != 6 || finalVoteCount != 3)
        throw new InvalidOperationException($"Final Round ma niepełny audyt: selfie={finalSelfieCount}, edits={finalEditCount}, votes={finalVoteCount}.");
    if (conflictingSubmissionIdCount != 0 || duplicateAnswerCount != 0 || duplicateVoteCount != 0 ||
        duplicateClientSubmissionIdCount != 0 || iosPostReconnectDuplicateSubmissionCount != 0 ||
        scriptedPlayerADuplicateSubmissionCount != 0 || scriptedPlayerBDuplicateSubmissionCount != 0 || displaySubmissionCount != 0)
    {
        throw new InvalidOperationException("Audyt submissions wykrył konflikt, duplikat logiczny albo submission Displaya.");
    }
    await WriteJson("outcome.json", new
    {
        status = "passed",
        stage,
        roomCode,
        contentPackageVersionId = packageId,
        roomPhase = "Completed",
        roomStartedEvents = Volatile.Read(ref startedEvents),
        playedQuestionCount = tracker.PlayedQuestionCount,
        uniqueQuestionIdCount = tracker.PlayedQuestionCount,
        playerSelectionCount = tracker.Count("PlayerSelection"),
        textAnswerCount = tracker.Count("TextAnswer"),
        photoAnswerCount = tracker.Count("PhotoAnswer"),
        drawingAnswerCount = tracker.Count("DrawingAnswer"),
        rankingCount = tracker.RankingCount(completed),
        stateVersion = ledger.FinalBackendStateVersion,
        stateVersionMonotonic = ledger.Clients.Values.All(client => client.RegressionCount == 0),
        iosReconnectCount = 1,
        iosSamePlayerRecovered = true,
        iosVersionBeforeDisconnect = iosBefore,
        iosRecoveredVersion = iosRecovered,
        iosVersionRegressionCount = iosLedger.RegressionCount,
        displayReconnectCount = 1,
        displayVersionBeforeDisconnect = displayBefore,
        displayRecoveredVersion = displayRecovered,
        displayVersionRegressionCount = displayLedger.RegressionCount,
        iosObservationCount = iosLedger.ObservationCount,
        displayObservationCount = displayLedger.ObservationCount,
        scriptedPlayerAObservationCount = playerALedger.ObservationCount,
        scriptedPlayerAVersionRegressionCount = playerALedger.RegressionCount,
        scriptedPlayerBObservationCount = playerBLedger.ObservationCount,
        scriptedPlayerBVersionRegressionCount = playerBLedger.RegressionCount,
        backendObservationCount = backendLedger.ObservationCount,
        backendVersionRegressionCount = backendLedger.RegressionCount,
        finalBackendStateVersion = ledger.FinalBackendStateVersion,
        stateVersionLedgerPassed = ledger.Passed,
        stateVersionLedgerFailureCount = ledger.FailureCount,
        totalSubmissionAttemptCount = auditEntries.Length,
        acceptedUniqueSubmissionCount,
        idempotentReplayCount,
        conflictingSubmissionIdCount,
        duplicateTextAnswerCount,
        duplicateTextVoteCount,
        duplicatePhotoAnswerCount,
        duplicatePhotoVoteCount,
        duplicateDrawingAnswerCount,
        duplicateDrawingVoteCount,
        finalSelfieCount,
        finalEditCount,
        finalVoteCount,
        duplicateAnswerCount,
        duplicateVoteCount,
        duplicateDrawingCount,
        duplicateClientSubmissionIdCount,
        iosPostReconnectDuplicateSubmissionCount,
        scriptedPlayerADuplicateSubmissionCount,
        scriptedPlayerBDuplicateSubmissionCount,
        displaySubmissionCount,
        questions = tracker.Questions,
        ios = "completed",
        display = "completed",
        scriptedPlayers = "completed"
    });
    Console.WriteLine($"PASS: complete four-question game in room {roomCode}.");
}
catch (Exception exception)
{
    await WriteJson("outcome.json", new
    {
        status = "failed",
        stage,
        roomStartedEvents = Volatile.Read(ref startedEvents),
        lastQuestionType = tracker.LastQuestionType,
        lastPhase = tracker.LastPhase,
        lastStateVersion = tracker.LastStateVersion,
        error = exception.Message
    });
    Console.Error.WriteLine($"FAIL ({stage}): {exception}");
    Environment.ExitCode = 1;
}

HubConnection Connection() => new HubConnectionBuilder().WithUrl($"{backendUrl}/hubs/game").Build();
async Task<JsonElement> PostJson(string path, object body) { using var response = await http.PostAsJsonAsync(path, body, json); return await ReadSuccess(response); }
async Task<JsonElement> GetJson(string path)
{
    using var response = await http.GetAsync(path);
    var value = await ReadSuccess(response);
    if (path.StartsWith("/api/rooms/", StringComparison.Ordinal) && value.TryGetProperty("stateVersion", out _))
        Observe(backendObservations, value, "snapshot-accepted");
    return value;
}
static async Task<JsonElement> ReadSuccess(HttpResponseMessage response) { var content = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {content}"); return JsonDocument.Parse(content).RootElement.Clone(); }
static bool IsGameCompleted(JsonElement room) =>
    room.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object &&
    game.TryGetProperty("stage", out var stage) && stage.GetString() == "Completed";
static bool TryFinalStage(JsonElement room, out string stage, out int currentPass)
{
    stage = "";
    currentPass = 0;
    if (!room.TryGetProperty("game", out var game) || game.ValueKind == JsonValueKind.Null ||
        !game.TryGetProperty("stage", out var stageValue) || stageValue.ValueKind != JsonValueKind.String)
        return false;
    stage = stageValue.GetString()!;
    if (game.TryGetProperty("finalRound", out var finalRound) && finalRound.ValueKind == JsonValueKind.Object &&
        finalRound.TryGetProperty("currentPass", out var passValue) && passValue.ValueKind == JsonValueKind.Number)
        currentPass = passValue.GetInt32();
    return stage is "CollectingFinalSelfies" or "CollectingFinalEdits" or "ShowingFinalPresentation" or "CollectingFinalVotes" or "ShowingFinalResults";
}
async Task UploadProfile(string roomCode, PlayerAccess player, byte[] image) { using var form = new MultipartFormDataContent(); var content = new ByteArrayContent(image); content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg"); form.Add(content, "file", "profile.jpg"); using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo") { Content = form }; request.Headers.Add("X-Player-Token", player.Token); using var response = await http.SendAsync(request); _ = await ReadSuccess(response); }
async Task UploadAnswer(string roomCode, PlayerAccess player, Guid questionId, string field, byte[] image, string contentType, Guid clientSubmissionId)
{
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(player.Id.ToString()), "playerId");
    form.Add(new StringContent(player.Token), "reconnectToken");
    form.Add(new StringContent(clientSubmissionId.ToString()), "clientSubmissionId");
    var content = new ByteArrayContent(image);
    content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
    form.Add(content, field, $"{field}.{(contentType == "image/png" ? "png" : "jpg")}");
    using var response = await http.PostAsync($"/api/rooms/{roomCode}/questions/{questionId}/{field}-answers", form);
    var responseBody = await response.Content.ReadAsStringAsync();
    if ((int)response.StatusCode == 409 && responseBody.Contains($"{field}_answer_not_active", StringComparison.Ordinal)) return;
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {responseBody}");
}
async Task UploadFinalSelfie(string roomCode, PlayerAccess player, byte[] image, Guid clientSubmissionId)
{
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(player.Id.ToString()), "playerId");
    form.Add(new StringContent(player.Token), "reconnectToken");
    form.Add(new StringContent(clientSubmissionId.ToString()), "clientSubmissionId");
    var content = new ByteArrayContent(image);
    content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
    form.Add(content, "photo", "final-selfie.jpg");
    using var response = await http.PostAsync($"/api/rooms/{roomCode}/final-round/selfies", form);
    _ = await ReadSuccess(response);
}
async Task UploadFinalEdit(string roomCode, PlayerAccess player, Guid artifactId, byte[] image, Guid clientSubmissionId)
{
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(player.Id.ToString()), "playerId");
    form.Add(new StringContent(player.Token), "reconnectToken");
    form.Add(new StringContent(clientSubmissionId.ToString()), "clientSubmissionId");
    var content = new ByteArrayContent(image);
    content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
    form.Add(content, "drawing", "final-edit.png");
    using var response = await http.PostAsync($"/api/rooms/{roomCode}/final-round/artifacts/{artifactId}/edits", form);
    _ = await ReadSuccess(response);
}
async Task SubmitFinalVote(string roomCode, PlayerAccess player, Guid artifactId, Guid clientSubmissionId) =>
    _ = await PostJson($"/api/rooms/{roomCode}/final-round/votes", new { playerId = player.Id, reconnectToken = player.Token, artifactId, clientSubmissionId });
static async Task<byte[]> Jpeg(Color color) { using var image = new Image<Rgba32>(400, 400, color); await using var stream = new MemoryStream(); await image.SaveAsync(stream, new JpegEncoder()); return stream.ToArray(); }
static async Task<byte[]> Png(Color color) { using var image = new Image<Rgba32>(400, 400, color); await using var stream = new MemoryStream(); await image.SaveAsync(stream, new PngEncoder()); return stream.ToArray(); }
async Task WaitForMarker(string name, TimeSpan timeout) => await WaitUntil(() => File.Exists(Path.Combine(coordinationDir, name)), timeout, name);
async Task WaitForAnyMarker(IEnumerable<string> names, TimeSpan timeout) => await WaitUntil(() => names.Any(name => File.Exists(Path.Combine(coordinationDir, name))), timeout, string.Join(" lub ", names));
async Task VoteMedia(HubConnection connection, string method, string roomCode, PlayerAccess voter, Guid questionId, Guid answerId, Guid clientSubmissionId) => await connection.InvokeAsync(method + "WithSubmission", roomCode, voter.Id, voter.Token, questionId, answerId, clientSubmissionId);
Guid SubmissionId(PlayerAccess player, Guid questionId, string action)
{
    var key = $"{player.Id:N}:{questionId:N}:{action}";
    if (submissionIds.TryGetValue(key, out var id)) return id;
    id = Guid.NewGuid(); submissionIds[key] = id; return id;
}
static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, string description) { var deadline = DateTimeOffset.UtcNow + timeout; while (DateTimeOffset.UtcNow < deadline) { if (predicate()) return; await Task.Delay(100); } throw new TimeoutException($"Timeout: {description}"); }
async Task WriteJson(string fileName, object value) { var path = Path.Combine(coordinationDir, fileName); var temporaryPath = path + ".tmp"; await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value, json)); File.Move(temporaryPath, path, true); }
void Mark(string name) => File.WriteAllText(Path.Combine(coordinationDir, name), string.Empty);
void Observe(ClientStateVersionRecorder recorder, JsonElement snapshot, string @event)
{
    try { recorder.Observe(snapshot, @event); }
    catch (Exception exception) { observationFailures.Enqueue(exception); }
}
void ThrowObservationFailure()
{
    if (observationFailures.TryDequeue(out var failure)) throw new InvalidOperationException("Błąd obserwacji stateVersion.", failure);
}
static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Brak wymaganej zmiennej środowiskowej {name}.");
static PlayerAccess Access(JsonElement response, string name) => new(response.GetProperty("playerId").GetGuid(), response.GetProperty("reconnectToken").GetString()!, name);
static PrivateState Private(JsonElement value) => new(ReadGuid(value, "ownTextAnswerId"), ReadGuid(value, "ownPhotoAnswerId"), ReadGuid(value, "ownDrawingAnswerId"));
static Guid? ReadGuid(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id) ? id : null;
static int AuditCount(IEnumerable<JsonElement> entries, string result) => entries.Count(entry => entry.GetProperty("result").GetString() == result);
static int DomainDuplicateCount(IEnumerable<JsonElement> entries, string action) => entries
    .Where(entry => entry.GetProperty("actionType").GetString() == action && entry.GetProperty("result").GetString() == "Accepted")
    .GroupBy(entry => $"{entry.GetProperty("playerId").GetGuid():N}:{entry.GetProperty("questionInstanceId").GetGuid():N}")
    .Sum(group => Math.Max(0, group.Count() - 1));
static int DomainDuplicateCountForPlayer(IEnumerable<JsonElement> entries, Guid playerId) => entries
    // A FinalEdit is intentionally accepted once per pass for a player. Its
    // audit question id is the stable final-session id, so it cannot be
    // grouped with one-pass answer/vote actions here.
    .Where(entry => entry.GetProperty("playerId").GetGuid() == playerId && entry.GetProperty("result").GetString() == "Accepted" && entry.GetProperty("actionType").GetString() != "FinalEdit")
    .GroupBy(entry => $"{entry.GetProperty("actionType").GetString()}:{entry.GetProperty("questionInstanceId").GetGuid():N}")
    .Sum(group => Math.Max(0, group.Count() - 1));
static int DuplicateSubmissionIdCount(IEnumerable<JsonElement> entries) => entries
    .Where(entry => entry.GetProperty("result").GetString() == "Accepted")
    .GroupBy(entry => $"{entry.GetProperty("playerId").GetGuid():N}:{entry.GetProperty("questionInstanceId").GetGuid():N}:{entry.GetProperty("actionType").GetString()}:{entry.GetProperty("clientSubmissionId").GetGuid():N}")
    .Sum(group => Math.Max(0, group.Count() - 1));
static ActiveQuestion? Active(JsonElement room, IReadOnlyDictionary<Guid, string> questionTypes)
{
    if (!room.TryGetProperty("game", out var game) || game.ValueKind == JsonValueKind.Null ||
        !game.TryGetProperty("question", out var question) || question.ValueKind == JsonValueKind.Null)
        return null;
    var questionId = question.GetProperty("id").GetGuid();
    var instanceId = question.TryGetProperty("instanceId", out var instance) && instance.ValueKind == JsonValueKind.String
        ? instance.GetGuid()
        : questionId;
    if (!questionTypes.TryGetValue(questionId, out var questionType))
        throw new InvalidOperationException($"Snapshot wskazuje pytanie {questionId}, którego nie ma w pakiecie 7.2.");
    var phase = game.GetProperty("stage").GetString()!;
    var phaseType = phase switch
    {
        "CollectingPlayerSelections" or "ShowingQuestionResults" => "PlayerSelection",
        "CollectingTextAnswers" or "RevealingTextAnswers" or "CollectingTextAnswerVotes" or "ShowingTextAnswerResults" => "TextAnswer",
        "CollectingPhotoAnswers" or "RevealingPhotoAnswers" or "CollectingPhotoAnswerVotes" or "ShowingPhotoAnswerResults" => "PhotoAnswer",
        "CollectingDrawingAnswers" or "RevealingDrawingAnswers" or "CollectingDrawingAnswerVotes" or "ShowingDrawingAnswerResults" => "DrawingAnswer",
        _ => null
    };
    if (phaseType is not null && phaseType != questionType)
        throw new InvalidOperationException($"Faza {phase} nie odpowiada typowi {questionType} pytania {questionId}.");
    return new ActiveQuestion(
        questionId, instanceId,
        questionType,
        phase,
        game.GetProperty("currentQuestionNumber").GetInt32(),
        room.GetProperty("stateVersion").GetInt64());
}
static bool IsStillCollecting(JsonElement room, ActiveQuestion active, string stage) =>
    room.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null &&
    game.GetProperty("stage").GetString() == stage &&
    game.GetProperty("question").GetProperty("id").GetGuid() == active.Id;
static List<Guid> MediaAnswerIds(JsonElement room, string resultsProperty, string answerIdProperty)
{
    var ids = room.GetProperty("game").GetProperty(resultsProperty).GetProperty("anonymousOptions")
        .EnumerateArray().Select(option => option.GetProperty(answerIdProperty).GetGuid()).ToList();
    if (ids.Count == 0) throw new InvalidOperationException($"Brak anonimowych opcji dla {resultsProperty}.");
    return ids;
}
static List<Guid> TextAnswerIds(JsonElement room) => room.GetProperty("game").GetProperty("textResults").GetProperty("votingOptions").EnumerateArray().Select(item => item.GetProperty("answerId").GetGuid()).ToList();
static List<FinalEditAssignment> FinalEditAssignments(JsonElement room) => room.GetProperty("game").GetProperty("finalRound").GetProperty("editAssignments")
    .EnumerateArray().Select(item => new FinalEditAssignment(item.GetProperty("artifactId").GetGuid(), item.GetProperty("editorPlayerId").GetGuid())).ToList();
static List<Guid> FinalArtifactIds(JsonElement room) => room.GetProperty("game").GetProperty("finalRound").GetProperty("artifacts")
    .EnumerateArray().Select(item => item.GetProperty("artifactId").GetGuid()).ToList();
static void AssertAllMediaSubmitted(JsonElement room, string resultsProperty, string submittedProperty, string requiredProperty, string questionType)
{
    var results = room.GetProperty("game").GetProperty(resultsProperty);
    var submitted = results.GetProperty(submittedProperty).GetInt32();
    var required = results.GetProperty(requiredProperty).GetInt32();
    if (submitted != required || required < 1)
        throw new InvalidOperationException($"{questionType}: przyjęto {submitted} z {required} wymaganych odpowiedzi.");
}
static void ValidateStarted(JsonElement room, Guid packageId) { if (room.GetProperty("phase").GetString() != "Started") throw new InvalidOperationException("Pokój nie przeszedł do Started."); if (room.GetProperty("contentPackageVersionId").GetGuid() != packageId) throw new InvalidOperationException("Pokój zmienił wersję pakietu."); if (room.GetProperty("startedAtUtc").ValueKind == JsonValueKind.Null) throw new InvalidOperationException("Brakuje startedAtUtc."); if (room.GetProperty("players").EnumerateArray().Any(player => !player.GetProperty("isReady").GetBoolean())) throw new InvalidOperationException("Gra wystartowała przed Ready wszystkich graczy."); }
static void ValidateFinalRoundCompleted(JsonElement room)
{
    var final = room.GetProperty("game").GetProperty("finalRound");
    if (final.GetProperty("artifacts").GetArrayLength() != 3 || final.GetProperty("submittedSelfies").GetInt32() != 3 ||
        final.GetProperty("submittedVotes").GetInt32() != 3 || final.GetProperty("artifacts").EnumerateArray().Any(artifact => artifact.GetProperty("displayMediaUrl").ValueKind == JsonValueKind.Null))
        throw new InvalidOperationException("Końcowy snapshot Final Round nie zawiera trzech kompletnych artefaktów i głosów.");
}

internal sealed class GameTracker
{
    private readonly Dictionary<Guid, string> played = [];
    private readonly Dictionary<Guid, int> questionNumbers = [];
    private readonly Dictionary<int, Guid> questionIdsByNumber = [];
    public long LastStateVersion { get; private set; } = -1;
    public string? LastQuestionType { get; private set; }
    public string? LastPhase { get; private set; }
    public int PlayedQuestionCount => played.Count;
    public IReadOnlyList<object> Questions => played.Select(pair => (object)new { questionId = pair.Key, questionType = pair.Value }).ToList();
    public void Observe(JsonElement room) { var version = room.GetProperty("stateVersion").GetInt64(); if (version < LastStateVersion) throw new InvalidOperationException($"stateVersion cofnął się z {LastStateVersion} do {version}."); LastStateVersion = version; }
    public void ObserveQuestion(ActiveQuestion question)
    {
        if (played.TryGetValue(question.Id, out var knownType) && knownType != question.Type)
            throw new InvalidOperationException($"Pytanie {question.Id} zmieniło typ z {knownType} na {question.Type}.");
        if (questionNumbers.TryGetValue(question.Id, out var knownNumber) && knownNumber != question.Number)
            throw new InvalidOperationException($"questionId {question.Id} został ponownie użyty jako pytanie {question.Number}; wcześniej miał numer {knownNumber}.");
        if (questionIdsByNumber.TryGetValue(question.Number, out var knownId) && knownId != question.Id)
            throw new InvalidOperationException($"Numer pytania {question.Number} zmienił questionId z {knownId} na {question.Id}.");
        played[question.Id] = question.Type;
        questionNumbers[question.Id] = question.Number;
        questionIdsByNumber[question.Number] = question.Id;
        LastQuestionType = question.Type;
        LastPhase = question.Stage;
    }
    public int Count(string type) => played.Values.Count(value => value == type);
    public int RankingCount(JsonElement room) => room.GetProperty("game").GetProperty("ranking").GetArrayLength();
    public void AssertComplete(JsonElement room, int roomStartedEvents) { if (!room.TryGetProperty("game", out var game) || game.GetProperty("stage").GetString() != "Completed") throw new InvalidOperationException("Gra nie doszła do Completed."); if (roomStartedEvents != 1) throw new InvalidOperationException($"RoomStarted wystąpił {roomStartedEvents} razy."); if (played.Count != 4) throw new InvalidOperationException($"Rozegrano {played.Count} pytań zamiast 4."); var expected = new[] { "PlayerSelection", "TextAnswer", "PhotoAnswer", "DrawingAnswer" }; if (expected.Any(type => played.Values.Count(value => value == type) != 1)) throw new InvalidOperationException("Pakiet nie rozegrał dokładnie po jednym pytaniu każdego typu."); var rankings = room.GetProperty("game").GetProperty("ranking"); if (rankings.GetArrayLength() != room.GetProperty("players").GetArrayLength()) throw new InvalidOperationException("Końcowy ranking nie zawiera wszystkich graczy."); }
}

internal sealed record PlayerAccess(Guid Id, string Token, string Name);
internal sealed record PrivateState(Guid? TextAnswerId = null, Guid? PhotoAnswerId = null, Guid? DrawingAnswerId = null);
internal sealed record ActiveQuestion(Guid Id, Guid InstanceId, string Type, string Stage, int Number, long StateVersion);
internal sealed record FinalEditAssignment(Guid ArtifactId, Guid EditorPlayerId);
internal sealed record QuestionDefinition(string Key, string Type, string TextPl, string TextEn, int SortOrder);
