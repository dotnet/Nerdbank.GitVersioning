using System.IO;
using Xunit;
using NerdBank.GitVersioning.Managed;

namespace NerdBank.GitVersioning.Tests.Managed
{
    public class StreamExtensionsTests
    {
        [Fact]
        public void ReadTest()
        {
            byte[] data = new byte[] { 0b10010001, 0b00101110 };

            using (MemoryStream stream = new MemoryStream(data))
            {
                Assert.Equal(5905, stream.ReadMbsInt());
            }
        }
    }
}
