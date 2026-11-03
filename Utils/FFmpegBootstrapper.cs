using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DevEnvy.FFmpeg.Binaries;
using FFmpeg.AutoGen;

namespace Ouro.Utils
{
    /// <summary>
    /// Utility to discover and configure native FFmpeg dynamic libraries for FFmpeg.AutoGen.
    /// Locates avcodec, avformat, and avutil binaries from local runtimes, custom paths, or installed software,
    /// sets native DLL search directories, and initializes dynamic bindings.
    /// </summary>
    public static class FFmpegBootstrapper
    {
        private static bool _isInitialized;
        private static readonly object _syncLock = new();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static bool Initialize()
        {
            lock (_syncLock)
            {
                if (_isInitialized) return true;

                string? targetDir = null;

                // 1. Try DevEnvy package library path
                try
                {
                    string devEnvyDir = FFmpegBinaries.GetLibraryPath();
                    if (!string.IsNullOrEmpty(devEnvyDir) && Directory.Exists(devEnvyDir) && Directory.GetFiles(devEnvyDir, "avcodec*.dll").Length > 0)
                    {
                        targetDir = devEnvyDir;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FFmpegBootstrapper] DevEnvy probe failed: {ex.Message}");
                }

                // 2. Fallback probe paths
                if (string.IsNullOrEmpty(targetDir))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string[] probePaths =
                    [
                        Path.Combine(baseDir, "runtimes", "win-x64", "native"),
                        Path.Combine(baseDir, "ffmpeg"),
                        baseDir,
                        @"C:\Program Files\obs-studio\bin\64bit"
                    ];

                    foreach (var path in probePaths)
                    {
                        if (Directory.Exists(path) && Directory.GetFiles(path, "avcodec*.dll").Length > 0)
                        {
                            targetDir = path;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(targetDir))
                {
                    System.Diagnostics.Debug.WriteLine("[FFmpegBootstrapper] Warning: Native FFmpeg shared libraries (avcodec, avformat) not found.");
                    return false;
                }

                try
                {
                    SetDllDirectory(targetDir);
                    ffmpeg.RootPath = targetDir;

                    // Initialize DynamicallyLoadedBindings for FFmpeg.AutoGen 9.0+
                    var dynBindingsType = typeof(ffmpeg).Assembly.GetType("FFmpeg.AutoGen.DynamicallyLoadedBindings");
                    if (dynBindingsType != null)
                    {
                        var libPathProp = dynBindingsType.GetProperty("LibrariesPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        libPathProp?.SetValue(null, targetDir);

                        var initMethod = dynBindingsType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        initMethod?.Invoke(null, null);
                    }

                    _isInitialized = true;
                    System.Diagnostics.Debug.WriteLine($"[FFmpegBootstrapper] Native FFmpeg successfully initialized from: {targetDir}");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FFmpegBootstrapper] Error initializing FFmpeg bindings: {ex}");
                    return false;
                }
            }
        }

        public static string? GetFFmpegExecutablePath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                [
                    Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
                    Path.Combine(baseDir, "ffmpeg.exe"),
                    Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe")
                ];

                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }
            }
            catch { }

            return "ffmpeg";
        }

        public static string? GetFFprobeExecutablePath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                [
                    Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffprobe.exe"),
                    Path.Combine(baseDir, "ffprobe.exe"),
                    Path.Combine(baseDir, "ffmpeg", "ffprobe.exe")
                ];

                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }
            }
            catch { }

            return "ffprobe";
        }
    }
}
