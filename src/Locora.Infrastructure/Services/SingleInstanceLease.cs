using System.Security.Cryptography;
using System.Text;

namespace Locora.Infrastructure.Services;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex _mutex;
    private bool _released;

    private SingleInstanceLease(
        Mutex mutex,
        string mutexName,
        string rootPath,
        bool ownsMutex,
        bool timedOut)
    {
        _mutex = mutex;
        MutexName = mutexName;
        RootPath = rootPath;
        OwnsMutex = ownsMutex;
        TimedOut = timedOut;
    }

    public string MutexName { get; }

    public string RootPath { get; }

    public bool OwnsMutex { get; }

    public bool TimedOut { get; }

    public static SingleInstanceLease Acquire(string processKey, TimeSpan timeout)
    {
        var rootPath = ResolveRootPath();
        var mutexName = CreateMutexName(processKey, rootPath);
        var mutex = new Mutex(false, mutexName);

        try
        {
            var ownsMutex = mutex.WaitOne(timeout);
            return new SingleInstanceLease(mutex, mutexName, rootPath, ownsMutex, timedOut: !ownsMutex);
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceLease(mutex, mutexName, rootPath, ownsMutex: true, timedOut: false);
        }
    }

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        if (OwnsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _released = true;
    }

    private static string ResolveRootPath()
    {
        var root = Environment.GetEnvironmentVariable("LOCORA_ROOT") ?? AppContext.BaseDirectory;
        return Path.GetFullPath(root);
    }

    private static string CreateMutexName(string processKey, string rootPath)
    {
        var normalizedRoot = rootPath.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..12];
        return $@"Local\{processKey}.{hash}";
    }
}
