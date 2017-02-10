using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nerdbank.GitVersioning.Tests
{
    public class RepoFinderTests : RepoTestBase
    {
        private MethodInfo _findGitDirMi;
        private string _gitDir;

        public RepoFinderTests(ITestOutputHelper logger) : base(logger)
        {
            this.InitializeSourceControl();
            _findGitDirMi = typeof(VersionOracle).GetMethod("FindGitDir", BindingFlags.Static | BindingFlags.NonPublic);
            _gitDir = Path.Combine(this.RepoPath, ".git");
        }

        private string FindGitDir(string startingDir)
        {
            var result = _findGitDirMi.Invoke(null, new[] { startingDir });
            return (string)result;
        }

        private DirectoryInfo CreateSubDirectory()
        {
            return Directory.CreateDirectory(Path.Combine(this.RepoPath, Guid.NewGuid().ToString()));
        }

        [Fact]
        public void RepoRoot()
        {
            Assert.Equal(FindGitDir(this.RepoPath), _gitDir);
        }

        [Fact]
        public void SubDirectory()
        {
            var dir = CreateSubDirectory();
            Assert.Equal(FindGitDir(dir.FullName), _gitDir);
        }

        [Theory]
        [InlineData("")]
        [InlineData("askdjn")]
        [InlineData("gitdir: ../qwerty")]
        public void SubDirectoryWithInvalidGitFile(string contents)
        {
            var dir = CreateSubDirectory();
            File.WriteAllText(Path.Combine(dir.FullName, ".git"), contents);
            Assert.Equal(FindGitDir(dir.FullName), _gitDir);
        }

        [Fact]
        public void SubDirectoryWithValidGitFile()
        {
            var submodule = CreateSubDirectory();
            var gitDir = CreateSubDirectory();
            File.WriteAllText(Path.Combine(submodule.FullName, ".git"), $"gitdir: ../{gitDir.Name}");
            Assert.Equal(FindGitDir(submodule.FullName), gitDir.FullName);
        }
    }
}
