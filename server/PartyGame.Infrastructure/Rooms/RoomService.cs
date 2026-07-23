using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Content;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Rooms;

public sealed class RoomService(
    PartyGameDbContext dbContext,
    IRoomCodeGenerator roomCodeGenerator,
    IPlayerSessionService playerSessionService,
    RoomLockProvider lockProvider,
    ContentPackageLockProvider packageLocks,
    IGameClock clock,
    GamePlanner gamePlanner,
    GameStateMachine stateMachine,
    IMediaStorage mediaStorage,
    IProfilePhotoCleanupService profilePhotoCleanup,
    ILogger<RoomService> logger) : IRoomService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public async Task<RoomCreatedResult> CreateAsync(string? nickname, RoomSettings? settings, List<string>? selectedPackageKeys, List<string>? enabledQuestionTypes, Guid? contentPackageVersionId = null, CancellationToken cancellationToken = default)
    {
        var validNickname = Nickname.ValidateAndTrim(nickname);
        settings ??= new RoomSettings();
        settings.Validate();

        GamePackage? targetPackage;
        if (contentPackageVersionId.HasValue)
        {
            targetPackage = await CreateRoomPackageBindingAsync(contentPackageVersionId.Value, cancellationToken);
        }
        else
        {
            // Preserve compatibility with old requests: choose a currently published
            // default and then bind it under the same version lock used by archive.
            var defaultPackage = await dbContext.GamePackages
                .FirstOrDefaultAsync(p => p.Status == ContentPackageStatus.Published && p.IsDefault, cancellationToken)
                ?? await dbContext.GamePackages.FirstOrDefaultAsync(p => p.Status == ContentPackageStatus.Published, cancellationToken);

            if (defaultPackage is null)
            {
                throw new DomainValidationException(new Dictionary<string, string[]> { ["contentPackageVersionId"] = ["Brak dostępnego opublikowanego pakietu pytań."] });
            }

            targetPackage = await CreateRoomPackageBindingAsync(defaultPackage.Id, cancellationToken);
        }

        var finalPackageKeys = selectedPackageKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList() ?? [];
        if (finalPackageKeys.Count == 0)
        {
            finalPackageKeys = [targetPackage.Key];
        }

        var finalQuestionTypes = new List<QuestionType>();
        if (enabledQuestionTypes == null)
        {
            finalQuestionTypes.Add(QuestionType.PlayerSelection);
        }
        else if (enabledQuestionTypes.Count == 0)
        {
            throw new DomainValidationException(new Dictionary<string, string[]> { ["enabledQuestionTypes"] = ["Enabled question types cannot be empty."] });
        }
        else
        {
            foreach (var qtStr in enabledQuestionTypes)
            {
                if (Enum.TryParse<QuestionType>(qtStr, false, out var parsedQt) && Enum.IsDefined(parsedQt))
                {
                    if (!finalQuestionTypes.Contains(parsedQt))
                        finalQuestionTypes.Add(parsedQt);
                }
                else
                {
                    throw new DomainValidationException(new Dictionary<string, string[]> { ["enabledQuestionTypes"] = [$"Unknown question type: {qtStr}."] });
                }
            }
            finalQuestionTypes.Sort();
        }

        var packageBindingLock = packageLocks.ForVersion(targetPackage.Id);
        await packageBindingLock.WaitAsync(cancellationToken);
        try
        {
            targetPackage = await dbContext.GamePackages.FirstOrDefaultAsync(p => p.Id == targetPackage.Id, cancellationToken);
            if (targetPackage is null || targetPackage.Status != ContentPackageStatus.Published)
            {
                throw new DomainValidationException(new Dictionary<string, string[]> { ["contentPackageVersionId"] = ["Wskazany pakiet nie istnieje lub nie jest opublikowany (Draft/Archived nie mogą być użyte do gry)."] });
            }

            var createLock = lockProvider.For("__room_creation__");
            await createLock.WaitAsync(cancellationToken);
            try
            {
                var code = await roomCodeGenerator.GenerateAsync(
                    async (candidate, token) => !await dbContext.GameRooms.AnyAsync(room => room.Code == candidate, token),
                    cancellationToken);
                var now = clock.UtcNow;
                var roomId = Guid.NewGuid();
                var player = CreatePlayer(roomId, validNickname, true, now, out var rawToken);
                var room = new GameRoom
                {
                    Id = roomId,
                    Code = code,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    HostPlayerId = player.Id,
                    Settings = settings,
                    ContentPackageVersionId = targetPackage.Id,
                    SelectedPackageKeys = finalPackageKeys,
                    EnabledQuestionTypes = finalQuestionTypes,
                    Players = [player]
                };
                settings.GameRoomId = roomId;
                dbContext.GameRooms.Add(room);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Room {RoomCode} created by player {PlayerId}", code, player.Id);
                return new RoomCreatedResult(room, player, rawToken);
            }
            finally
            {
                createLock.Release();
            }
        }
        finally
        {
            packageBindingLock.Release();
        }
    }

    private async Task<GamePackage> CreateRoomPackageBindingAsync(Guid packageVersionId, CancellationToken cancellationToken)
    {
        var packageLock = packageLocks.ForVersion(packageVersionId);
        await packageLock.WaitAsync(cancellationToken);
        try
        {
            var package = await dbContext.GamePackages
                .FirstOrDefaultAsync(p => p.Id == packageVersionId, cancellationToken);
            if (package is null || package.Status != ContentPackageStatus.Published)
            {
                throw new DomainValidationException(new Dictionary<string, string[]>
                {
                    ["contentPackageVersionId"] = ["Wskazany pakiet nie istnieje lub nie jest opublikowany (Draft/Archived nie mogą być użyte do gry)."]
                });
            }

            return package;
        }
        finally
        {
            packageLock.Release();
        }
    }

    public async Task<RoomCreatedResult> JoinAsync(string roomCode, string? nickname, CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(roomCode);
        var validNickname = Nickname.ValidateAndTrim(nickname);
        var roomLock = lockProvider.For(code);
        await roomLock.WaitAsync(cancellationToken);
        try
        {
            var room = await LoadAsync(code, cancellationToken);
            if (room.Phase != RoomPhase.Lobby)
            {
                throw new RoomConflictException("Players cannot join after the room has started.");
            }
            if (room.Players.Count >= GameRoom.MaximumPlayers)
            {
                throw new RoomConflictException("The room already contains the maximum number of players.");
            }

            var normalizedNickname = Nickname.Normalize(validNickname);
            if (room.Players.Any(player => player.NormalizedNickname == normalizedNickname))
            {
                throw new RoomConflictException("This nickname is already in use in the room.");
            }

            var now = clock.UtcNow;
            var player = CreatePlayer(room.Id, validNickname, false, now, out var rawToken);
            room.Players.Add(player);
            dbContext.Players.Add(player);
            room.PublicStateChanged(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Player {PlayerId} joined room {RoomCode}", player.Id, code);
            return new RoomCreatedResult(room, player, rawToken);
        }
        finally
        {
            roomLock.Release();
        }
    }

    public Task<GameRoom> GetAsync(string roomCode, CancellationToken cancellationToken = default) =>
        LoadAsync(NormalizeCode(roomCode), cancellationToken);

    public async Task<PlayerAuthorizationResult> ResumeAsync(string roomCode, Guid playerId, string? token, CancellationToken cancellationToken = default)
    {
        var room = await LoadAsync(NormalizeCode(roomCode), cancellationToken);
        var player = Authorize(room, playerId, token);
        return new PlayerAuthorizationResult(room, player);
    }

    public Task<RoomMutationResult> AttachPlayerAsync(string roomCode, Guid playerId, string? token, CancellationToken cancellationToken = default) =>
        MutateAuthorizedAsync(roomCode, playerId, token, (room, player, now) =>
        {
            var changed = !player.IsConnected;
            player.IsConnected = true;
            player.LastSeenAtUtc = now;
            return changed;
        }, cancellationToken);

    public Task<RoomMutationResult> AttachDisplayAsync(string roomCode, CancellationToken cancellationToken = default) =>
        MutateAsync(roomCode, (room, now) =>
        {
            var changed = !room.DisplayConnected;
            room.DisplayConnected = true;

            if (room.Session != null && room.Session.Stage == GameStage.PausedForDisplay && room.Session.PausedStage != null)
            {
                room.Session.Stage = room.Session.PausedStage.Value;
                if (room.Session.PausedRemainingMilliseconds != null)
                {
                    room.Session.StageEndsAtUtc = now.AddMilliseconds(room.Session.PausedRemainingMilliseconds.Value);
                }
                else
                {
                    room.Session.StageEndsAtUtc = null;
                }
                room.Session.PausedAtUtc = null;
                room.Session.PausedStage = null;
                room.Session.PausedRemainingMilliseconds = null;
                changed = true;
            }

            return changed;
        }, cancellationToken);

    public Task<RoomMutationResult> SetReadyAsync(string roomCode, Guid playerId, string? token, bool isReady, CancellationToken cancellationToken = default) =>
        MutateAuthorizedAsync(roomCode, playerId, token, (room, player, _) =>
        {
            if (room.Phase != RoomPhase.Lobby)
            {
                throw new RoomConflictException("Ready can only be changed while the room is in the lobby.");
            }
            if (isReady && !player.HasProfilePhoto)
            {
                throw new RoomConflictException("A profile photo is required before becoming ready.");
            }
            var changed = player.IsReady != isReady;
            player.IsReady = isReady;
            return changed;
        }, cancellationToken);

    public async Task<RoomMutationResult> SetProfilePhotoAsync(string roomCode, Guid playerId, string? token, Guid mediaAssetId, StoredMediaResult storedMedia, CancellationToken cancellationToken = default)
    {
        // The upload endpoint authorizes before it writes the file. A concurrent
        // replacement can finish during that write, so discard that authorization
        // read before entering the room lock and loading the authoritative state.
        dbContext.ChangeTracker.Clear();
        Guid? previousAssetId = null;
        var result = await MutateAuthorizedAsync(roomCode, playerId, token, (room, player, now) =>
        {
            previousAssetId = player.ProfilePhotoMediaAssetId;
            var asset = new MediaAsset
            {
                Id = mediaAssetId,
                MediaKind = MediaKind.ProfilePhoto,
                StorageProvider = "LocalFileSystem",
                RoomId = room.Id,
                PlayerId = player.Id,
                DisplayStorageKey = storedMedia.DisplayStorageKey,
                ThumbnailStorageKey = storedMedia.ThumbnailStorageKey,
                ContentType = storedMedia.ContentType,
                Width = storedMedia.Width,
                Height = storedMedia.Height,
                ByteLength = storedMedia.ByteLength,
                Sha256 = storedMedia.Sha256,
                CreatedAtUtc = now
            };
            dbContext.MediaAssets.Add(asset);
            player.ProfilePhotoMediaAssetId = asset.Id;
            player.ProfilePhotoStorageKey = null;
            player.ProfilePhotoContentType = storedMedia.ContentType;
            player.HasProfilePhoto = true;
            return true;
        }, cancellationToken);

        if (previousAssetId is { } oldAssetId && oldAssetId != mediaAssetId)
        {
            try
            {
                await profilePhotoCleanup.CleanupAsync(oldAssetId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Profile photo cleanup failed after committing replacement of media asset {MediaAssetId}; error type {ErrorType}",
                    oldAssetId,
                    exception.GetType().Name);
            }
        }

        return result;
    }

    public Task<RoomMutationResult> DisconnectPlayerAsync(string roomCode, Guid playerId, CancellationToken cancellationToken = default) =>
        MutateAsync(roomCode, (room, now) =>
        {
            var player = room.Players.SingleOrDefault(candidate => candidate.Id == playerId);
            if (player is null || !player.IsConnected)
            {
                return false;
            }
            player.IsConnected = false;
            player.LastSeenAtUtc = now;
            return true;
        }, cancellationToken);

    public Task<RoomMutationResult> DisconnectDisplayAsync(string roomCode, CancellationToken cancellationToken = default) =>
        MutateAsync(roomCode, (room, now) =>
        {
            var changed = room.DisplayConnected;
            room.DisplayConnected = false;

            if (room.Session != null && room.Session.Stage != GameStage.PausedForDisplay && room.Session.Stage != GameStage.Completed)
            {
                room.Session.PausedStage = room.Session.Stage;
                if (room.Session.StageEndsAtUtc != null)
                {
                    room.Session.PausedRemainingMilliseconds = (room.Session.StageEndsAtUtc.Value - now).TotalMilliseconds;
                }
                else
                {
                    room.Session.PausedRemainingMilliseconds = null;
                }
                room.Session.PausedAtUtc = now;
                room.Session.Stage = GameStage.PausedForDisplay;
                room.Session.StageEndsAtUtc = null; // Infinite until resumed
                changed = true;
            }

            return changed;
        }, cancellationToken);

    public Task<RoomMutationResult> SubmitSelectionAsync(string roomCode, Guid playerId, string? token, Guid selectedPlayerId, CancellationToken cancellationToken = default) =>
        MutateAuthorizedAsync(roomCode, playerId, token, async (room, player, now) =>
        {
            if (room.Session == null || room.Session.Stage != GameStage.CollectingPlayerSelections)
            {
                return false;
            }

            var currentInstanceId = room.Session.CurrentQuestionInstanceId;
            if (currentInstanceId == null) return false;

            var currentInstance = dbContext.GameQuestionInstances
                .Include(i => i.EligiblePlayers)
                .Include(i => i.Answers)
                .FirstOrDefault(i => i.Id == currentInstanceId);

            if (currentInstance == null) return false;

            if (!currentInstance.EligiblePlayers.Any(e => e.PlayerId == player.Id))
            {
                return false;
            }

            if (currentInstance.Answers.Any(a => a.VoterPlayerId == player.Id))
            {
                return false; // Already voted
            }

            var selectedPlayer = room.Players.FirstOrDefault(p => p.Id == selectedPlayerId);
            if (selectedPlayer == null) return false;

            var newAnswer = new PlayerSelectionAnswer
            {
                Id = Guid.NewGuid(),
                QuestionInstanceId = currentInstance.Id,
                VoterPlayerId = player.Id,
                SelectedPlayerId = selectedPlayer.Id,
                SubmittedAtUtc = now
            };
            dbContext.PlayerSelectionAnswers.Add(newAnswer);

            // Re-eval answer count to see if we should fast-forward
            var currentAnswersCount = currentInstance.Answers.Count;
            var expectedAnswersCount = currentInstance.EligiblePlayers.Count;

            if (currentAnswersCount >= expectedAnswersCount)
            {
                // Last vote ends stage immediately and calculates score synchronously
                await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
            }

            return true;
        }, cancellationToken);

    public Task<RoomMutationResult> SubmitTextAnswerAsync(string roomCode, Guid playerId, string? token, string text, CancellationToken cancellationToken = default) =>
        MutateAuthorizedAsync(roomCode, playerId, token, async (room, player, now) =>
        {
            if (room.Session == null || room.Session.Stage != GameStage.CollectingTextAnswers) return false;
            var currentInstanceId = room.Session.CurrentQuestionInstanceId;
            if (currentInstanceId == null) return false;
            var currentInstance = dbContext.GameQuestionInstances
                .Include(i => i.TextAnswerEligiblePlayers)
                .Include(i => i.TextAnswerSubmissions)
                .FirstOrDefault(i => i.Id == currentInstanceId);
            if (currentInstance == null) return false;
            if (!currentInstance.TextAnswerEligiblePlayers.Any(e => e.PlayerId == player.Id)) return false;
            if (currentInstance.TextAnswerSubmissions.Any(a => a.AuthorPlayerId == player.Id)) return false; // Already submitted

            var cleanText = (text ?? "").Trim();
            if (string.IsNullOrEmpty(cleanText)) return false;

            var stringInfo = new System.Globalization.StringInfo(cleanText);
            if (stringInfo.LengthInTextElements > 150)
            {
                cleanText = stringInfo.SubstringByTextElements(0, 150);
            }

            var submission = new PartyGame.Domain.Game.TextAnswerSubmission
            {
                Id = Guid.NewGuid(),
                QuestionInstanceId = currentInstance.Id,
                AuthorPlayerId = player.Id,
                Text = cleanText,
                SubmittedAtUtc = now
            };
            dbContext.TextAnswerSubmissions.Add(submission);

            var currentAnswersCount = currentInstance.TextAnswerSubmissions.Count;
            var expectedAnswersCount = currentInstance.TextAnswerEligiblePlayers.Count;

            if (currentAnswersCount >= expectedAnswersCount)
            {
                await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
            }
            return true;
        }, cancellationToken);

    public Task<RoomMutationResult> SubmitTextAnswerVoteAsync(string roomCode, Guid playerId, string? token, Guid selectedAnswerId, CancellationToken cancellationToken = default) =>
        MutateAuthorizedAsync(roomCode, playerId, token, async (room, player, now) =>
        {
            if (room.Session == null || room.Session.Stage != GameStage.CollectingTextAnswerVotes) return false;
            var currentInstanceId = room.Session.CurrentQuestionInstanceId;
            if (currentInstanceId == null) return false;
            var currentInstance = dbContext.GameQuestionInstances
                .Include(i => i.TextAnswerVoteEligiblePlayers)
                .Include(i => i.TextAnswerVotes)
                .Include(i => i.TextAnswerSubmissions)
                .FirstOrDefault(i => i.Id == currentInstanceId);
            if (currentInstance == null) return false;
            if (!currentInstance.TextAnswerVoteEligiblePlayers.Any(e => e.PlayerId == player.Id)) return false;
            if (currentInstance.TextAnswerVotes.Any(a => a.VoterPlayerId == player.Id)) return false; // Already voted

            var targetSubmission = currentInstance.TextAnswerSubmissions.FirstOrDefault(s => s.Id == selectedAnswerId);
            if (targetSubmission == null) return false;
            if (targetSubmission.AuthorPlayerId == player.Id) return false; // Can't vote for your own text answer!

            var newVote = new PartyGame.Domain.Game.TextAnswerVote
            {
                Id = Guid.NewGuid(),
                QuestionInstanceId = currentInstance.Id,
                VoterPlayerId = player.Id,
                SelectedTextAnswerId = targetSubmission.Id,
                SubmittedAtUtc = now
            };
            dbContext.TextAnswerVotes.Add(newVote);

            var currentVotesCount = currentInstance.TextAnswerVotes.Count;
            var expectedVotesCount = currentInstance.TextAnswerVoteEligiblePlayers.Count;

            if (currentVotesCount >= expectedVotesCount)
            {
                await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
            }
            return true;
        }, cancellationToken);

    public async Task<PhotoAnswerUploadResult> SubmitPhotoAnswerAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid clientSubmissionId, Stream content, long byteLength, string contentType, CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(roomCode);
        var roomLock = lockProvider.For(code);
        await roomLock.WaitAsync(cancellationToken);
        StoredMediaResult? stored = null;
        try
        {
            var room = await LoadAsync(code, cancellationToken);
            var player = Authorize(room, playerId, token);
            var now = clock.UtcNow;
            if (room.Session?.CurrentQuestionInstanceId != questionInstanceId)
                throw new PhotoAnswerException("photo_answer_not_active", "Photo answers are not active for this question.");
            var instance = room.Session.Rounds.SelectMany(r => r.Questions).Single(q => q.Id == questionInstanceId);
            var existingByClient = instance.PhotoAnswerSubmissions.FirstOrDefault(s => s.ClientSubmissionId == clientSubmissionId && s.AuthorPlayerId == player.Id);
            if (existingByClient != null) return new PhotoAnswerUploadResult(room, existingByClient.Id, false);
            if (room.Session.Stage != GameStage.CollectingPhotoAnswers)
                throw new PhotoAnswerException("photo_answer_not_active", "Photo answers are not active for this question.");
            if (room.Session.StageEndsAtUtc <= now)
                throw new PhotoAnswerException("photo_answer_time_expired", "The photo answer time has expired.");
            if (instance.Question.Type != QuestionType.PhotoAnswer)
                throw new PhotoAnswerException("photo_answer_not_active", "The current question is not a photo question.");
            if (!instance.PhotoAnswerEligiblePlayers.Any(e => e.PlayerId == player.Id))
                throw new PhotoAnswerException("photo_answer_player_not_eligible", "The player is not eligible to submit a photo.");
            if (instance.PhotoAnswerSubmissions.Any(s => s.AuthorPlayerId == player.Id))
                throw new PhotoAnswerException("photo_answer_already_submitted", "The player has already submitted a photo.");

            var answerId = Guid.NewGuid();
            try
            {
                stored = await mediaStorage.SavePhotoAsync(new PhotoMediaWriteRequest(room.Id, questionInstanceId, answerId, content, byteLength, contentType), cancellationToken);
            }
            catch (PhotoMediaException exception)
            {
                throw new PhotoAnswerException(exception.Code, exception.Message);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Photo media write failed for room {RoomCode} and answer {PhotoAnswerId}", code, answerId);
                throw new PhotoAnswerException("photo_answer_storage_failed", "The photo could not be stored.");
            }

            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                MediaKind = MediaKind.PhotoAnswer,
                StorageProvider = "LocalFileSystem",
                RoomId = room.Id,
                PlayerId = player.Id,
                QuestionInstanceId = questionInstanceId,
                DisplayStorageKey = stored.DisplayStorageKey,
                ThumbnailStorageKey = stored.ThumbnailStorageKey,
                ContentType = stored.ContentType,
                Width = stored.Width,
                Height = stored.Height,
                ByteLength = stored.ByteLength,
                Sha256 = stored.Sha256,
                CreatedAtUtc = now
            };
            var submission = new PhotoAnswerSubmission
            {
                Id = answerId,
                QuestionInstanceId = questionInstanceId,
                AuthorPlayerId = player.Id,
                MediaAssetId = asset.Id,
                MediaAsset = asset,
                ClientSubmissionId = clientSubmissionId,
                SubmittedAtUtc = now
            };
            dbContext.MediaAssets.Add(asset);
            dbContext.PhotoAnswerSubmissions.Add(submission);
            if (instance.PhotoAnswerSubmissions.Count >= instance.PhotoAnswerEligiblePlayers.Count)
                await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
            room.PublicStateChanged(now);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await DeleteStoredAsync(stored);
                logger.LogWarning("Photo answer database commit failed for room {RoomCode} and answer {PhotoAnswerId}", code, answerId);
                throw new PhotoAnswerException("photo_answer_storage_failed", "The photo answer could not be committed.");
            }
            return new PhotoAnswerUploadResult(room, answerId, true);
        }
        finally
        {
            roomLock.Release();
        }
    }

    public Task<RoomMutationResult> SubmitPhotoAnswerVoteAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid selectedAnswerId, CancellationToken cancellationToken = default) =>
        MutateAuthorizedAsync(roomCode, playerId, token, async (room, player, now) =>
        {
            if (room.Session?.CurrentQuestionInstanceId != questionInstanceId || room.Session.Stage != GameStage.CollectingPhotoAnswerVotes)
                throw new PhotoAnswerException("photo_answer_vote_not_active", "Photo answer voting is not active.");
            if (room.Session.StageEndsAtUtc <= now)
                throw new PhotoAnswerException("photo_answer_vote_time_expired", "Photo answer voting has expired.");
            var instance = room.Session.Rounds.SelectMany(r => r.Questions).Single(q => q.Id == questionInstanceId);
            if (!instance.PhotoAnswerVoteEligiblePlayers.Any(e => e.PlayerId == player.Id))
                throw new PhotoAnswerException("photo_answer_vote_player_not_eligible", "The player is not eligible to vote.");
            if (instance.PhotoAnswerVotes.Any(v => v.VoterPlayerId == player.Id))
                throw new PhotoAnswerException("photo_answer_vote_already_submitted", "The player has already voted.");
            if (!instance.PhotoAnswerSubmissions.Any(s => s.Id == selectedAnswerId))
                throw new PhotoAnswerException("photo_answer_not_found", "The selected photo answer was not found.");
            var vote = new PhotoAnswerVote { Id = Guid.NewGuid(), QuestionInstanceId = questionInstanceId, VoterPlayerId = player.Id, SelectedPhotoAnswerId = selectedAnswerId, SubmittedAtUtc = now };
            dbContext.PhotoAnswerVotes.Add(vote);
            if (instance.PhotoAnswerVotes.Count >= instance.PhotoAnswerVoteEligiblePlayers.Count)
                await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<DrawingAnswerUploadResult> SubmitDrawingAnswerAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid clientSubmissionId, Stream content, long byteLength, string contentType, CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(roomCode);
        var roomLock = lockProvider.For(code);
        await roomLock.WaitAsync(cancellationToken);
        StoredMediaResult? stored = null;
        try
        {
            var room = await LoadAsync(code, cancellationToken);
            var player = Authorize(room, playerId, token);
            var now = clock.UtcNow;
            if (room.Session?.CurrentQuestionInstanceId != questionInstanceId)
                throw new DrawingAnswerException("drawing_answer_not_active", "Drawing answers are not active for this question.");
            var instance = room.Session.Rounds.SelectMany(r => r.Questions).Single(q => q.Id == questionInstanceId);
            var retry = instance.DrawingAnswerSubmissions.FirstOrDefault(s => s.ClientSubmissionId == clientSubmissionId && s.AuthorPlayerId == player.Id);
            if (retry != null) return new DrawingAnswerUploadResult(room, retry.Id, false);
            if (room.Session.Stage != GameStage.CollectingDrawingAnswers)
                throw new DrawingAnswerException("drawing_answer_not_active", "Drawing answers are not active.");
            if (room.Session.StageEndsAtUtc <= now)
                throw new DrawingAnswerException("drawing_answer_time_expired", "Drawing answer time has expired.");
            if (instance.Question.Type != QuestionType.DrawingAnswer)
                throw new DrawingAnswerException("drawing_answer_not_active", "The current question is not a drawing question.");
            if (!instance.DrawingAnswerEligiblePlayers.Any(e => e.PlayerId == player.Id))
                throw new DrawingAnswerException("drawing_answer_player_not_eligible", "The player is not eligible.");
            if (instance.DrawingAnswerSubmissions.Any(s => s.AuthorPlayerId == player.Id))
                throw new DrawingAnswerException("drawing_answer_already_submitted", "The player has already submitted a drawing.");
            var answerId = Guid.NewGuid();
            try
            {
                stored = await mediaStorage.SaveDrawingAsync(new DrawingMediaWriteRequest(room.Id, questionInstanceId, answerId, content, byteLength, contentType), cancellationToken);
            }
            catch (PhotoMediaException exception)
            {
                throw new DrawingAnswerException(exception.Code, exception.Message);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Drawing media write failed for room {RoomCode} and answer {DrawingAnswerId}", code, answerId);
                throw new DrawingAnswerException("drawing_answer_storage_failed", "The drawing could not be stored.");
            }
            var asset = new MediaAsset { Id = Guid.NewGuid(), MediaKind = MediaKind.DrawingAnswer, StorageProvider = "LocalFileSystem", RoomId = room.Id, PlayerId = player.Id, QuestionInstanceId = questionInstanceId, DisplayStorageKey = stored.DisplayStorageKey, ThumbnailStorageKey = stored.ThumbnailStorageKey, ContentType = stored.ContentType, Width = stored.Width, Height = stored.Height, ByteLength = stored.ByteLength, Sha256 = stored.Sha256, CreatedAtUtc = now };
            dbContext.MediaAssets.Add(asset); dbContext.DrawingAnswerSubmissions.Add(new DrawingAnswerSubmission { Id = answerId, QuestionInstanceId = questionInstanceId, AuthorPlayerId = player.Id, MediaAssetId = asset.Id, MediaAsset = asset, ClientSubmissionId = clientSubmissionId, SubmittedAtUtc = now });
            if (instance.DrawingAnswerSubmissions.Count >= instance.DrawingAnswerEligiblePlayers.Count) await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
            room.PublicStateChanged(now);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await DeleteStoredAsync(stored);
                throw new DrawingAnswerException("drawing_answer_storage_failed", "The drawing could not be committed.");
            }
            return new DrawingAnswerUploadResult(room, answerId, true);
        }
        finally { roomLock.Release(); }
    }

    public Task<RoomMutationResult> SubmitDrawingAnswerVoteAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid selectedAnswerId, CancellationToken cancellationToken = default) => MutateAuthorizedAsync(roomCode, playerId, token, async (room, player, now) =>
    {
        if (room.Session?.CurrentQuestionInstanceId != questionInstanceId || room.Session.Stage != GameStage.CollectingDrawingAnswerVotes) throw new DrawingAnswerException("drawing_answer_vote_not_active", "Drawing answer voting is not active.");
        if (room.Session.StageEndsAtUtc <= now) throw new DrawingAnswerException("drawing_answer_vote_time_expired", "Drawing answer voting has expired.");
        var instance = room.Session.Rounds.SelectMany(r => r.Questions).Single(q => q.Id == questionInstanceId);
        if (!instance.DrawingAnswerVoteEligiblePlayers.Any(e => e.PlayerId == player.Id)) throw new DrawingAnswerException("drawing_answer_vote_player_not_eligible", "The player is not eligible to vote.");
        if (instance.DrawingAnswerVotes.Any(v => v.VoterPlayerId == player.Id)) throw new DrawingAnswerException("drawing_answer_vote_already_submitted", "The player has already voted.");
        if (!instance.DrawingAnswerSubmissions.Any(s => s.Id == selectedAnswerId)) throw new DrawingAnswerException("drawing_answer_not_found", "The selected drawing was not found.");
        dbContext.DrawingAnswerVotes.Add(new DrawingAnswerVote { Id = Guid.NewGuid(), QuestionInstanceId = questionInstanceId, VoterPlayerId = player.Id, SelectedDrawingAnswerId = selectedAnswerId, SubmittedAtUtc = now });
        if (instance.DrawingAnswerVotes.Count >= instance.DrawingAnswerVoteEligiblePlayers.Count) await stateMachine.ForceTransitionAsync(room.Session, now, cancellationToken);
        return true;
    }, cancellationToken);

    private async Task DeleteStoredAsync(StoredMediaResult stored)
    {
        try
        {
            await mediaStorage.DeleteAsync(stored.DisplayStorageKey, CancellationToken.None);
            await mediaStorage.DeleteAsync(stored.ThumbnailStorageKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Compensating media cleanup failed");
        }
    }

    private async Task<RoomMutationResult> MutateAuthorizedAsync(
        string roomCode,
        Guid playerId,
        string? token,
        Func<GameRoom, Player, DateTimeOffset, Task<bool>> mutation,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(roomCode, async (room, now) => await mutation(room, Authorize(room, playerId, token), now), cancellationToken);
    }

    private async Task<RoomMutationResult> MutateAuthorizedAsync(
        string roomCode,
        Guid playerId,
        string? token,
        Func<GameRoom, Player, DateTimeOffset, bool> mutation,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(roomCode, (room, now) => mutation(room, Authorize(room, playerId, token), now), cancellationToken);
    }

    private async Task<RoomMutationResult> MutateAsync(
        string roomCode,
        Func<GameRoom, DateTimeOffset, bool> mutation,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(roomCode, (room, now) => Task.FromResult(mutation(room, now)), cancellationToken);
    }

    private async Task<RoomMutationResult> MutateAsync(
        string roomCode,
        Func<GameRoom, DateTimeOffset, Task<bool>> mutation,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(roomCode);
        var roomLock = lockProvider.For(code);
        await roomLock.WaitAsync(cancellationToken);
        try
        {
            var room = await LoadAsync(code, cancellationToken);
            var now = clock.UtcNow;
            var changed = await mutation(room, now);
            if (changed)
            {
                room.PublicStateChanged(now);
            }

            bool startedNow = false;
            if (RoomStartEvaluator.CanStart(room))
            {
                if (await gamePlanner.TryCreatePlanAsync(room, now, cancellationToken))
                {
                    room.Phase = RoomPhase.Started;
                    room.StartedAtUtc = now;
                    room.PublicStateChanged(now);
                    startedNow = true;
                }
                else
                {
                    // Failed to plan (not enough content for the current player count/settings)
                    // We must revert players' ready status so they know it failed, or send an error.
                    foreach (var p in room.Players)
                    {
                        p.IsReady = false;
                    }
                    room.PublicStateChanged(now);
                    changed = true; // State changed because we reverted Ready
                }
            }

            if (changed || startedNow)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            if (startedNow)
            {
                logger.LogInformation("Room {RoomCode} started automatically at state version {StateVersion}", room.Code, room.StateVersion);
            }
            return new RoomMutationResult(room, changed || startedNow, startedNow);
        }
        finally
        {
            roomLock.Release();
        }
    }

    private Player CreatePlayer(Guid roomId, string nickname, bool isHost, DateTimeOffset now, out string rawToken)
    {
        rawToken = playerSessionService.GenerateToken();
        var playerId = Guid.NewGuid();
        return new Player
        {
            Id = playerId,
            RoomId = roomId,
            Nickname = nickname,
            NormalizedNickname = Nickname.Normalize(nickname),
            IsHost = isHost,
            JoinedAtUtc = now,
            LastSeenAtUtc = now,
            Session = new PlayerSession
            {
                PlayerId = playerId,
                ReconnectTokenHash = playerSessionService.HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(SessionLifetime)
            }
        };
    }

    private Player Authorize(GameRoom room, Guid playerId, string? token)
    {
        var player = room.Players.SingleOrDefault(candidate => candidate.Id == playerId)
            ?? throw new PlayerNotFoundException();
        if (player.Session.ExpiresAtUtc <= clock.UtcNow || !playerSessionService.VerifyToken(token ?? string.Empty, player.Session.ReconnectTokenHash))
        {
            throw new InvalidPlayerTokenException();
        }
        return player;
    }

    private async Task<GameRoom> LoadAsync(string code, CancellationToken cancellationToken) =>
        await dbContext.GameRooms
            .Include(room => room.Settings)
            .Include(room => room.Players)
                .ThenInclude(player => player.Session)
            .Include(room => room.Session)
                .ThenInclude(s => s!.Rounds)
                    .ThenInclude(r => r.Category)
            .Include(room => room.Session)
                .ThenInclude(s => s!.Rounds)
                    .ThenInclude(r => r.Questions)
                        .ThenInclude(i => i.Question)
            .Include(room => room.Session)
                .ThenInclude(s => s!.Rounds)
                    .ThenInclude(r => r.Questions)
                        .ThenInclude(i => i.Answers)
            .Include(room => room.Session)
                .ThenInclude(s => s!.Rounds)
                    .ThenInclude(r => r.Questions)
                        .ThenInclude(i => i.EligiblePlayers)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.PhotoAnswerEligiblePlayers)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.PhotoAnswerSubmissions).ThenInclude(s => s.MediaAsset)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.PhotoAnswerVoteEligiblePlayers)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.PhotoAnswerVotes)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.DrawingAnswerEligiblePlayers)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.DrawingAnswerSubmissions).ThenInclude(s => s.MediaAsset)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.DrawingAnswerVoteEligiblePlayers)
            .Include(room => room.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(i => i.DrawingAnswerVotes)
            .Include(room => room.Session).ThenInclude(s => s!.ScoreTransactions)
            .SingleOrDefaultAsync(room => room.Code == code, cancellationToken)
        ?? throw new RoomNotFoundException();

    private static string NormalizeCode(string roomCode) => (roomCode ?? string.Empty).Trim().ToUpperInvariant();

    public async Task<PartyGame.Domain.Rooms.PlayerPrivateGameState> GetPlayerPrivateGameStateAsync(string roomCode, Guid playerId, CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(roomCode);
        var room = await dbContext.GameRooms
            .Include(r => r.Session)
                .ThenInclude(s => s!.Rounds)
                    .ThenInclude(r => r.Questions)
                        .ThenInclude(q => q.TextAnswerSubmissions)
            .Include(r => r.Session)
                .ThenInclude(s => s!.Rounds)
                    .ThenInclude(r => r.Questions)
                        .ThenInclude(q => q.TextAnswerVotes)
            .Include(r => r.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.PhotoAnswerSubmissions)
            .Include(r => r.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.PhotoAnswerVotes)
            .Include(r => r.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.DrawingAnswerSubmissions)
            .Include(r => r.Session).ThenInclude(s => s!.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.DrawingAnswerVotes)
            .FirstOrDefaultAsync(r => r.Code == code, cancellationToken);

        if (room == null || room.Session == null) return new PartyGame.Domain.Rooms.PlayerPrivateGameState(playerId, null, false, null, false);

        var currentInstanceId = room.Session.CurrentQuestionInstanceId;
        if (currentInstanceId == null) return new PartyGame.Domain.Rooms.PlayerPrivateGameState(playerId, null, false, null, false);

        var currentInstance = room.Session.Rounds.SelectMany(r => r.Questions).FirstOrDefault(q => q.Id == currentInstanceId);
        if (currentInstance == null) return new PartyGame.Domain.Rooms.PlayerPrivateGameState(playerId, currentInstanceId, false, null, false);

        var hasSubmittedAnswer = currentInstance.TextAnswerSubmissions.Any(s => s.AuthorPlayerId == playerId);
        var ownAnswerId = currentInstance.TextAnswerSubmissions.FirstOrDefault(s => s.AuthorPlayerId == playerId)?.Id;
        var hasSubmittedVote = currentInstance.TextAnswerVotes.Any(v => v.VoterPlayerId == playerId);

        var ownPhoto = currentInstance.PhotoAnswerSubmissions.FirstOrDefault(s => s.AuthorPlayerId == playerId);
        var hasPhotoVote = currentInstance.PhotoAnswerVotes.Any(v => v.VoterPlayerId == playerId);
        var ownDrawing = currentInstance.DrawingAnswerSubmissions.FirstOrDefault(s => s.AuthorPlayerId == playerId);
        var hasDrawingVote = currentInstance.DrawingAnswerVotes.Any(v => v.VoterPlayerId == playerId);
        return new PartyGame.Domain.Rooms.PlayerPrivateGameState(playerId, currentInstanceId, hasSubmittedAnswer, ownAnswerId, hasSubmittedVote, ownPhoto != null, ownPhoto?.Id, hasPhotoVote, ownDrawing != null, ownDrawing?.Id, hasDrawingVote);
    }
}
