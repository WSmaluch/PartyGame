using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Infrastructure;

public sealed class RoomCodeGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsFourAllowedCharacters()
    {
        var result = await new RoomCodeGenerator().GenerateAsync((_, _) => Task.FromResult(true));
        Assert.Equal(4, result.Length);
        Assert.All(result, character => Assert.Contains(character, RoomCodeGenerator.Alphabet));
    }

    [Fact]
    public async Task GenerateAsync_RetriesAfterCollision()
    {
        var indices = new Queue<int>([0, 0, 0, 0, 1, 1, 1, 1]);
        var generator = new RoomCodeGenerator(_ => indices.Dequeue(), 2);
        var calls = 0;
        var result = await generator.GenerateAsync((_, _) => Task.FromResult(++calls == 2));
        Assert.Equal("BBBB", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GenerateAsync_StopsAtMaximumAttempts()
    {
        var generator = new RoomCodeGenerator(_ => 0, 2);
        await Assert.ThrowsAsync<RoomCodeGenerationException>(() => generator.GenerateAsync((_, _) => Task.FromResult(false)));
    }
}
