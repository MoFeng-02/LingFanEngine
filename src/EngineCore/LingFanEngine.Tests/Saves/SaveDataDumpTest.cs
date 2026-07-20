using System.Text;
using System.Text.Json;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Serialization;
using LingFanEngine.Services.Saves;
using Xunit;

namespace LingFanEngine.Tests.Saves;

/// <summary>
/// 解密并打印存档文件内容——用于诊断读档后元素混乱问题
/// </summary>
public class SaveDataDumpTest
{
    [Fact]
    public async Task DumpDemoSlotSave()
    {
        var sb = new StringBuilder();

        // 存档路径（与 Test.Windows 运行目录一致）
        var savePath = @"E:\Project\Engine\src\Demo\Test.Windows\bin\Debug\net10.0-windows\Saves\demo_slot.sav";
        if (!File.Exists(savePath))
        {
            savePath = Path.Combine(AppContext.BaseDirectory, "Saves", "demo_slot.sav");
            if (!File.Exists(savePath))
            {
                File.WriteAllText(@"E:\Project\Engine\save_dump.txt", "[SaveDataDump] demo_slot.sav not found");
                return;
            }
        }

        // 使用与 BinarySaveService 相同的机器密钥生成逻辑
        var machineKey = $"{Environment.MachineName}_{Environment.UserName}";
        var key = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(machineKey));
        var iv = new byte[16];
        Array.Copy(key, iv, 16);

        var aes = new AesEncryption(key, iv);
        var encryptedData = await File.ReadAllBytesAsync(savePath);
        var decryptedData = aes.Decrypt(encryptedData);
        var json = Encoding.UTF8.GetString(decryptedData);

        // 美化打印
        var doc = JsonDocument.Parse(json);
        var prettyJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

        sb.AppendLine("========== SaveData Dump (demo_slot) ==========");
        sb.AppendLine(prettyJson);
        sb.AppendLine("===============================================");

        // 关键字段提取
        var saveData = JsonSerializer.Deserialize(json, LfJsonContext.Default.SaveData);
        Assert.NotNull(saveData);

        sb.AppendLine();
        sb.AppendLine("--- 关键字段 ---");
        sb.AppendLine($"SceneName: {saveData!.SceneName}");
        sb.AppendLine($"DslCurrentIndex: {saveData.DslCurrentIndex}");
        sb.AppendLine($"DslWaitingType: {saveData.DslWaitingType}");
        sb.AppendLine($"SceneType: {saveData.SceneType}");
        sb.AppendLine($"State.Count: {saveData.State?.Count ?? 0}");
        sb.AppendLine($"TypedState.Count: {saveData.TypedState?.Count ?? 0}");
        sb.AppendLine($"SceneStackSnapshot.Count: {saveData.SceneStackSnapshot?.Count ?? 0}");

        // 打印所有状态键
        if (saveData.TypedState != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- TypedState 键列表 ---");
            foreach (var (k, v) in saveData.TypedState)
                sb.AppendLine($"  {k} = Type={v.Type}, Value={v.Value}");
        }
        else if (saveData.State != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- State 键列表 ---");
            foreach (var (k, v) in saveData.State)
                sb.AppendLine($"  {k} = {v}");
        }

        // 写入文件
        var outputPath = @"E:\Project\Engine\save_dump.txt";
        File.WriteAllText(outputPath, sb.ToString());
    }
}
