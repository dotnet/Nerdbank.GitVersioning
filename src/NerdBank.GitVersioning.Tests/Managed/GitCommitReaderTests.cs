using System;
using System.IO;
using NerdBank.GitVersioning.Managed;
using Xunit;

namespace NerdBank.GitVersioning.Tests.Managed
{
    public class GitCommitReaderTests
    {
        [Fact]
        public void ReadTest()
        {
            using (Stream stream = File.OpenRead("Managed/commit-d56dc3ed179053abef2097d1120b4507769bcf1a.txt"))
            {
                var commit = GitCommitReader.Read(stream, GitObjectId.Parse("d56dc3ed179053abef2097d1120b4507769bcf1a"));

                Assert.Equal("d56dc3ed179053abef2097d1120b4507769bcf1a", commit.Sha.ToString());
                Assert.Equal("f914b48023c7c804a4f3be780d451f31aef74ac1", commit.Tree.ToString());

                Assert.Collection(
                    commit.Parents,
                    c => Assert.Equal("4497b0eaaa89abf0e6d70961ad5f04fd3a49cbc6", c.ToString()),
                    c => Assert.Equal("0989e8fe0cd0e0900173b26decdfb24bc0cc8232", c.ToString()));

                Assert.Equal("Andrew Arnott", commit.Author.Name);
                Assert.Equal(new DateTimeOffset(2020, 10, 6, 13, 40, 09, TimeSpan.FromHours(-6)), commit.Author.Date);
                Assert.Equal("andrewarnott@gmail.com", commit.Author.Email);

                // Committer and commit message are not read
            }
        }
    }
}
