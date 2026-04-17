using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EmoTracker.Core;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.VoiceRecognition
{
    public partial class VoskModelDownloadWindow : Window
    {
        private const string ModelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";

        public bool Success { get; private set; }

        private CancellationTokenSource _cts;

        public VoskModelDownloadWindow()
        {
            InitializeComponent();
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            BeginDownload();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        private void BeginDownload()
        {
            PromptPanel.IsVisible = false;
            DownloadPanel.IsVisible = true;
            DownloadButton.IsVisible = false;
            CancelButton.IsEnabled = true;

            _cts = new CancellationTokenSource();
            _ = RunDownloadAsync(_cts.Token);
        }

        private async Task RunDownloadAsync(CancellationToken token)
        {
            string zipPath = Path.Combine(UserDirectory.Path, "vosk-model-download.zip");
            string tempDir = Path.Combine(UserDirectory.Path, "vosk-model-temp");
            string targetPath = Path.Combine(UserDirectory.Path, "vosk-model");

            try
            {
                // Download
                using var client = new HttpClient();
                client.Timeout = Timeout.InfiniteTimeSpan;

                using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                // Nested block ensures fileStream is fully closed before ZipFile reads the same path
                using (var httpStream = await response.Content.ReadAsStreamAsync(token))
                using (var fileStream = File.Create(zipPath))
                {
                    byte[] buffer = new byte[81920];
                    long bytesRead = 0;
                    int read;

                    while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, token);
                        bytesRead += read;

                        if (totalBytes > 0)
                        {
                            double pct = (double)bytesRead / totalBytes.Value * 100.0;
                            double mb = bytesRead / 1_048_576.0;
                            double totalMb = totalBytes.Value / 1_048_576.0;
                            Dispatcher.UIThread.Post(() =>
                            {
                                DownloadProgress.Value = pct;
                                StatusText.Text = $"Downloading speech model… {mb:F1} / {totalMb:F1} MB";
                            });
                        }
                    }
                } // fileStream disposed and flushed here before extraction

                // Extract
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText.Text = "Installing speech model…";
                    DownloadProgress.IsIndeterminate = true;
                    CancelButton.IsEnabled = false;
                });

                await Task.Run(() =>
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    ZipFile.ExtractToDirectory(zipPath, tempDir);

                    // The zip contains one top-level model directory; move it to vosk-model
                    string extractedModel = Directory.GetDirectories(tempDir).First();
                    if (Directory.Exists(targetPath)) Directory.Delete(targetPath, recursive: true);
                    Directory.Move(extractedModel, targetPath);

                    Directory.Delete(tempDir, recursive: false);
                    File.Delete(zipPath);
                }, token);

                Success = true;
                Dispatcher.UIThread.Post(Close);
            }
            catch (OperationCanceledException)
            {
                Cleanup(zipPath, tempDir);
                Dispatcher.UIThread.Post(Close);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[Voice] Failed to download Vosk model");
                Cleanup(zipPath, tempDir);
                Dispatcher.UIThread.Post(() =>
                {
                    DownloadPanel.IsVisible = false;
                    PromptPanel.IsVisible = true;
                    DownloadButton.IsVisible = true;
                    DownloadButton.Content = "Retry";
                    CancelButton.IsEnabled = true;
                    StatusText.Text = "Downloading speech model…";
                    DownloadProgress.Value = 0;
                    DownloadProgress.IsIndeterminate = false;
                });
            }
        }

        private static void Cleanup(string zipPath, string tempDir)
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
