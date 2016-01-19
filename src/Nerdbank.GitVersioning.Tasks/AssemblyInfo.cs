namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.CodeDom;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using PInvoke;

    public class AssemblyInfo : Task
    {
        private static readonly CodeGeneratorOptions codeGeneratorOptions = new CodeGeneratorOptions
        {
            BlankLinesBetweenMembers = false,
            IndentString = "    ",
        };

        private CodeCompileUnit generatedFile;

        [Required]
        public string CodeLanguage { get; set; }

        [Required]
        public string OutputFile { get; set; }

        public string AssemblyName { get; set; }

        public string AssemblyVersion { get; set; }

        public string AssemblyFileVersion { get; set; }

        public string AssemblyInformationalVersion { get; set; }

        public string RootNamespace { get; set; }

        public string AssemblyOriginatorKeyFile { get; set; }

        public string AssemblyKeyContainerName { get; set; }

        public string AssemblyTitle { get; set; }

        public string AssemblyProduct { get; set; }

        public string AssemblyCopyright { get; set; }

        public string AssemblyCompany { get; set; }

        public string AssemblyConfiguration { get; set; }

        public override bool Execute()
        {
            using (var codeDomProvider = CodeDomProvider.CreateProvider(this.CodeLanguage))
            {
                this.generatedFile = new CodeCompileUnit();
                this.generatedFile.AssemblyCustomAttributes.AddRange(this.CreateAssemblyAttributes().ToArray());

                var ns = new CodeNamespace();
                this.generatedFile.Namespaces.Add(ns);
                ns.Types.Add(this.CreateThisAssemblyClass());

                Directory.CreateDirectory(Path.GetDirectoryName(this.OutputFile));
                using (var fileWriter = new StreamWriter(File.OpenWrite(this.OutputFile), new UTF8Encoding(true)))
                {
                    codeDomProvider.GenerateCodeFromCompileUnit(this.generatedFile, fileWriter, codeGeneratorOptions);
                }

                return !this.Log.HasLoggedErrors;
            }
        }

        private CodeTypeDeclaration CreateThisAssemblyClass()
        {
            var thisAssembly = new CodeTypeDeclaration("ThisAssembly")
            {
                IsClass = true,
                IsPartial = true,
                TypeAttributes = TypeAttributes.NotPublic | TypeAttributes.Sealed,
            };

            // CodeDOM doesn't support static classes, so hide the constructor instead.
            thisAssembly.Members.Add(new CodeConstructor { Attributes = MemberAttributes.Private });

            // Determine information about the public key used in the assembly name.
            string publicKey, publicKeyToken;
            this.ReadKeyInfo(out publicKey, out publicKeyToken);

            // Define the constants.
            thisAssembly.Members.AddRange(CreateFields(new Dictionary<string, string>
            {
                { "AssemblyVersion", this.AssemblyVersion },
                { "AssemblyFileVersion", this.AssemblyFileVersion },
                { "AssemblyInformationalVersion", this.AssemblyInformationalVersion },
                { "AssemblyName", this.AssemblyName },
                { "PublicKey", publicKey },
                { "PublicKeyToken", publicKeyToken },
                { "AssemblyTitle", this.AssemblyTitle },
                { "AssemblyProduct", this.AssemblyProduct },
                { "AssemblyCopyright", this.AssemblyCopyright },
                { "AssemblyCompany", this.AssemblyCompany },
                { "AssemblyConfiguration", this.AssemblyConfiguration }
            }).ToArray());

            // These properties should be defined even if they are empty.
            thisAssembly.Members.Add(CreateField("RootNamespace", this.RootNamespace));

            return thisAssembly;
        }

        private void ReadKeyInfo(out string publicKey, out string publicKeyToken)
        {
            byte[] publicKeyBytes = null;
            if (!string.IsNullOrEmpty(this.AssemblyOriginatorKeyFile) && File.Exists(this.AssemblyOriginatorKeyFile))
            {
                if (Path.GetExtension(this.AssemblyOriginatorKeyFile).Equals(".snk", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] keyBytes = File.ReadAllBytes(this.AssemblyOriginatorKeyFile);
                    bool publicKeyOnly = keyBytes[0] != 0x07;
                    publicKeyBytes = publicKeyOnly ? keyBytes : MSCorEE.StrongNameGetPublicKey(keyBytes);
                }
            }
            else if (!string.IsNullOrEmpty(this.AssemblyKeyContainerName))
            {
                publicKeyBytes = MSCorEE.StrongNameGetPublicKey(this.AssemblyKeyContainerName);
            }

            if (publicKeyBytes != null)
            {
                publicKey = ToHex(publicKeyBytes);
                publicKeyToken = ToHex(MSCorEE.StrongNameTokenFromPublicKey(publicKeyBytes));
            }
            else
            {
                publicKey = null;
                publicKeyToken = null;
            }
        }

        private static string ToHex(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
        }

        private IEnumerable<CodeAttributeDeclaration> CreateAssemblyAttributes()
        {
            yield return DeclareAttribute(typeof(AssemblyVersionAttribute), this.AssemblyVersion);
            yield return DeclareAttribute(typeof(AssemblyFileVersionAttribute), this.AssemblyFileVersion);
            yield return DeclareAttribute(typeof(AssemblyInformationalVersionAttribute), this.AssemblyInformationalVersion);
            if (!string.IsNullOrEmpty(this.AssemblyTitle))
                yield return DeclareAttribute(typeof(AssemblyTitleAttribute), this.AssemblyTitle);
            if (!string.IsNullOrEmpty(this.AssemblyProduct))
                yield return DeclareAttribute(typeof(AssemblyProductAttribute), this.AssemblyProduct);
            if (!string.IsNullOrEmpty(this.AssemblyCompany))
                yield return DeclareAttribute(typeof(AssemblyCompanyAttribute), this.AssemblyCompany);
            if (!string.IsNullOrEmpty(this.AssemblyCopyright))
                yield return DeclareAttribute(typeof(AssemblyCopyrightAttribute), this.AssemblyCopyright);
        }

        private static IEnumerable<CodeMemberField> CreateFields(IReadOnlyDictionary<string, string> namesAndValues)
        {
            foreach (var item in namesAndValues)
            {
                if (!string.IsNullOrEmpty(item.Value))
                {
                    yield return CreateField(item.Key, item.Value);
                }
            }
        }

        private static CodeMemberField CreateField(string name, string value)
        {
            return new CodeMemberField(typeof(string), name)
            {
                Attributes = MemberAttributes.Const | MemberAttributes.Assembly,
                InitExpression = new CodePrimitiveExpression(value),
            };
        }

        private static CodeAttributeDeclaration DeclareAttribute(Type attributeType, params CodeAttributeArgument[] arguments)
        {
            var assemblyTypeReference = new CodeTypeReference(attributeType);
            return new CodeAttributeDeclaration(assemblyTypeReference, arguments);
        }

        private static CodeAttributeDeclaration DeclareAttribute(Type attributeType, params string[] arguments)
        {
            return DeclareAttribute(
                attributeType,
                arguments.Select(a => new CodeAttributeArgument(new CodePrimitiveExpression(a))).ToArray());
        }
    }
}
