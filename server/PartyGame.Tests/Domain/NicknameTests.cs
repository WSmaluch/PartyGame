using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Domain;

public sealed class NicknameTests
{
    [Fact]
    public void ValidateAndTrim_TrimsValidNickname() => Assert.Equal("Wojtek", Nickname.ValidateAndTrim("  Wojtek  "));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("123456789012345678901")]
    public void ValidateAndTrim_RejectsInvalidLength(string value) =>
        Assert.Throws<DomainValidationException>(() => Nickname.ValidateAndTrim(value));

    [Fact]
    public void Normalize_IsCaseInsensitive() => Assert.Equal(Nickname.Normalize("Wojtek"), Nickname.Normalize("wojtek"));
}
