using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Nerdbank.GitVersioning;

namespace NerdBank.GitVersioning.Managed
{
    internal static class VersionFile
    {
        const bool ClearArray =
#if DEBUG
            true;
#else
            false;
#endif

        public static VersionOptions GetVersionOptions(GitRepository repository, GitCommit commit, string relativeRepoProjectDirectory)
        {
            Stack<string> directories = new Stack<string>();
            string currentDirectory = relativeRepoProjectDirectory;

            while (currentDirectory != null)
            {
                directories.Push(Path.GetFileName(currentDirectory));
                currentDirectory = Path.GetDirectoryName(currentDirectory);
            }

            var tree = commit.Tree;

            using (Stream treeStream = repository.GetObjectBySha(tree, "tree", false))
            {
                GitTreeStreamingReader.FindNode(treeStream, Encoding.UTF8.GetBytes(currentDirectory));
            }

            throw new NotImplementedException();
        }

        public static VersionOptions GetVersionOptions(string path)
        {
            throw new NotImplementedException();
        }

        public static string GetVersion(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return GetVersion(stream);
            }
        }

        public static string GetVersion(Stream stream)
        {
            string value = null;

            byte[] data = ArrayPool<byte>.Shared.Rent((int)stream.Length);
            stream.Read(data);

            var span = data.AsSpan(0, (int)stream.Length);
            var reader = new Utf8JsonReader(span, isFinalBlock: true, default);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.ValueTextEquals("version"))
                {
                    reader.Read();
                    value = reader.GetString();
                    break;
                }
            }

            ArrayPool<byte>.Shared.Return(data, clearArray: ClearArray);

            return value;
        }
    }
}
