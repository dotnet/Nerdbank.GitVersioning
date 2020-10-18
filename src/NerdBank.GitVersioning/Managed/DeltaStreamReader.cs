using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    internal static class DeltaStreamReader
    {
        public static DeltaInstruction? Read(Stream stream)
        {
            int next = stream.ReadByte();

            if (next == -1)
            {
                return null;
            }

            byte instruction = (byte)next;

            DeltaInstruction value;
            value.Offset = 0;
            value.Size = 0;

            value.InstructionType = (DeltaInstructionType)((instruction & 0b1000_0000) >> 7);

            if (value.InstructionType == DeltaInstructionType.Insert)
            {
                value.Size = instruction & 0b0111_1111;
            }
            else if (value.InstructionType == DeltaInstructionType.Copy)
            {
                // offset1
                if ((instruction & 0b0000_0001) != 0)
                {
                    value.Offset |= (byte)stream.ReadByte();
                }

                // offset2
                if ((instruction & 0b0000_0010) != 0)
                {
                    value.Offset |= ((byte)stream.ReadByte() << 8);
                }

                // offset3
                if ((instruction & 0b0000_0100) != 0)
                {
                    value.Offset |= ((byte)stream.ReadByte() << 16);
                }

                // offset4
                if ((instruction & 0b0000_1000) != 0)
                {
                    value.Offset |= ((byte)stream.ReadByte() << 24);
                }

                // size1
                if ((instruction & 0b0001_0000) != 0)
                {
                    value.Size = (byte)stream.ReadByte();
                }

                // size2
                if ((instruction & 0b0010_0000) != 0)
                {
                    value.Size |= ((byte)stream.ReadByte() << 8);
                }

                // size3
                if ((instruction & 0b0100_0000) != 0)
                {
                    value.Size |= ((byte)stream.ReadByte() << 16);
                }
            }

            return value;
        }
    }
}
