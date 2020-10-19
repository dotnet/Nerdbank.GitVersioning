using System;

namespace NerdBank.GitVersioning.Managed
{
    struct GitSignature
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public DateTimeOffset Date { get; set; }
    }
}
