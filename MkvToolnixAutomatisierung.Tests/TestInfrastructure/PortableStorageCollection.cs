using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.TestInfrastructure;

[CollectionDefinition("PortableStorage", DisableParallelization = true)]
public sealed class PortableStorageCollection : ICollectionFixture<PortableStorageFixture>
{
}

public sealed class PortableStorageFixture : IDisposable
{
    public PortableStorageFixture()
    {
        Reset();
    }

    public void Reset()
    {
        DeleteDirectoryIfExists(PortableAppStorage.DataDirectory);
        DeleteDirectoryIfExists(PortableAppStorage.LogsDirectory);
    }

    public void Dispose()
    {
        Reset();
    }

    private static void DeleteDirectoryIfExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        Directory.Delete(directoryPath, recursive: true);
    }
}
