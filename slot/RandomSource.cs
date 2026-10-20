using System;
using System.Security.Cryptography;

namespace slot
{
    internal interface IRandomSource
    {
        int NextInt(int exclusiveMaximum);
    }

    internal sealed class CryptoRandomSource : IRandomSource, IDisposable
    {
        private readonly RandomNumberGenerator _generator;

        public CryptoRandomSource() : this(RandomNumberGenerator.Create()) { }
        internal CryptoRandomSource(RandomNumberGenerator generator) => _generator = generator ?? throw new ArgumentNullException(nameof(generator));

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            ulong range = (ulong)uint.MaxValue + 1;
            ulong limit = range - range % (uint)exclusiveMaximum;
            byte[] bytes = new byte[4];
            uint value;
            do
            {
                _generator.GetBytes(bytes);
                value = BitConverter.ToUInt32(bytes, 0);
            } while ((ulong)value >= limit);
            return (int)(value % (uint)exclusiveMaximum);
        }

        public void Dispose() => _generator.Dispose();
    }
}
