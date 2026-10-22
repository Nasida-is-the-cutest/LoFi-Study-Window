using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace IGPUSavior.Tests
{
    public class HarmonyRegistrationTests
    {
        [Fact]
        public void EveryAttributedPatchHasAnExplicitRegistration()
        {
            string root = SourceRoot();
            string entry = File.ReadAllText(Path.Combine(root, "Core", "PotatoPlugin.cs"));
            var registered = Regex.Matches(entry, @"typeof\((\w+)\)")
                .Select(match => match.Groups[1].Value).ToHashSet();
            var patches = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .SelectMany(path => Regex.Matches(File.ReadAllText(path),
                    @"\[HarmonyPatch(?:\([^\r\n]*\))?\]\s*(?:public|internal)?\s*(?:static|sealed)?\s*class\s+(\w+)")
                    .Select(match => match.Groups[1].Value)).ToArray();
            Assert.NotEmpty(patches);
            Assert.All(patches, patch => Assert.Contains(patch, registered));
        }

        private static string SourceRoot([CallerFilePath] string path = "")
            => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!, "iGPU Savior");
    }
}
