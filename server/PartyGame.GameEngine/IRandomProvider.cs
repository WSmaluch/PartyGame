namespace PartyGame.GameEngine;

public interface IRandomProvider
{
    int Next(int minValue, int maxValue);
    int Next(int maxValue);
    void Shuffle<T>(IList<T> list);
}

public sealed class SystemRandomProvider : IRandomProvider
{
    public int Next(int minValue, int maxValue) => Random.Shared.Next(minValue, maxValue);
    public int Next(int maxValue) => Random.Shared.Next(maxValue);
    public void Shuffle<T>(IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Shared.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}
