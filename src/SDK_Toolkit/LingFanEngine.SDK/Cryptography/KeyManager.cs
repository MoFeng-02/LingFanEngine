using System;
using System.IO;
using System.Security.Cryptography;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Cryptography;

/// <summary>
/// 密钥生成 + 分片 + 本地存储
/// </summary>
public static class KeyManager
{
    private const int KeySize = 32; // AES-256

    /// <summary>生成 32 字节随机密钥</summary>
    public static byte[] GenerateKey()
    {
        var key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>保存密钥到本地（~/.lingfan/keys/{SHA256(projectPath)}.key）</summary>
    public static void SaveKey(string projectPath, byte[] key)
    {
        var keysDir = PathHelper.GetKeysDirectory();
        PathHelper.EnsureDirectory(keysDir);

        var keyFile = GetKeyFilePath(projectPath);
        File.WriteAllBytes(keyFile, key);
    }

    /// <summary>从本地加载密钥</summary>
    public static byte[]? LoadKey(string projectPath)
    {
        var keyFile = GetKeyFilePath(projectPath);
        if (!File.Exists(keyFile))
            return null;

        return File.ReadAllBytes(keyFile);
    }

    /// <summary>获取或创建密钥</summary>
    public static byte[] GetOrCreateKey(string projectPath)
    {
        var key = LoadKey(projectPath);
        if (key != null)
            return key;

        key = GenerateKey();
        SaveKey(projectPath, key);
        return key;
    }

    /// <summary>删除密钥</summary>
    public static void DeleteKey(string projectPath)
    {
        var keyFile = GetKeyFilePath(projectPath);
        if (File.Exists(keyFile))
            File.Delete(keyFile);
    }

    /// <summary>将密钥分片为 N 段</summary>
    public static byte[][] ShardKey(byte[] key, int shardCount)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"密钥长度必须为 {KeySize} 字节", nameof(key));

        var shardSize = KeySize / shardCount;
        var shards = new byte[shardCount][];

        for (var i = 0; i < shardCount; i++)
        {
            shards[i] = new byte[shardSize];
            Array.Copy(key, i * shardSize, shards[i], 0, shardSize);
        }

        return shards;
    }

    /// <summary>从分片重组密钥</summary>
    public static byte[] CombineShards(byte[][] shards)
    {
        var key = new byte[KeySize];
        var offset = 0;

        foreach (var shard in shards)
        {
            Array.Copy(shard, 0, key, offset, shard.Length);
            offset += shard.Length;
        }

        return key;
    }

    /// <summary>获取密钥文件路径</summary>
    private static string GetKeyFilePath(string projectPath)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(projectPath));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
        return Path.Combine(PathHelper.GetKeysDirectory(), $"{hashHex}.key");
    }
}
