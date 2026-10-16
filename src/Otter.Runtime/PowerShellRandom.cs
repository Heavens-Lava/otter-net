using System.Security.Cryptography;

namespace Otter.Runtime;

// Get-Random as Windows PowerShell 5.1 runs it (PolymorphicRandomNumberGenerator
// in GetRandomCommand.cs): cryptographic numbers until `set random seed`
// (D101), then System.Random with that seed - whose seeded sequence .NET 10
// keeps from .NET Framework - so a seeded program picks the same numbers in
// both implementations.
public sealed class PowerShellRandom
{
    Random? seeded;

    // Get-Random -SetSeed n. The cmdlet also returns a number, which otterPS
    // throws away, so one draw is used up here too.
    public void Seed(int seed)
    {
        seeded = new Random(seed);
        Next(0, int.MaxValue);
    }

    // Get-Random -Minimum min -Maximum max: max itself is never picked.
    public int Next(int minValue, int maxValue)
    {
        long range = (long)maxValue - minValue;
        if (range <= int.MaxValue) return (int)(NextDouble() * range) + minValue;
        return (int)((long)(NextDouble() * range) + minValue);
    }

    double NextDouble() => NextInt() * (1.0 / int.MaxValue);

    int NextInt()
    {
        int result;
        do { result = Sample(); } while (result == int.MaxValue);
        if (result < 0) result += int.MaxValue;
        return result;
    }

    int Sample()
    {
        var data = new byte[sizeof(int)];
        if (seeded != null) seeded.NextBytes(data);
        else RandomNumberGenerator.Fill(data);
        return BitConverter.ToInt32(data, 0);
    }
}
