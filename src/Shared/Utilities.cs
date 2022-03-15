using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validation;

namespace Nerdbank.GitVersioning
{
    internal static class Utilities
    {
        private const int ProcessCannotAccessFileHR = unchecked((int)0x80070020);

        internal static void FileOperationWithRetry(Action operation)
        {
            Requires.NotNull(operation, nameof(operation));

            for (int retriesLeft = 6; retriesLeft > 0; retriesLeft--)
            {
                try
                {
                    operation();
                    break;
                }
                catch (IOException ex) when (ex.HResult == ProcessCannotAccessFileHR && retriesLeft > 0)
                {
                    Task.Delay(100).Wait();
                    continue;
                }
            }
        }
    }
}
