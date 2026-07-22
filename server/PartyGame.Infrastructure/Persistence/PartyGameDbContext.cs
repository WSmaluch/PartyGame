using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;

namespace PartyGame.Infrastructure.Persistence;

public sealed class PartyGameDbContext(DbContextOptions<PartyGameDbContext> options)
    : DbContext(options)
{
    public DbSet<DatabaseMetadata> DatabaseMetadata => Set<DatabaseMetadata>();
    public DbSet<GameRoom> GameRooms => Set<GameRoom>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerSession> PlayerSessions => Set<PlayerSession>();
    public DbSet<RoomSettings> RoomSettings => Set<RoomSettings>();

    public DbSet<GamePackage> GamePackages => Set<GamePackage>();
    public DbSet<GameCategory> GameCategories => Set<GameCategory>();
    public DbSet<GameQuestion> GameQuestions => Set<GameQuestion>();

    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();
    public DbSet<GameQuestionInstance> GameQuestionInstances => Set<GameQuestionInstance>();
    public DbSet<GameQuestionEligiblePlayer> GameQuestionEligiblePlayers => Set<GameQuestionEligiblePlayer>();
    public DbSet<PlayerSelectionAnswer> PlayerSelectionAnswers => Set<PlayerSelectionAnswer>();
    public DbSet<ScoreTransaction> ScoreTransactions => Set<ScoreTransaction>();

    public DbSet<TextAnswerEligiblePlayer> TextAnswerEligiblePlayers => Set<TextAnswerEligiblePlayer>();
    public DbSet<TextAnswerSubmission> TextAnswerSubmissions => Set<TextAnswerSubmission>();
    public DbSet<TextAnswerVoteEligiblePlayer> TextAnswerVoteEligiblePlayers => Set<TextAnswerVoteEligiblePlayer>();
    public DbSet<TextAnswerVote> TextAnswerVotes => Set<TextAnswerVote>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PhotoAnswerEligiblePlayer> PhotoAnswerEligiblePlayers => Set<PhotoAnswerEligiblePlayer>();
    public DbSet<PhotoAnswerSubmission> PhotoAnswerSubmissions => Set<PhotoAnswerSubmission>();
    public DbSet<PhotoAnswerVoteEligiblePlayer> PhotoAnswerVoteEligiblePlayers => Set<PhotoAnswerVoteEligiblePlayer>();
    public DbSet<PhotoAnswerVote> PhotoAnswerVotes => Set<PhotoAnswerVote>();
    public DbSet<DrawingAnswerEligiblePlayer> DrawingAnswerEligiblePlayers => Set<DrawingAnswerEligiblePlayer>();
    public DbSet<DrawingAnswerSubmission> DrawingAnswerSubmissions => Set<DrawingAnswerSubmission>();
    public DbSet<DrawingAnswerVoteEligiblePlayer> DrawingAnswerVoteEligiblePlayers => Set<DrawingAnswerVoteEligiblePlayer>();
    public DbSet<DrawingAnswerVote> DrawingAnswerVotes => Set<DrawingAnswerVote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DatabaseMetadata>(entity =>
        {
            entity.ToTable("DatabaseMetadata");
            entity.HasKey(metadata => metadata.Id);
        });

        modelBuilder.Entity<GameRoom>(entity =>
        {
            entity.ToTable("GameRooms");
            entity.HasKey(room => room.Id);
            entity.Property(room => room.Code).HasMaxLength(4).IsRequired().UseCollation("NOCASE");
            entity.HasIndex(room => room.Code).IsUnique();
            entity.Property(room => room.Phase).HasConversion<int>();

            entity.Property(room => room.SelectedPackageKeys)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            var enumJsonOptions = new JsonSerializerOptions();
            enumJsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

            var enabledQuestionTypesComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<QuestionType>>(
                (c1, c2) => c1!.SequenceEqual(c2!),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            entity.Property(room => room.EnabledQuestionTypes)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, enumJsonOptions),
                    v => JsonSerializer.Deserialize<List<QuestionType>>(v, enumJsonOptions) ?? new List<QuestionType> { QuestionType.PlayerSelection })
                .Metadata.SetValueComparer(enabledQuestionTypesComparer);

            entity.HasMany(room => room.Players)
                .WithOne(player => player.Room)
                .HasForeignKey(player => player.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(room => room.Settings)
                .WithOne()
                .HasForeignKey<RoomSettings>(settings => settings.GameRoomId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(room => room.Session)
                .WithOne(session => session.Room)
                .HasForeignKey<GameSession>(session => session.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(room => room.ContentPackage)
                .WithMany()
                .HasForeignKey(room => room.ContentPackageVersionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("Players");
            entity.HasKey(player => player.Id);
            entity.Property(player => player.Nickname).HasMaxLength(Nickname.MaximumLength).IsRequired();
            entity.Property(player => player.NormalizedNickname).HasMaxLength(Nickname.MaximumLength).IsRequired().UseCollation("NOCASE");
            entity.Property(player => player.ProfilePhotoStorageKey).HasMaxLength(120);
            entity.Property(player => player.ProfilePhotoContentType).HasMaxLength(32);
            entity.HasIndex(player => new { player.RoomId, player.NormalizedNickname }).IsUnique();
            entity.HasOne(player => player.Session)
                .WithOne(session => session.Player)
                .HasForeignKey<PlayerSession>(session => session.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerSession>(entity =>
        {
            entity.ToTable("PlayerSessions");
            entity.HasKey(session => session.PlayerId);
            entity.Property(session => session.ReconnectTokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        });

        modelBuilder.Entity<RoomSettings>(entity =>
        {
            entity.ToTable("RoomSettings");
            entity.HasKey(settings => settings.GameRoomId);
        });

        modelBuilder.Entity<GamePackage>(entity =>
        {
            entity.ToTable("GamePackages");
            entity.HasKey(package => package.Id);
            entity.Property(package => package.Key).HasMaxLength(64).IsRequired();
            entity.Property(package => package.Status).HasConversion<int>();
            entity.Property(package => package.ConcurrencyToken).IsConcurrencyToken();
            entity.HasIndex(package => new { package.LogicalPackageId, package.Version }).IsUnique();
            entity.HasIndex(package => new { package.LogicalPackageId, package.Status })
                .HasDatabaseName("IX_GamePackages_LogicalPackageId_ActiveDraft")
                .HasFilter("\"Status\" = 0")
                .IsUnique();
            entity.HasMany(package => package.Categories)
                .WithOne(category => category.Package)
                .HasForeignKey(category => category.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameCategory>(entity =>
        {
            entity.ToTable("GameCategories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Key).HasMaxLength(64).IsRequired();
            entity.Property(category => category.ConcurrencyToken).IsConcurrencyToken();
            entity.HasIndex(category => new { category.PackageId, category.Key }).IsUnique();
            entity.HasMany(category => category.Questions)
                .WithOne(question => question.Category)
                .HasForeignKey(question => question.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameQuestion>(entity =>
        {
            entity.ToTable("GameQuestions");
            entity.HasKey(question => question.Id);
            entity.Property(question => question.Key).HasMaxLength(64).IsRequired();
            entity.Property(question => question.Type).HasConversion<int>();
            entity.Property(question => question.ConcurrencyToken).IsConcurrencyToken();
            entity.HasIndex(question => new { question.CategoryId, question.Key }).IsUnique();
        });

        modelBuilder.Entity<GameSession>(entity =>
        {
            entity.ToTable("GameSessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Stage).HasConversion<int>();
            entity.Property(session => session.PausedStage).HasConversion<int>();
            entity.HasIndex(session => session.RoomId).IsUnique();
            entity.HasMany(session => session.Rounds)
                .WithOne(round => round.Session)
                .HasForeignKey(round => round.GameSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(session => session.ScoreTransactions).WithOne(transaction => transaction.Session).HasForeignKey(transaction => transaction.GameSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameRound>(entity =>
        {
            entity.ToTable("GameRounds");
            entity.HasKey(round => round.Id);
            entity.HasMany(round => round.Questions)
                .WithOne(question => question.Round)
                .HasForeignKey(question => question.RoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameQuestionInstance>(entity =>
        {
            entity.ToTable("GameQuestionInstances");
            entity.HasKey(instance => instance.Id);
            entity.Property(instance => instance.Stage).HasConversion<int>();
            entity.HasMany(instance => instance.EligiblePlayers)
                .WithOne(eligible => eligible.QuestionInstance)
                .HasForeignKey(eligible => eligible.QuestionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.Answers)
                .WithOne(answer => answer.QuestionInstance)
                .HasForeignKey(answer => answer.QuestionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.TextAnswerEligiblePlayers)
                .WithOne()
                .HasForeignKey(e => e.QuestionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.TextAnswerSubmissions)
                .WithOne()
                .HasForeignKey(e => e.QuestionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.TextAnswerVoteEligiblePlayers)
                .WithOne()
                .HasForeignKey(e => e.QuestionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.TextAnswerVotes)
                .WithOne()
                .HasForeignKey(e => e.QuestionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.PhotoAnswerEligiblePlayers).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.PhotoAnswerSubmissions).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.PhotoAnswerVoteEligiblePlayers).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.PhotoAnswerVotes).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.DrawingAnswerEligiblePlayers).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.DrawingAnswerSubmissions).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.DrawingAnswerVoteEligiblePlayers).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(instance => instance.DrawingAnswerVotes).WithOne().HasForeignKey(e => e.QuestionInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameQuestionEligiblePlayer>(entity =>
        {
            entity.ToTable("GameQuestionEligiblePlayers");
            entity.HasKey(eligible => eligible.Id);
            entity.HasIndex(eligible => new { eligible.QuestionInstanceId, eligible.PlayerId }).IsUnique();
        });

        modelBuilder.Entity<PlayerSelectionAnswer>(entity =>
        {
            entity.ToTable("PlayerSelectionAnswers");
            entity.HasKey(answer => answer.Id);
            entity.HasIndex(answer => new { answer.QuestionInstanceId, answer.VoterPlayerId }).IsUnique();
        });

        modelBuilder.Entity<ScoreTransaction>(entity =>
        {
            entity.ToTable("ScoreTransactions");
            entity.HasKey(transaction => transaction.Id);
            entity.HasIndex(transaction => new { transaction.QuestionInstanceId, transaction.PlayerId }).IsUnique();
        });

        modelBuilder.Entity<TextAnswerEligiblePlayer>(entity =>
        {
            entity.ToTable("TextAnswerEligiblePlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.PlayerId }).IsUnique();
        });

        modelBuilder.Entity<TextAnswerSubmission>(entity =>
        {
            entity.ToTable("TextAnswerSubmissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.AuthorPlayerId }).IsUnique();
            entity.HasIndex(e => new { e.QuestionInstanceId, e.RevealOrder }).IsUnique();
        });

        modelBuilder.Entity<TextAnswerVoteEligiblePlayer>(entity =>
        {
            entity.ToTable("TextAnswerVoteEligiblePlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.PlayerId }).IsUnique();
        });

        modelBuilder.Entity<TextAnswerVote>(entity =>
        {
            entity.ToTable("TextAnswerVotes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.VoterPlayerId }).IsUnique();
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable("MediaAssets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StorageProvider).HasMaxLength(32).IsRequired();
            entity.Property(e => e.DisplayStorageKey).HasMaxLength(320).IsRequired();
            entity.Property(e => e.ThumbnailStorageKey).HasMaxLength(320).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        });

        modelBuilder.Entity<PhotoAnswerEligiblePlayer>(entity =>
        {
            entity.ToTable("PhotoAnswerEligiblePlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.PlayerId }).IsUnique();
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhotoAnswerSubmission>(entity =>
        {
            entity.ToTable("PhotoAnswerSubmissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.AuthorPlayerId }).IsUnique();
            entity.HasIndex(e => new { e.QuestionInstanceId, e.ClientSubmissionId }).IsUnique();
            entity.HasIndex(e => new { e.QuestionInstanceId, e.RevealOrder }).IsUnique();
            entity.HasOne(e => e.MediaAsset).WithOne().HasForeignKey<PhotoAnswerSubmission>(e => e.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.AuthorPlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhotoAnswerVoteEligiblePlayer>(entity =>
        {
            entity.ToTable("PhotoAnswerVoteEligiblePlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.PlayerId }).IsUnique();
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhotoAnswerVote>(entity =>
        {
            entity.ToTable("PhotoAnswerVotes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.VoterPlayerId }).IsUnique();
            entity.HasOne<PhotoAnswerSubmission>().WithMany().HasForeignKey(e => e.SelectedPhotoAnswerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.VoterPlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DrawingAnswerEligiblePlayer>(entity =>
        {
            entity.ToTable("DrawingAnswerEligiblePlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.PlayerId }).IsUnique();
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DrawingAnswerSubmission>(entity =>
        {
            entity.ToTable("DrawingAnswerSubmissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.AuthorPlayerId }).IsUnique();
            entity.HasIndex(e => new { e.QuestionInstanceId, e.ClientSubmissionId }).IsUnique();
            entity.HasIndex(e => new { e.QuestionInstanceId, e.RevealOrder }).IsUnique();
            entity.HasOne(e => e.MediaAsset).WithOne().HasForeignKey<DrawingAnswerSubmission>(e => e.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.AuthorPlayerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DrawingAnswerVoteEligiblePlayer>(entity =>
        {
            entity.ToTable("DrawingAnswerVoteEligiblePlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.PlayerId }).IsUnique();
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DrawingAnswerVote>(entity =>
        {
            entity.ToTable("DrawingAnswerVotes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.QuestionInstanceId, e.VoterPlayerId }).IsUnique();
            entity.HasOne<DrawingAnswerSubmission>().WithMany().HasForeignKey(e => e.SelectedDrawingAnswerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Player>().WithMany().HasForeignKey(e => e.VoterPlayerId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
