using System.Security.Cryptography;

namespace RtpReceiver.Rtp;

public class Crypto
{
    // TODO: When .NET Standard and Framework support are deprecated these pragmas can be removed.
#pragma warning disable SYSLIB0023

    public const int DEFAULT_RANDOM_LENGTH = 10;    // Number of digits to return for default random numbers.
    private const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";


    static int seed = Environment.TickCount;

    static readonly ThreadLocal<Random> random = new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));

    public static int Rand(int maxValue)
    {
        return random.Value.Next(maxValue);
    }
    private static RNGCryptoServiceProvider m_randomProvider = new RNGCryptoServiceProvider();

    public static string GetRandomString(int length)
    {
        char[] buffer = new char[length];

        for (int i = 0; i < length; i++)
        {
            buffer[i] = CHARS[Rand(CHARS.Length)];
        }
        return new string(buffer);
    }

    /// <summary>
    /// Returns a random number of a specified length.
    /// </summary>
    public static int GetRandomInt(int length)
    {
        int randomStart = 1000000000;
        int randomEnd = Int32.MaxValue;

        if (length > 0 && length < DEFAULT_RANDOM_LENGTH)
        {
            randomStart = Convert.ToInt32(Math.Pow(10, length - 1));
            randomEnd = Convert.ToInt32(Math.Pow(10, length) - 1);
        }

        return GetRandomInt(randomStart, randomEnd);
    }

    public static Int32 GetRandomInt(Int32 minValue, Int32 maxValue)
    {

        if (minValue > maxValue)
        {
            throw new ArgumentOutOfRangeException("minValue");
        }
        else if (minValue == maxValue)
        {
            return minValue;
        }

        Int64 diff = maxValue - minValue + 1;
        int attempts = 0;
        while (attempts < 10)
        {
            byte[] uint32Buffer = new byte[4];
            m_randomProvider.GetBytes(uint32Buffer);
            UInt32 rand = BitConverter.ToUInt32(uint32Buffer, 0);

            Int64 max = (1 + (Int64)UInt32.MaxValue);
            Int64 remainder = max % diff;
            if (rand <= max - remainder)
            {
                return (Int32)(minValue + (rand % diff));
            }
            attempts++;
        }
        throw new ApplicationException("GetRandomInt did not return an appropriate random number within 10 attempts.");
    }

    public static UInt16 GetRandomUInt16()
    {
        byte[] uint16Buffer = new byte[2];
        m_randomProvider.GetBytes(uint16Buffer);
        return BitConverter.ToUInt16(uint16Buffer, 0);
    }

    public static UInt32 GetRandomUInt(bool noZero = false)
    {
        byte[] uint32Buffer = new byte[4];
        m_randomProvider.GetBytes(uint32Buffer);
        var randomUint = BitConverter.ToUInt32(uint32Buffer, 0);

        if (noZero && randomUint == 0)
        {
            m_randomProvider.GetBytes(uint32Buffer);
            randomUint = BitConverter.ToUInt32(uint32Buffer, 0);
        }

        return randomUint;
    }
}