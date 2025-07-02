using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Speech.Synthesis;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

public class PageContent
{
    public string EnglishText { get; set; }
    public string TurkishText { get; set; }
    public List<byte[]> Images { get; set; } = new List<byte[]>();
}

public class Program
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run <pdf-file>");
            return;
        }

        string pdfPath = args[0];
        var pages = ExtractPages(pdfPath);

        foreach (var page in pages)
        {
            page.TurkishText = await TranslateToTurkish(page.EnglishText);
        }

        string audioPath = Path.Combine(Path.GetTempPath(), "output.wav");
        GenerateSpeech(string.Join("\n", pages.ConvertAll(p => p.TurkishText)), audioPath);

        string imagesFolder = Path.Combine(Path.GetTempPath(), "frames");
        Directory.CreateDirectory(imagesFolder);
        int frameIndex = 1;
        foreach (var page in pages)
        {
            foreach (var img in page.Images)
            {
                File.WriteAllBytes(Path.Combine(imagesFolder, $"frame{frameIndex:000}.png"), img);
                frameIndex++;
            }
        }

        string videoPath = Path.Combine(Directory.GetCurrentDirectory(), "video.mp4");
        CreateVideo(imagesFolder, audioPath, videoPath);

        Console.WriteLine($"Video created at {videoPath}");
        TryOpenVideo(videoPath);
    }

    private static List<PageContent> ExtractPages(string pdfPath)
    {
        var result = new List<PageContent>();
        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            var content = new PageContent
            {
                EnglishText = page.Text
            };

            foreach (var image in page.GetImages())
            {
                using var ms = new MemoryStream();
                image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                content.Images.Add(ms.ToArray());
            }

            result.Add(content);
        }

        return result;
    }

    private static async Task<string> TranslateToTurkish(string text)
    {
        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var payload = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = "Translate the user content from English to Turkish." },
                new { role = "user", content = text }
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    }

    private static void GenerateSpeech(string text, string outputPath)
    {
        using var synth = new SpeechSynthesizer();
        synth.SetOutputToWaveFile(outputPath);
        synth.Speak(text);
    }

    private static void CreateVideo(string imagesFolder, string audioPath, string outputPath)
    {
        string ffmpeg = "ffmpeg"; // requires ffmpeg installed
        string args = $"-y -r 1 -i {Path.Combine(imagesFolder, "frame%03d.png")} -i {audioPath} -c:v libx264 -c:a aac -strict experimental -shortest {outputPath}";
        var p = Process.Start(ffmpeg, args);
        p.WaitForExit();
    }

    private static void TryOpenVideo(string path)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
            Process.Start(psi);
        }
        catch { }
    }
}
