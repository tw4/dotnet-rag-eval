using System.Security.Cryptography;
using System.Text;

namespace RagEval.Core.Indexing;

/// <summary>Name-based UUIDs (RFC 9562 version 5), so a chunk keeps its point id across runs.</summary>
public static class DeterministicGuid
{
    private static readonly Guid Namespace = new("6f1d2a3c-4b5e-4f60-8a71-92b3c4d5e6f7");

#pragma warning disable CA5350 // SHA-1 is what UUIDv5 specifies; this is an identifier, not security.
    public static Guid Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Span<byte> ns = stackalloc byte[16];
        Namespace.TryWriteBytes(ns, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        ns.CopyTo(input);
        nameBytes.CopyTo(input, 16);
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);   // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);   // RFC variant
        return new Guid(hash[..16], bigEndian: true);
    }
#pragma warning restore CA5350
}
