#nullable disable
using System;

namespace Sproto
{
    public class SprotoTypeReader
    {
        private byte[] buffer;
        private int begin;
        private int pos;
        private int size;

        public byte[] Buffer => buffer;

        public int Position => pos - begin;

        public int Offset => pos;

        public int Length => size - begin;

        public SprotoTypeReader(byte[] buffer, int offset, int size)
        {
            Init(buffer, offset, size);
        }

        public SprotoTypeReader()
        {
        }

        public void Init(byte[] buffer, int offset, int size)
        {
            begin = offset;
            pos = offset;
            this.buffer = buffer;
            this.size = offset + size;
            Check();
        }

        private void Check()
        {
            if (pos > size || begin > pos)
                SprotoTypeSize.error("invalid pos.");
        }

        public byte ReadByte()
        {
            Check();
            return buffer[pos++];
        }

        public void Seek(int offset)
        {
            pos = begin + offset;
            Check();
        }

        public void Read(byte[] data, int offset, int size)
        {
            int curPos = pos;
            pos += size;
            Check();

            for (int i = curPos; i < pos; i++)
                data[offset + i - curPos] = buffer[i];
        }
    }
}
