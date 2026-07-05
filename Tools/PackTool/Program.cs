using LingFanEngine.Services.Resources;
using LingFanEngine.Services.Saves;

// 灵泛引擎资源打包工具
// 用法: dotnet run -- <sourceDir> <outputPath> [keyFile]
// 默认密钥用于开发测试，生产环境应使用独立密钥文件

var sourceDir = args.Length > 0 ? args[0] : "../Stories";
var outputPath = args.Length > 1 ? args[1] : "../Stories.lfpack";

// 开发测试密钥（32字节Key + 16字节IV）
// 生产环境应从安全存储读取
byte[] key, iv;
if (args.Length > 2 && File.Exists(args[2]))
{
    var keyData = await File.ReadAllBytesAsync(args[2]);
    key = keyData[..32];
    iv = keyData[32..48];
}
else
{
    // 默认开发密钥
    key = new byte[] {
        0x2B, 0x7E, 0x15, 0x16, 0x28, 0xAE, 0xD2, 0xA6,
        0xAB, 0xF7, 0x15, 0x88, 0x09, 0xCF, 0x4F, 0x3C,
        0x76, 0x2E, 0x71, 0x60, 0xF3, 0x8B, 0x45, 0x6A,
        0x8D, 0x9C, 0x3E, 0x12, 0x4E, 0xA1, 0x7B, 0xC5
    };
    iv = new byte[] {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F
    };
}

Console.WriteLine($"打包: {sourceDir} → {outputPath}");
Console.WriteLine($"密钥: {(args.Length > 2 ? args[2] : "默认开发密钥")}");

if (!Directory.Exists(sourceDir))
{
    Console.WriteLine($"错误: 源目录不存在 '{sourceDir}'");
    return 1;
}

await PackBuilder.BuildAsync(sourceDir, outputPath, key, iv);
Console.WriteLine($"完成: {outputPath} ({(new FileInfo(outputPath).Length / 1024)} KB)");
return 0;
