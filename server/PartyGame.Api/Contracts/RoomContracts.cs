using PartyGame.Domain.Rooms;

namespace PartyGame.Api.Contracts;

public sealed record CreateRoomRequest(string? Nickname, RoomSettingsRequest? Settings, List<string>? SelectedPackageKeys, List<string>? EnabledQuestionTypes, Guid? ContentPackageVersionId = null);
public sealed record JoinRoomRequest(string? Nickname);
public sealed record PlayAgainRequest(Guid PlayerId, string? ReconnectToken);
public sealed record RoomSettingsRequest(
    int RoundCount = 4,
    int QuestionsPerRound = 5,
    int PlayerSelectionSeconds = 20,
    int TextAnswerSeconds = 40,
    int VotingSeconds = 20,
    int PhotoSeconds = 45,
    int DrawingSeconds = 90,
    int ResultPresentationSeconds = 8,
    bool FinalRoundEnabled = true,
    int FinalDrawingPasses = 3)
{
    public RoomSettings ToDomain() => new()
    {
        RoundCount = RoundCount,
        QuestionsPerRound = QuestionsPerRound,
        PlayerSelectionSeconds = PlayerSelectionSeconds,
        TextAnswerSeconds = TextAnswerSeconds,
        VotingSeconds = VotingSeconds,
        PhotoSeconds = PhotoSeconds,
        DrawingSeconds = DrawingSeconds,
        ResultPresentationSeconds = ResultPresentationSeconds,
        FinalRoundEnabled = FinalRoundEnabled,
        FinalDrawingPasses = FinalDrawingPasses
    };
}

public sealed record RoomAccessResponse(string RoomCode, Guid PlayerId, string ReconnectToken, RoomSnapshot Snapshot, PlayerPrivateGameState PrivateState);
public sealed record ResumePlayerResponse(PublicPlayer Player, RoomSnapshot Snapshot, PlayerPrivateGameState PrivateState);
public sealed record RoomSnapshot(
    string RoomCode,
    RoomPhase Phase,
    long StateVersion,
    bool DisplayConnected,
    int MinimumPlayers,
    int MaximumPlayers,
    bool CanStart,
    PublicRoomSettings Settings,
    IReadOnlyList<PublicPlayer> Players,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    GameSnapshot? Game = null,
    Guid? ContentPackageVersionId = null
);

public sealed record GameSnapshot(
    string Stage,
    int CurrentRoundNumber,
    int TotalRounds,
    int CurrentQuestionNumber,
    int QuestionsInCurrentRound,
    DateTimeOffset? StageEndsAtUtc,
    DateTimeOffset? PausedAtUtc,
    string? PausedStage,
    double? PausedRemainingMilliseconds,
    List<PlayerScoreSnapshot> Scores,
    DateTimeOffset? CompletedAtUtc = null,
    int? TotalPlayedQuestions = null,
    GameCategorySnapshot? Category = null,
    GameQuestionSnapshot? Question = null,
    PlayerSelectionResults? Results = null,
    TextAnswerResults? TextResults = null,
    PhotoAnswerResultsSnapshot? PhotoAnswerResults = null,
    DrawingAnswerResultsSnapshot? DrawingAnswerResults = null,
    RoundSummarySnapshot? RoundSummary = null,
    List<RankingEntry>? Ranking = null,
    List<Guid>? AnsweredPlayerIds = null,
    int? AnsweredPlayers = null,
    int? RequiredPlayers = null,
    List<Guid>? SubmittedDrawingAnswerPlayerIds = null,
    int? SubmittedDrawingAnswers = null,
    int? RequiredDrawingAnswers = null,
    FinalRoundSnapshot? FinalRound = null
);

public sealed record GameCategorySnapshot(Guid Id, LocalizedText Name, LocalizedText Description);
// `Id` remains the content-definition id for clients that identify a question
// in a package.  Transport actions and player-private state use the distinct
// persisted game-question instance id.
public sealed record GameQuestionSnapshot(Guid Id, LocalizedText Text, Guid? InstanceId = null);
public sealed record PlayerSelectionResults(Guid QuestionInstanceId, int AnsweredPlayers, int RequiredPlayers, int MissingPlayers, int HighestVoteCount, List<PlayerSelectionResultOption> Options);
public sealed record PlayerSelectionResultOption(Guid SelectedPlayerId, string SelectedPlayerNickname, string? SelectedPlayerPhotoUrl, int VoteCount, bool IsTopResult, List<ResultVoter> Voters);

public sealed record TextAnswerResults(Guid QuestionInstanceId, int AnsweredPlayers, int RequiredPlayers, int? MissingPlayers = null, int? HighestVoteCount = null, List<TextAnswerOptionResult>? Options = null, List<TextAnswerOptionVoting>? VotingOptions = null, List<Guid>? SubmittedAnswerPlayerIds = null);
public sealed record TextAnswerOptionVoting(Guid AnswerId, string Text, int? DisplayOrder);
public sealed record TextAnswerOptionResult(Guid AnswerId, string Text, Guid AuthorPlayerId, string AuthorPlayerNickname, string? AuthorPlayerPhotoUrl, int VoteCount, bool IsTopResult, List<ResultVoter> Voters);

public sealed record PhotoAnswerResultsSnapshot(
    Guid QuestionInstanceId,
    int SubmittedPlayers,
    int RequiredPlayers,
    int? VotedPlayers,
    int? RequiredVoters,
    int? MissingSubmissionPlayers,
    int? MissingVotePlayers,
    int? HighestVoteCount,
    List<PhotoAnswerResultOption>? Options = null,
    List<AnonymousPhotoAnswer>? AnonymousOptions = null);
public sealed record AnonymousPhotoAnswer(Guid PhotoAnswerId, string DisplayPhotoUrl, string ThumbnailPhotoUrl, int DisplayOrder, int Width, int Height);
public sealed record PhotoAnswerResultOption(Guid PhotoAnswerId, string DisplayPhotoUrl, string ThumbnailPhotoUrl, int Width, int Height, Guid AuthorPlayerId, string AuthorNickname, string? AuthorPhotoUrl, int VoteCount, bool IsTopResult, List<PhotoAnswerResultVoter> Voters);
public sealed record PhotoAnswerResultVoter(Guid PlayerId, string Nickname, string? ProfilePhotoUrl, int PointsAwarded);
public sealed record DrawingAnswerResultsSnapshot(Guid QuestionInstanceId, int SubmittedPlayers, int RequiredPlayers, int? VotedPlayers, int? RequiredVoters, int? MissingSubmissionPlayers, int? MissingVotePlayers, int? HighestVoteCount, List<DrawingAnswerResultOption>? Options = null, List<AnonymousDrawingAnswer>? AnonymousOptions = null);
public sealed record AnonymousDrawingAnswer(Guid DrawingAnswerId, string DisplayDrawingUrl, string ThumbnailDrawingUrl, int Width, int Height, int? RevealOrder = null, int? DisplayOrder = null);
public sealed record DrawingAnswerResultOption(Guid DrawingAnswerId, string DisplayDrawingUrl, string ThumbnailDrawingUrl, int Width, int Height, Guid AuthorPlayerId, string AuthorNickname, string? AuthorPhotoUrl, int VoteCount, bool IsTopResult, List<DrawingAnswerResultVoter> Voters);
public sealed record DrawingAnswerResultVoter(Guid PlayerId, string Nickname, string? ProfilePhotoUrl, int PointsAwarded);
public sealed record FinalRoundSnapshot(int CurrentPass, int TotalPasses, int SubmittedSelfies, int RequiredSelfies, int SubmittedEdits, int RequiredEdits, int SubmittedVotes, int RequiredVotes, List<FinalRoundArtifactSnapshot> Artifacts, List<FinalRoundEditAssignmentSnapshot>? EditAssignments = null);
public sealed record FinalRoundArtifactSnapshot(Guid ArtifactId, Guid SubjectPlayerId, string SubjectNickname, LocalizedText SelfiePrompt, LocalizedText TargetRole, string? DisplayMediaUrl, string? ThumbnailMediaUrl, int VoteCount, bool IsTopResult);
public sealed record FinalRoundEditAssignmentSnapshot(Guid ArtifactId, Guid EditorPlayerId, string SourceDisplayMediaUrl, string SourceThumbnailMediaUrl);



public sealed record ResultVoter(Guid PlayerId, string Nickname, string? ProfilePhotoUrl, int PointsAwarded);
public sealed record RankingEntry(Guid PlayerId, string Nickname, string? ProfilePhotoUrl, int Score, int Rank);
public sealed record RoundSummarySnapshot(int RoundNumber, GameCategorySnapshot Category, int PlayedQuestionCount, List<PlayerScoreSnapshot> PlayerRoundScores, List<RankingEntry> Ranking, bool HasNextRound, int? NextRoundNumber);

public sealed record PlayerScoreSnapshot(
    Guid PlayerId,
    int Score,
    int? Rank = null
);
public sealed record PublicPlayer(
    Guid Id,
    string Nickname,
    bool IsHost,
    bool IsReady,
    bool IsConnected,
    bool HasProfilePhoto,
    string? ProfilePhotoUrl,
    int Score);
public sealed record PublicRoomSettings(
    int RoundCount,
    int QuestionsPerRound,
    int PlayerSelectionSeconds,
    int TextAnswerSeconds,
    int VotingSeconds,
    int PhotoSeconds,
    int DrawingSeconds,
    int ResultPresentationSeconds,
    bool FinalRoundEnabled,
    int FinalDrawingPasses);
