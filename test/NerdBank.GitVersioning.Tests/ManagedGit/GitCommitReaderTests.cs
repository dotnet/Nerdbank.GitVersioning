using System;
using System.IO;
using System.Linq;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit
{
    public class GitCommitReaderTests
    {
        [Fact]
        public void ReadTest()
        {
            using (Stream stream = TestUtilities.GetEmbeddedResource(@"ManagedGit\commit-d56dc3ed179053abef2097d1120b4507769bcf1a"))
            {
                var commit = GitCommitReader.Read(stream, GitObjectId.Parse("d56dc3ed179053abef2097d1120b4507769bcf1a"), readAuthor: true);

                Assert.Equal("d56dc3ed179053abef2097d1120b4507769bcf1a", commit.Sha.ToString());
                Assert.Equal("f914b48023c7c804a4f3be780d451f31aef74ac1", commit.Tree.ToString());

                Assert.Collection(
                    commit.Parents,
                    c => Assert.Equal("4497b0eaaa89abf0e6d70961ad5f04fd3a49cbc6", c.ToString()),
                    c => Assert.Equal("0989e8fe0cd0e0900173b26decdfb24bc0cc8232", c.ToString()));

                var author = commit.Author.Value;

                Assert.Equal("Andrew Arnott", author.Name);
                Assert.Equal(new DateTimeOffset(2020, 10, 6, 13, 40, 09, TimeSpan.FromHours(-6)), author.Date);
                Assert.Equal("andrewarnott@gmail.com", author.Email);

                // Committer and commit message are not read
            }
        }

        [Fact]
        public void ReadCommitWithThreeParents()
        {
            using (Stream stream = TestUtilities.GetEmbeddedResource(@"ManagedGit\commit-ab39e8acac105fa0db88514f259341c9f0201b22"))
            {
                var commit = GitCommitReader.Read(stream, GitObjectId.Parse("ab39e8acac105fa0db88514f259341c9f0201b22"), readAuthor: true);

                Assert.Equal(3, commit.Parents.Count());

                Assert.Collection(
                    commit.Parents,
                    c => Assert.Equal("e0b4d66ef7915417e04e88d5fa173185bb940029", c.ToString()),
                    c => Assert.Equal("10e67ce38fbee44b3f5584d4f9df6de6c5f4cc5c", c.ToString()),
                    c => Assert.Equal("a7fef320334121af85dce4b9b731f6c9a9127cfd", c.ToString()));
            }
        }
    }
}
