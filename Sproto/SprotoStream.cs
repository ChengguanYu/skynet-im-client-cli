using System;
using System.IO;

namespace Sproto
{
    public class SprotoStream
    {
        private int size;
        private int pos;
        private byte[] buffer;

        public int Position => pos;
        public byte[] Buffer => buffer;

        public SprotoStream()
        {
            size = 128;
            pos = 0;
            buffer = new byte[size];
        }

        private void Expand(int sz = 0)
        {
            if (size - pos < sz)
            {
                long bakSz = size;
                while (size - pos < sz)
                    size *= 2;

                if (size >= SprotoTypeSize.encode_max_size)
                    SprotoTypeSize.error("object is too large (>" + SprotoTypeSize.encode_max_size + ")");

                var newBuffer = new byte[size];
                for (long i = 0; i < bakSz; i++)
                    newBuffer[i] = buffer[i];
                buffer = newBuffer;
            }
        }

        public void WriteByte(byte v)
        {
            Expand(sizeof(byte));
            buffer[pos++] = v;
        }

        public void Write(byte[] data, int offset, int count)
        {
            Expand(count);
            for (int i = 0; i < count; i++)
                buffer[pos++] = data[offset + i];
        }

        public int Seek(int offset, SeekOrigin loc)
        {
            pos = loc switch
            {
                SeekOrigin.Begin   => offset,
                SeekOrigin.Current => pos + offset,
                SeekOrigin.End     => size + offset,
                _ => pos
            };
            Expand();
            return pos;
        }

        public void Read(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                buffer[offset + i] = this.buffer[pos++];
        }

        public void MoveUp(int position, int upCount)
        {
            if (upCount <= 0) return;
            long count = pos - position;
            for (int i = 0; i < count; i++)
                buffer[position - upCount + i] = buffer[position + i];
            pos -= upCount;
        }

        public byte this[int i]
        {
            get
            {
                if (i < 0 || i >= size)
                    throw new Exception("invalid idx:" + i + "@get");
                return buffer[i];
            }
            set
            {
                if (i < 0 || i >= size)
                    throw new Exception("invalid idx:" + i + "@set");
                buffer[i] = value;
            }
        }
    }
}
