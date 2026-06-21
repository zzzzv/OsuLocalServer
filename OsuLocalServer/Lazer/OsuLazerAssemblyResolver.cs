using System.Reflection;
using System.Runtime.Loader;

namespace OsuLocalServer.Lazer;

internal static class OsuLazerAssemblyResolver
{
    private static int registered;
    private static string? lazerDirectory;

    public static void Register(string lazerCurrentDirectory)
    {
        if (Interlocked.Exchange(ref registered, 1) == 1)
            return;

        lazerDirectory = lazerCurrentDirectory;
        AssemblyLoadContext.Default.Resolving += ResolveFromLazerCurrentDirectory;

        PreloadRulesetAssemblies(lazerCurrentDirectory);
    }

    private static void PreloadRulesetAssemblies(string lazerCurrentDirectory)
    {
        if (!Directory.Exists(lazerCurrentDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(lazerCurrentDirectory, "osu.Game.Rulesets*.dll", SearchOption.TopDirectoryOnly))
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                    string.Equals(a.GetName().Name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    }

    private static Assembly? ResolveFromLazerCurrentDirectory(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (lazerDirectory is null || !Directory.Exists(lazerDirectory))
            return null;

        // 1) 尝试从 lazer 目录加载
        var dllPath = TryFindDll(assemblyName.Name);
        if (dllPath is not null)
        {
            try
            {
                return context.LoadFromAssemblyPath(dllPath);
            }
            catch
            {
                // 版本不匹配等错误 — 继续尝试回退
            }
        }

        // 2) 回退：检查是否已有同名程序集已加载（例如来自 NuGet 包的不同版本）
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(asm.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return asm;
        }

        return null;
    }

    private static string? TryFindDll(string? assemblyName)
    {
        if (assemblyName is null || lazerDirectory is null)
            return null;

        var directPath = Path.Combine(lazerDirectory, $"{assemblyName}.dll");
        if (File.Exists(directPath))
            return directPath;

        return Directory
            .EnumerateFiles(lazerDirectory, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .FirstOrDefault();
    }
}
