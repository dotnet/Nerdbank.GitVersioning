using System.IO;
using NerdBank.GitVersioning.Managed;
using Xunit;

namespace NerdBank.GitVersioning.Tests.Managed
{
    public class GitPackDeltafiedStreamTests
    {
        // Reconstructs an object by reading the base stream and the delta stream.
        // You can create delta representations of an object by running the
        // test tool which is located in the t/helper/ folder of the Git source repository.
        // Use with the delta -d [base file,in] [updated file,in] [delta file,out] arguments.
        [Theory]
        [InlineData("Managed/commit-4497b0eaaa89abf0e6d70961ad5f04fd3a49cbc6.txt", "Managed/commit.delta", "Managed/commit-d56dc3ed179053abef2097d1120b4507769bcf1a.txt")]
        [InlineData("Managed/tree-bb36cf0ca445ccc8e5ce9cc88f7cf74128e96dc9.txt", "Managed/tree.delta", "Managed/tree-f914b48023c7c804a4f3be780d451f31aef74ac1.txt")]
        public void TestDeltaStream(string basePath, string deltaPath, string expectedPath)
        {
            byte[] expected = File.ReadAllBytes(expectedPath);
            byte[] actual = new byte[expected.Length];

            using (Stream baseStream = File.OpenRead(basePath))
            using (Stream deltaStream = File.OpenRead(deltaPath))
            using (GitPackDeltafiedStream deltafiedStream = new GitPackDeltafiedStream(baseStream, deltaStream))
            {
                Assert.Equal(expected.Length, deltafiedStream.Length);

                deltafiedStream.Read(actual);

                Assert.Equal(expected, actual);
            }
        }
    }
}
