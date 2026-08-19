using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class DataProtectionTests
{
    [Fact]
    public void Keys_persist_between_providers_with_the_same_application_name()
    {
        var directory = Directory.CreateTempSubdirectory("morita-keys-");
        try
        {
            var first = DataProtectionProvider.Create(directory, configuration => configuration.SetApplicationName("Morita.LP.Razor"));
            var protectedValue = first.CreateProtector("tests").Protect("persisted");
            var second = DataProtectionProvider.Create(directory, configuration => configuration.SetApplicationName("Morita.LP.Razor"));
            Assert.Equal("persisted", second.CreateProtector("tests").Unprotect(protectedValue));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
