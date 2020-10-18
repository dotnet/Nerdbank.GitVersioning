namespace NerdBank.GitVersioning.Managed
{
    internal struct DeltaInstruction
    {
        public DeltaInstructionType InstructionType;
        public int Offset;
        public int Size;
    }
}
