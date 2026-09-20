using System.Diagnostics;

namespace Station.Desktop.Services;

/// <summary>新报警声音提示：Windows 用控制台蜂鸣，Linux 尽力用 paplay/aplay 播放生成的提示音。</summary>
public static class DesktopAlertSound
{
    public static void Play()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Console.Beep(880, 250);
                return;
            }

            var wav = Path.Combine(Path.GetTempPath(), "station-alert.wav");
            if (!File.Exists(wav))
            {
                File.WriteAllBytes(wav, GenerateBeepWav(880, 0.25));
            }

            foreach (var command in new[] { "paplay", "aplay" })
            {
                try
                {
                    Process.Start(new ProcessStartInfo(command, wav)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    return;
                }
                catch
                {
                    // 尝试下一个播放器
                }
            }
        }
        catch
        {
            // 声音不可用不影响功能
        }
    }

    /// <summary>生成 16-bit PCM 单声道提示音 WAV（正弦波）。</summary>
    private static byte[] GenerateBeepWav(int frequency, double seconds)
    {
        const int sampleRate = 8000;
        var samples = (int)(sampleRate * seconds);
        var dataSize = samples * 2;
        var wav = new byte[44 + dataSize];
        Buffer.BlockCopy("RIFF"u8.ToArray(), 0, wav, 0, 4);
        BitConverter.GetBytes(36 + dataSize).CopyTo(wav, 4);
        Buffer.BlockCopy("WAVEfmt "u8.ToArray(), 0, wav, 8, 8);
        BitConverter.GetBytes(16).CopyTo(wav, 16);
        BitConverter.GetBytes((short)1).CopyTo(wav, 20);
        BitConverter.GetBytes((short)1).CopyTo(wav, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
        BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28);
        BitConverter.GetBytes((short)2).CopyTo(wav, 32);
        BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        Buffer.BlockCopy("data"u8.ToArray(), 0, wav, 36, 4);
        BitConverter.GetBytes(dataSize).CopyTo(wav, 40);
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * short.MaxValue * 0.6);
            BitConverter.GetBytes(value).CopyTo(wav, 44 + i * 2);
        }

        return wav;
    }
}
