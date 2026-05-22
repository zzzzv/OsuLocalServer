using System.Reflection;
using System.Runtime.Loader;

internal static class OsuLazerAssemblyResolver
{
    private static int registered;
    private static string? lazerDirectory;

    public static void Register(string lazerCurrentDirectory)
    {
        if (Interlocked.Exchange(ref registered, 1) == 1)
        {
            return;
        }

        lazerDirectory = lazerCurrentDirectory;
        AssemblyLoadContext.Default.Resolving += ResolveFromLazerCurrentDirectory;
    }

    private static Assembly? ResolveFromLazerCurrentDirectory(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (lazerDirectory is null || !Directory.Exists(lazerDirectory))
        {
            return null;
        }

        var directPath = Path.Combine(lazerDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(directPath))
        {
            return context.LoadFromAssemblyPath(directPath);
        }

        var recursiveMatch = Directory
            .EnumerateFiles(lazerDirectory, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
            .FirstOrDefault();

        return recursiveMatch is null
            ? null
            : context.LoadFromAssemblyPath(recursiveMatch);
    }
}
