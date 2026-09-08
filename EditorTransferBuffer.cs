using System;
using System.IO;

namespace TradersExtended
{
    /// <summary>One bounded, ordered reliable-RPC transfer. Authorization belongs to the connection handler.</summary>
    internal sealed class EditorTransferBuffer
    {
        internal const int ChunkBytes = 32 * 1024;
        private readonly byte[] bytes;
        private int nextOffset;
        internal readonly long Id;
        internal readonly int Operation;
        internal float LastActivity;

        internal EditorTransferBuffer(long id, int operation, int length, int maximumBytes, float now)
        {
            if (id <= 0 || length <= 0 || length > maximumBytes)
                throw new InvalidDataException("Invalid configuration transfer length or ID.");
            Id = id;
            Operation = operation;
            bytes = new byte[length];
            LastActivity = now;
        }

        internal byte[] Add(long id, int operation, int length, int offset, byte[] chunk, float now)
        {
            if (id != Id || operation != Operation || length != bytes.Length || chunk == null ||
                chunk.Length == 0 || chunk.Length > ChunkBytes || offset != nextOffset ||
                chunk.Length > bytes.Length - nextOffset)
                throw new InvalidDataException("Invalid or out-of-order configuration transfer chunk.");
            Buffer.BlockCopy(chunk, 0, bytes, nextOffset, chunk.Length);
            nextOffset += chunk.Length;
            LastActivity = now;
            return nextOffset == bytes.Length ? bytes : null;
        }
    }
}
